using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core.Audio;
using ZombieSandbox.Core.Input;

namespace ZombieSandbox.Combat;

/// <summary>Everything needed to launch one bullet.</summary>
public readonly record struct Shot(
    Vector3 Origin,
    Vector3 Velocity,
    AmmoData Ammo,
    /// <summary>Where the barrel line starts inside the shooter (eye/shoulder). The bullet can't pass a wall between here and the muzzle.</summary>
    Vector3 SafeOrigin,
    /// <summary>The sight line at the moment of firing (for measuring drop).</summary>
    Vector3 SightOrigin,
    Vector3 SightDirection,
    WeaponHolder? Shooter,
    /// <summary>The shooter's own body and hitboxes: bullets pass through them.</summary>
    Array<Rid> Exclude,
    uint CollisionMask);

/// <summary>What the last bullet did (shown on the debug overlay).</summary>
public readonly record struct ImpactReport(Vector3 Position, float Distance, float Speed, float FlightTime, float DropFromSightLine, bool HitTarget);

/// <summary>
/// Autoload. Simulates every bullet in flight: gravity, air drag, and a ray test along each step's
/// path so nothing is skipped at 900 m/s. Bullets are plain data, not nodes, so hundreds are cheap.
/// Also owns impact effects (holes, dust, sound) and the debug bullet trails (F7).
/// </summary>
public partial class Ballistics : Node3D
{
    private const int MaxHoles = 300;
    private const int PuffPoolSize = 16;
    private const int ImpactAudioPoolSize = 8;
    private const float TrailLifetime = 4f;
    private const int MaxTrailSegments = 4000;

    public static Ballistics Instance { get; private set; } = null!;

    public ImpactReport? LastImpact { get; private set; }
    public bool ShowTrails { get; private set; }
    public int BulletsInFlight => _bullets.Count;

    private struct Bullet
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Time;
        public float Distance;
        public Shot Shot;
    }

    private readonly List<Bullet> _bullets = new();
    private readonly PhysicsRayQueryParameters3D _ray = new();
    private readonly Array<Rid> _exclude = new();
    private float _gravity;

    private readonly List<MeshInstance3D> _holes = new();
    private int _nextHole;
    private QuadMesh _holeMesh = null!;
    private readonly List<CpuParticles3D> _puffs = new();
    private int _nextPuff;
    private readonly List<AudioStreamPlayer3D> _impactAudio = new();
    private int _nextImpactAudio;

    private readonly List<(Vector3 From, Vector3 To, float Born)> _trail = new();
    private MeshInstance3D _trailMesh = null!;
    private ImmediateMesh _trailImmediate = null!;
    private float _clock;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        _ray.Exclude = _exclude;

        _holeMesh = new QuadMesh
        {
            Size = new Vector2(0.012f, 0.012f),
            Material = new StandardMaterial3D { AlbedoColor = new Color(0.04f, 0.04f, 0.04f), Roughness = 1f },
        };

        for (int i = 0; i < PuffPoolSize; i++)
            _puffs.Add(CreatePuff());

        for (int i = 0; i < ImpactAudioPoolSize; i++)
        {
            var player = new AudioStreamPlayer3D { Stream = PlaceholderSounds.Impact, UnitSize = 3f, MaxDistance = 80f };
            AddChild(player);
            _impactAudio.Add(player);
        }

        _trailImmediate = new ImmediateMesh();
        _trailMesh = new MeshInstance3D
        {
            Mesh = _trailImmediate,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        AddChild(_trailMesh);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugBulletTrails))
        {
            ShowTrails = !ShowTrails;
            if (!ShowTrails)
            {
                _trail.Clear();
                _trailImmediate.ClearSurfaces();
            }
        }
    }

    public void Fire(in Shot shot)
    {
        // A wall between the shooter's body and the muzzle stops the bullet right there.
        if (CastSegment(shot, shot.SafeOrigin, shot.Origin, out Dictionary blocked))
        {
            Impact(shot, blocked, shot.Velocity, distance: 0f, time: 0f);
            return;
        }

        _bullets.Add(new Bullet { Position = shot.Origin, Velocity = shot.Velocity, Shot = shot });
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _clock += dt;

        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = _bullets[i];
            Vector3 start = b.Position;
            (Vector3 end, Vector3 endVelocity) = Step(b.Position, b.Velocity, b.Shot.Ammo.DragPerMeter, _gravity, dt);

            if (CastSegment(b.Shot, start, end, out Dictionary hit))
            {
                Vector3 hitPoint = hit["position"].AsVector3();
                float fraction = start.DistanceTo(end) > 0f ? start.DistanceTo(hitPoint) / start.DistanceTo(end) : 0f;
                AddTrail(start, hitPoint);
                Impact(b.Shot, hit, b.Velocity.Lerp(endVelocity, fraction), b.Distance + start.DistanceTo(hitPoint), b.Time + dt * fraction);
                RemoveAt(i);
                continue;
            }

            AddTrail(start, end);
            b.Distance += start.DistanceTo(end);
            b.Position = end;
            b.Velocity = endVelocity;
            b.Time += dt;
            if (b.Time > b.Shot.Ammo.MaxFlightTime)
                RemoveAt(i);
            else
                _bullets[i] = b;
        }

        if (ShowTrails)
            DrawTrails();
    }

    /// <summary>One integration step: gravity plus drag proportional to speed squared.</summary>
    public static (Vector3 Position, Vector3 Velocity) Step(Vector3 position, Vector3 velocity, float dragPerMeter, float gravity, float dt)
    {
        Vector3 acceleration = Vector3.Down * gravity - velocity * (dragPerMeter * velocity.Length());
        Vector3 newVelocity = velocity + acceleration * dt;
        return (position + (velocity + newVelocity) * 0.5f * dt, newVelocity);
    }

    /// <summary>
    /// Finds the upward angle (radians) the bore must have so a bullet starting <paramref name="sightHeight"/>
    /// below the sight line crosses it again at <paramref name="zeroDistance"/>.
    /// </summary>
    public static float SolveZeroAngle(AmmoData ammo, float sightHeight, float zeroDistance, float gravity)
    {
        const float dt = 1f / 480f;
        float low = 0f, high = 0.02f;
        for (int iteration = 0; iteration < 30; iteration++)
        {
            float angle = (low + high) * 0.5f;
            Vector3 position = Vector3.Zero;
            Vector3 velocity = new(0f, Mathf.Sin(angle) * ammo.MuzzleVelocity, -Mathf.Cos(angle) * ammo.MuzzleVelocity);
            float time = 0f;
            while (-position.Z < zeroDistance && time < ammo.MaxFlightTime)
            {
                (position, velocity) = Step(position, velocity, ammo.DragPerMeter, gravity, dt);
                time += dt;
            }
            if (position.Y > sightHeight)
                high = angle;
            else
                low = angle;
        }
        return (low + high) * 0.5f;
    }

    private bool CastSegment(in Shot shot, Vector3 from, Vector3 to, out Dictionary hit)
    {
        _ray.Exclude = shot.Exclude ?? _exclude;
        _ray.From = from;
        _ray.To = to;
        _ray.CollisionMask = shot.CollisionMask;
        hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        return hit.Count > 0;
    }

    private void Impact(in Shot shot, Dictionary hit, Vector3 velocity, float distance, float time)
    {
        Vector3 position = hit["position"].AsVector3();
        Vector3 normal = hit["normal"].AsVector3();
        GodotObject? collider = hit["collider"].AsGodotObject();

        bool hitTarget = false;
        if (collider is IHittable hittable)
            hitTarget = hittable.OnBulletHit(new BulletHit(position, normal, velocity, distance, shot.Ammo, shot.Shooter));

        shot.Shooter?.OnShotLanded(distance, hitTarget);

        LastImpact = new ImpactReport(position, distance, velocity.Length(), time, DropFromSightLine(shot, position), hitTarget);

        PlaceHole(position, normal, collider as Node3D);
        PlayPuff(position, normal);
        AudioStreamPlayer3D audio = _impactAudio[_nextImpactAudio];
        _nextImpactAudio = (_nextImpactAudio + 1) % _impactAudio.Count;
        audio.GlobalPosition = position;
        audio.Play();
    }

    private static float DropFromSightLine(in Shot shot, Vector3 point)
    {
        Vector3 direction = shot.SightDirection.Normalized();
        Vector3 offset = point - shot.SightOrigin;
        Vector3 perpendicular = offset - direction * offset.Dot(direction);
        Vector3 up = (Vector3.Up - direction * Vector3.Up.Dot(direction)).Normalized();
        return perpendicular.Dot(up);
    }

    private void PlaceHole(Vector3 position, Vector3 normal, Node3D? surface)
    {
        MeshInstance3D hole;
        if (_holes.Count < MaxHoles)
        {
            hole = new MeshInstance3D { Mesh = _holeMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(hole);
            _holes.Add(hole);
        }
        else
        {
            hole = _holes[_nextHole];
            if (!IsInstanceValid(hole))
            {
                hole = new MeshInstance3D { Mesh = _holeMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(hole);
                _holes[_nextHole] = hole;
            }
            _nextHole = (_nextHole + 1) % MaxHoles;
        }

        // Stick to whatever was hit so holes move with moving targets.
        Node newParent = surface != null && IsInstanceValid(surface) ? surface : this;
        if (hole.GetParent() != newParent)
            hole.Reparent(newParent, keepGlobalTransform: false);

        Vector3 up = Mathf.Abs(normal.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        // QuadMesh faces +Z: point +Z along the surface normal, just off the surface.
        hole.GlobalTransform = new Transform3D(Basis.LookingAt(-normal, up), position + normal * 0.002f);
    }

    private CpuParticles3D CreatePuff()
    {
        var puff = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = 10,
            Lifetime = 0.45f,
            Explosiveness = 0.95f,
            Direction = Vector3.Back, // Local +Z, aimed along the surface normal below.
            Spread = 35f,
            InitialVelocityMin = 0.6f,
            InitialVelocityMax = 1.8f,
            Gravity = new Vector3(0f, -2f, 0f),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            Mesh = new SphereMesh
            {
                Radius = 0.015f,
                Height = 0.03f,
                RadialSegments = 6,
                Rings = 3,
                Material = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.52f, 0.48f), Roughness = 1f },
            },
        };
        AddChild(puff);
        return puff;
    }

    private void PlayPuff(Vector3 position, Vector3 normal)
    {
        CpuParticles3D puff = _puffs[_nextPuff];
        _nextPuff = (_nextPuff + 1) % _puffs.Count;
        Vector3 up = Mathf.Abs(normal.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        puff.GlobalTransform = new Transform3D(Basis.LookingAt(-normal, up), position + normal * 0.01f);
        puff.Restart();
    }

    private void RemoveAt(int index)
    {
        int last = _bullets.Count - 1;
        _bullets[index] = _bullets[last];
        _bullets.RemoveAt(last);
    }

    // ---------- Debug trails ----------

    private void AddTrail(Vector3 from, Vector3 to)
    {
        if (!ShowTrails)
            return;
        if (_trail.Count >= MaxTrailSegments)
            _trail.RemoveAt(0);
        _trail.Add((from, to, _clock));
    }

    private void DrawTrails()
    {
        _trail.RemoveAll(s => _clock - s.Born > TrailLifetime);
        _trailImmediate.ClearSurfaces();
        if (_trail.Count == 0)
            return;

        _trailImmediate.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach ((Vector3 from, Vector3 to, float born) in _trail)
        {
            var color = new Color(1f, 0.85f, 0.2f, 1f - (_clock - born) / TrailLifetime);
            _trailImmediate.SurfaceSetColor(color);
            _trailImmediate.SurfaceAddVertex(from);
            _trailImmediate.SurfaceSetColor(color);
            _trailImmediate.SurfaceAddVertex(to);
        }
        _trailImmediate.SurfaceEnd();
    }
}
