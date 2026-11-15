using Godot;
using ZombieSandbox.Combat;
using ZombieSandbox.Core.Audio;
using ZombieSandbox.Health;
using ZombieSandbox.World;

namespace ZombieSandbox.AI;

public enum ZombieState
{
    Wander,
    Investigate,
    Chase,
    Search,
    Dead,
}

/// <summary>
/// A zombie: wanders, investigates sounds, chases what it sees, searches where it lost you.
/// Killed by destroying the head, or by a destroyed chest (it bleeds out after a stagger).
/// Destroyed legs turn it into a crawler. Attacks come in piece B.
/// </summary>
public partial class Zombie : CharacterBody3D
{
    public const string Group = "zombies";

    [Export] public ZombieType Type { get; set; } = null!;

    public ZombieState State { get; private set; } = ZombieState.Wander;
    public bool IsCrawler { get; private set; }
    public BodyHealth Health { get; private set; } = null!;
    public Perception Perception { get; private set; } = null!;

    private NavigationAgent3D _agent = null!;
    private Node3D _visual = null!;
    private CollisionShape3D _collider = null!;
    private AimAssistTarget? _assistTarget;
    private Label3D? _debugLabel;
    private AudioStreamPlayer3D _voice = null!;
    private readonly RandomNumberGenerator _rng = new();

    private float _gravity;
    private float _chaseSpeed;
    private float _staggerLeft;
    private float _stateTimer;
    private float _idleTimer;
    private float _repathTimer;
    private float _groanTimer;
    private int _lastHeardCount;
    private bool _hasDestination;
    private bool _navReady => RuntimeNavigationBaker.MapReady;
    private float _noPathTime;
    private float _deathTime;

    public override void _Ready()
    {
        AddToGroup(Group);
        _rng.Randomize();
        Health = GetNode<BodyHealth>("Health");
        Perception = GetNode<Perception>("Perception");
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _visual = GetNode<Node3D>("Visual");
        _collider = GetNode<CollisionShape3D>("CollisionShape3D");
        _assistTarget = GetNodeOrNull<AimAssistTarget>("AimAssistTarget");
        _debugLabel = GetNodeOrNull<Label3D>("DebugLabel");
        if (_debugLabel != null)
            _debugLabel.Visible = OS.IsDebugBuild();

        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        _chaseSpeed = _rng.RandfRange(Type.ChaseSpeedMin, Type.ChaseSpeedMax);

        Perception.SightRangeDay = Type.SightRangeDay;
        Perception.SightRangeNight = Type.SightRangeNight;
        Perception.FieldOfViewDegrees = Type.FieldOfViewDegrees;
        Perception.HearingMultiplier = Type.HearingMultiplier;
        Perception.SelfExclude.Add(GetRid());
        GetNode<HitboxRig>("Visual/Hitboxes").AddRidsTo(Perception.SelfExclude);

        Health.Damaged += (_, _) => _staggerLeft = Type.StaggerSeconds;
        Health.Died += OnDied;

        _voice = new AudioStreamPlayer3D { Stream = PlaceholderSounds.ZombieGroan, UnitSize = 4f, MaxDistance = 45f, PitchScale = _rng.RandfRange(0.8f, 1.15f) };
        AddChild(_voice);
        _groanTimer = _rng.RandfRange(2f, 10f);
        _idleTimer = _rng.RandfRange(0f, 3f);

    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;
        if (!IsOnFloor())
            velocity.Y -= _gravity * dt;

        if (State == ZombieState.Dead)
        {
            _deathTime += dt;
            _visual.Rotation = new Vector3(Mathf.DegToRad(-85f) * Mathf.Min(_deathTime * 2.5f, 1f), 0f, 0f);
            velocity.X = velocity.Z = 0f;
            Velocity = velocity;
            MoveAndSlide();
            return;
        }

        UpdateCrawler();
        Think(dt);
        GiveUpOnUnreachableDestination(dt);

        // Move along the path.
        float speed = CurrentSpeed();
        Vector3 desired = Vector3.Zero;
        if (_hasDestination && _navReady && !_agent.IsNavigationFinished())
        {
            Vector3 next = _agent.GetNextPathPosition();
            Vector3 to = next - GlobalPosition;
            to.Y = 0f;
            if (to.LengthSquared() > 0.0004f)
                desired = to.Normalized() * speed;
        }

        // Stop at arm's length from what it's chasing.
        if (State == ZombieState.Chase && Perception.Target != null
            && GlobalPosition.DistanceTo(Perception.Target.GroundPosition) <= Type.ReachDistance)
            desired = Vector3.Zero;

        Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z).MoveToward(desired, Type.Acceleration * dt);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Z;
        Velocity = velocity;
        MoveAndSlide();

        Face(desired.LengthSquared() > 0.01f ? desired : FacingTarget(), dt);
        UpdateVoice(dt);
        UpdateDebugLabel();
    }

    // ---------- Decisions ----------

    private void Think(float dt)
    {
        _staggerLeft = Mathf.Max(0f, _staggerLeft - dt);
        _repathTimer -= dt;

        if (Perception.CanSeeTarget && Perception.Target != null)
        {
            State = ZombieState.Chase;
            if (_repathTimer <= 0f)
            {
                SetDestination(Perception.Target.GroundPosition);
                _repathTimer = 0.4f;
            }
            return;
        }

        if (State == ZombieState.Chase)
        {
            // Lost sight: head for where it was last seen, then search around.
            State = ZombieState.Search;
            _stateTimer = Type.SearchSeconds;
            SetDestination(Perception.LastSeenPosition);
            return;
        }

        if (Perception.HeardCount != _lastHeardCount)
        {
            _lastHeardCount = Perception.HeardCount;
            State = ZombieState.Investigate;
            _stateTimer = Type.InvestigateSeconds;
            SetDestination(Perception.HeardPosition);
            return;
        }

        switch (State)
        {
            case ZombieState.Investigate:
            case ZombieState.Search:
                if (ReachedDestination())
                {
                    _stateTimer -= dt;
                    // Searching: shuffle around the area.
                    if (State == ZombieState.Search && _idleTimer <= 0f)
                    {
                        WanderTo(Type.WanderRadius * 0.5f);
                        _idleTimer = _rng.RandfRange(2f, 4f);
                    }
                    _idleTimer -= dt;
                    if (_stateTimer <= 0f)
                        State = ZombieState.Wander;
                }
                break;

            case ZombieState.Wander:
                if (ReachedDestination())
                {
                    _idleTimer -= dt;
                    if (_idleTimer <= 0f)
                    {
                        WanderTo(Type.WanderRadius);
                        _idleTimer = _rng.RandfRange(3f, 8f);
                    }
                }
                break;
        }
    }

    private float CurrentSpeed()
    {
        float speed = IsCrawler ? Type.CrawlSpeed : State switch
        {
            ZombieState.Chase => _chaseSpeed,
            ZombieState.Investigate => _chaseSpeed * 0.75f,
            ZombieState.Search => _chaseSpeed * 0.6f,
            _ => Type.WanderSpeed,
        };
        if (_staggerLeft > 0f || Health.IsDying)
            speed *= 0.3f;
        return speed;
    }

    private void SetDestination(Vector3 point)
    {
        if (!_navReady)
            return;
        Rid map = GetWorld3D().NavigationMap;
        _agent.TargetPosition = NavigationServer3D.MapGetClosestPoint(map, point);
        _hasDestination = true;
    }

    private void WanderTo(float radius)
    {
        float angle = _rng.Randf() * Mathf.Tau;
        float distance = _rng.RandfRange(radius * 0.3f, radius);
        SetDestination(GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance);
    }

    /// <summary>Safety net: a destination with no path (unreachable, or set too early) is dropped after a second.</summary>
    private void GiveUpOnUnreachableDestination(float dt)
    {
        if (!_hasDestination || !_navReady || _agent.IsNavigationFinished() || _agent.GetCurrentNavigationPath().Length > 0)
        {
            _noPathTime = 0f;
            return;
        }
        _noPathTime += dt;
        if (_noPathTime > 1f)
        {
            _hasDestination = false;
            _noPathTime = 0f;
        }
    }

    private bool ReachedDestination() => !_hasDestination || !_navReady || _agent.IsNavigationFinished();

    private Vector3 FacingTarget()
    {
        if (State == ZombieState.Chase && Perception.Target != null)
        {
            Vector3 to = Perception.Target.GroundPosition - GlobalPosition;
            to.Y = 0f;
            return to;
        }
        return Vector3.Zero;
    }

    private void Face(Vector3 direction, float dt)
    {
        if (direction.LengthSquared() < 0.0001f)
            return;
        float targetYaw = Mathf.Atan2(-direction.X, -direction.Z);
        float yaw = Mathf.RotateToward(Rotation.Y, targetYaw, Mathf.DegToRad(Type.TurnSpeedDegrees) * dt);
        Rotation = new Vector3(0f, yaw, 0f);
    }

    // ---------- Body ----------

    private void UpdateCrawler()
    {
        if (IsCrawler || (!Health.IsDestroyed(BodyPart.LeftLeg) && !Health.IsDestroyed(BodyPart.RightLeg)))
            return;
        IsCrawler = true;
        _visual.Rotation = new Vector3(Mathf.DegToRad(-80f), 0f, 0f);
        _visual.Position = new Vector3(0f, 0.15f, 0f);
        if (_collider.Shape is CylinderShape3D cylinder)
        {
            cylinder.Height = 0.5f;
            _collider.Position = new Vector3(0f, 0.25f, 0f);
        }
        Perception.Position = new Vector3(0f, 0.35f, -0.6f);
    }

    private void OnDied()
    {
        State = ZombieState.Dead;
        _hasDestination = false;
        Perception.Enabled = false;
        CollisionLayer = 0; // Corpses don't block movement; their hitboxes still stop bullets.
        if (_assistTarget != null)
            _assistTarget.Hostile = false;
        _voice.Stop();
    }

    private void UpdateVoice(float dt)
    {
        _groanTimer -= dt;
        if (_groanTimer > 0f)
            return;
        _voice.Play();
        _groanTimer = State == ZombieState.Chase ? _rng.RandfRange(2.5f, 5f) : _rng.RandfRange(7f, 16f);
    }

    private void UpdateDebugLabel()
    {
        if (_debugLabel == null || !_debugLabel.Visible)
            return;
        _debugLabel.Text = $"{State}{(IsCrawler ? " (crawler)" : "")}{(Health.IsDying ? " BLEEDING OUT" : "")}\n"
            + $"aware {Perception.Awareness:0.0}  {CurrentSpeed():0.0} m/s\n"
            + $"head {Mathf.Ceil(Health.Hp(BodyPart.Head))}  chest {Mathf.Ceil(Health.Hp(BodyPart.Chest))}";
    }
}
