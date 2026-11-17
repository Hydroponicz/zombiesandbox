using Godot;
using Godot.Collections;
using ZombieSandbox.World;

namespace ZombieSandbox.AI;

/// <summary>
/// Sight and hearing for any AI. Place at the eyes; it faces the owner's -Z.
/// Sight: range shrinks at night and for targets that are crouched, prone or still; spotting takes a
/// moment unless the target is close. Hearing: every <see cref="GameNoise"/> event within its radius is
/// heard, with a position error that grows with distance.
/// </summary>
public partial class Perception : Node3D
{
    [Export] public float SightRangeDay { get; set; } = 25f;
    [Export] public float SightRangeNight { get; set; } = 8f;
    [Export] public float FieldOfViewDegrees { get; set; } = 120f;
    /// <summary>Anything this close is noticed in any direction (heard/felt).</summary>
    [Export] public float ProximityRange { get; set; } = 2f;
    /// <summary>Seconds to notice a target at the edge of sight range (much faster up close).</summary>
    [Export] public float DetectionTime { get; set; } = 1.5f;
    [Export] public float HearingMultiplier { get; set; } = 1f;
    [Export] public float UpdateInterval { get; set; } = 0.15f;

    /// <summary>The target currently in view (once fully noticed).</summary>
    public ISenseTarget? Target
    {
        get => SenseTargets.IsValid(_target) ? _target : null; // Freed targets (a corpse that rose) vanish.
        private set => _target = value;
    }
    private ISenseTarget? _target;
    public bool CanSeeTarget
    {
        get => _canSeeTarget && Target != null;
        private set => _canSeeTarget = value;
    }
    private bool _canSeeTarget;
    /// <summary>0..1 how close to noticing the target in view.</summary>
    public float Awareness { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    /// <summary>How the target was moving when last seen (horizontal, m/s): used to guess where it went.</summary>
    public Vector3 LastSeenVelocity { get; private set; }
    /// <summary>Time (seconds since startup) the target was last seen.</summary>
    public double LastSeenTime { get; private set; }
    private bool _sawLastUpdate;

    /// <summary>Most recent sound heard (estimated position).</summary>
    public Vector3 HeardPosition { get; private set; }
    /// <summary>Increments each time something is heard, so the owner can tell new from old.</summary>
    public int HeardCount { get; private set; }

    /// <summary>The owner's own colliders/hitboxes, ignored by line-of-sight rays.</summary>
    public Array<Rid> SelfExclude { get; } = new();
    /// <summary>The owner (never its own target).</summary>
    public ISenseTarget? Self { get; set; }
    /// <summary>Which targets this AI cares about (zombies: the living; survivors: enemies). Null = all.</summary>
    public System.Func<ISenseTarget, bool>? TargetFilter { get; set; }
    public bool Enabled { get; set; } = true;

    private readonly PhysicsRayQueryParameters3D _ray = new();
    private readonly Array<Rid> _rayExclude = new();
    private readonly RandomNumberGenerator _rng = new();
    private float _timer;

    public override void _EnterTree() => GameNoise.Emitted += OnNoise;

    public override void _ExitTree() => GameNoise.Emitted -= OnNoise;

    public override void _Ready()
    {
        _rng.Randomize();
        _timer = _rng.Randf() * UpdateInterval; // Stagger updates across many AIs.
        _ray.CollisionMask = 1; // World only: walls block sight, bodies don't.
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled)
        {
            CanSeeTarget = false;
            Awareness = 0f;
            return;
        }
        _timer -= (float)delta;
        if (_timer > 0f)
            return;
        float step = UpdateInterval - _timer;
        _timer = UpdateInterval;
        UpdateSight(step);
    }

    private void UpdateSight(float dt)
    {
        float daylight = WorldClock.Instance?.Daylight ?? 1f;
        float baseRange = Mathf.Lerp(SightRangeNight, SightRangeDay, daylight);
        Vector3 eye = GlobalPosition;
        Vector3 forward = -GlobalBasis.Z;
        forward.Y = 0f;
        forward = forward.Normalized();

        ISenseTarget? best = null;
        float bestCloseness = 0f;
        foreach (ISenseTarget target in SenseTargets.All)
        {
            if (!target.IsAlive || target == Self || (TargetFilter != null && !TargetFilter(target)))
                continue;
            Vector3 to = target.SightPoint - eye;
            float distance = to.Length();
            float range = baseRange * target.Visibility;
            bool close = distance <= ProximityRange;
            if (!close && distance > range)
                continue;

            Vector3 flat = new(to.X, 0f, to.Z);
            if (!close && flat.LengthSquared() > 0.0001f && forward.AngleTo(flat) > Mathf.DegToRad(FieldOfViewDegrees * 0.5f))
                continue;
            if (!HasLineOfSight(eye, target))
                continue;

            float closeness = close ? 1f : 1f - distance / range;
            if (closeness > bestCloseness)
            {
                best = target;
                bestCloseness = closeness;
            }
        }

        if (best != null)
        {
            // Noticing is quick up close, slow at the edge of sight.
            Awareness = Mathf.Min(1f, Awareness + dt / DetectionTime * (1f + 4f * bestCloseness));
            if (Awareness >= 1f)
            {
                Target = best;
                CanSeeTarget = true;
                Vector3 position = best.GroundPosition;
                if (_sawLastUpdate && dt > 0f)
                {
                    Vector3 velocity = (position - LastSeenPosition) / dt;
                    velocity.Y = 0f;
                    LastSeenVelocity = LastSeenVelocity.Lerp(velocity, 0.5f);
                }
                LastSeenPosition = position;
                LastSeenTime = Time.GetTicksMsec() / 1000.0;
                _sawLastUpdate = true;
            }
        }
        else
        {
            CanSeeTarget = false;
            _sawLastUpdate = false;
            Awareness = Mathf.Max(0f, Awareness - dt * 0.5f);
        }
    }

    private bool HasLineOfSight(Vector3 eye, ISenseTarget target)
    {
        _rayExclude.Clear();
        _rayExclude.AddRange(SelfExclude);
        _rayExclude.AddRange(target.SightExclude);
        _ray.Exclude = _rayExclude;
        _ray.From = eye;
        _ray.To = target.SightPoint;
        return GetWorld3D().DirectSpaceState.IntersectRay(_ray).Count == 0;
    }

    private void OnNoise(NoiseEvent noise)
    {
        if (!Enabled || !IsInsideTree())
            return;
        float distance = GlobalPosition.DistanceTo(noise.Position);
        float radius = noise.Radius * HearingMultiplier;
        if (distance > radius)
            return;

        // Faint, distant sounds are hard to place exactly.
        float error = distance * 0.25f * (distance / radius);
        float angle = _rng.Randf() * Mathf.Tau;
        HeardPosition = noise.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * error * _rng.Randf();
        HeardCount++;
    }
}
