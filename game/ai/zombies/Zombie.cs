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
public partial class Zombie : CharacterBody3D, ISenseTarget
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
    private enum SearchPhase { GoingToLastSeen, GoingToGuess, Sweeping }
    private SearchPhase _searchPhase;
    private Vector3 _searchDirection;
    private float _deathTime;
    private float _attackCooldown;
    private float _windupLeft = -1f;
    private IAttackable? _grabbing;
    private float _grabAttackTimer;
    private float _knockdownLeft;
    private AudioStreamPlayer3D _attackVoice = null!;

    public bool IsGrabbing => _grabbing != null;

    // ---------- Seen by survivors ----------
    public Vector3 SightPoint => GlobalPosition + Vector3.Up * (IsCrawler ? 0.3f : 1.5f);
    /// <summary>Where to aim for a kill: the head.</summary>
    public Vector3 HeadPoint => GlobalPosition + Vector3.Up * (IsCrawler || IsKnockedDown ? 0.25f : 1.62f);
    public Vector3 GroundPosition => GlobalPosition;
    public float Visibility => 1f;
    public Godot.Collections.Array<Rid> SightExclude => Perception.SelfExclude;
    public bool IsAlive => State != ZombieState.Dead;
    public SenseTeam Team => SenseTeam.Zombie;

    public override void _EnterTree() => SenseTargets.All.Add(this);

    public override void _ExitTree() => SenseTargets.All.Remove(this);
    public bool IsKnockedDown => _knockdownLeft > 0f;

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
        Perception.Self = this;
        Perception.TargetFilter = t => t.Team == SenseTeam.Living;
        Perception.SelfExclude.Add(GetRid());
        GetNode<HitboxRig>("Visual/Hitboxes").AddRidsTo(Perception.SelfExclude);

        Health.Damaged += (_, _) => _staggerLeft = Type.StaggerSeconds;
        Health.Died += OnDied;

        _voice = new AudioStreamPlayer3D { Stream = PlaceholderSounds.ZombieGroan, UnitSize = 4f, MaxDistance = 45f, PitchScale = _rng.RandfRange(0.8f, 1.15f) };
        AddChild(_voice);
        _groanTimer = _rng.RandfRange(2f, 10f);
        _attackVoice = new AudioStreamPlayer3D { Stream = PlaceholderSounds.ZombieAttack, UnitSize = 5f, MaxDistance = 30f, PitchScale = _rng.RandfRange(0.85f, 1.15f) };
        AddChild(_attackVoice);
        _idleTimer = _rng.RandfRange(0f, 3f);

    }

    public override void _PhysicsProcess(double delta)
    {
        if (_grabbing is GodotObject held && !IsInstanceValid(held))
            _grabbing = null; // The body it held is gone (rose as a zombie).
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
        if (UpdateKnockdown(dt, ref velocity))
            return;
        Think(dt);
        GiveUpOnUnreachableDestination(dt);
        UpdateAttack(dt);

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

        // Stop at arm's length from what it's chasing, and hold still while lunging.
        if ((State == ZombieState.Chase && Perception.Target != null
            && GlobalPosition.DistanceTo(Perception.Target.GroundPosition) <= Type.ReachDistance)
            || _windupLeft >= 0f)
            desired = Vector3.Zero;

        // Holding on: hang on to the target and get dragged/pulled along with it.
        if (_grabbing != null && Perception.Target != null)
        {
            Vector3 toTarget = Perception.Target.GroundPosition - GlobalPosition;
            toTarget.Y = 0f;
            float gap = toTarget.Length() - Type.ReachDistance * 0.8f;
            desired = gap > 0.05f ? toTarget.Normalized() * Mathf.Min(gap * 6f, 4f) : Vector3.Zero;
        }

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
            // Sounds heard while it can see you are old news: don't chase them once it loses sight.
            _lastHeardCount = Perception.HeardCount;
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
            _searchPhase = SearchPhase.GoingToLastSeen;
            Vector3 velocity = Perception.LastSeenVelocity;
            _searchDirection = velocity.LengthSquared() > 0.09f ? velocity.Normalized() : Vector3.Zero;
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
                    if (State == ZombieState.Search && AdvanceSearch())
                        break;
                    _stateTimer -= dt;
                    // Searching: sweep the area, favoring the way the target was heading.
                    if (State == ZombieState.Search && _idleTimer <= 0f)
                    {
                        SweepAhead();
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

    /// <summary>
    /// At the last known position: make an educated guess. The target kept moving the way it was
    /// going for as long as it's been out of sight, so head there (snapped onto walkable ground).
    /// Returns true if a new leg of the search started.
    /// </summary>
    private bool AdvanceSearch()
    {
        if (_searchPhase != SearchPhase.GoingToLastSeen)
            return false;
        _searchPhase = SearchPhase.Sweeping;
        if (!Type.PredictsMovement || _searchDirection == Vector3.Zero)
            return false;

        float seenSpeed = Mathf.Max(Perception.LastSeenVelocity.Length(), 1.5f);
        float elapsed = (float)(Time.GetTicksMsec() / 1000.0 - Perception.LastSeenTime);
        float distance = Mathf.Clamp(seenSpeed * elapsed, 3f, Type.MaxPredictionDistance);
        SetDestination(Perception.LastSeenPosition + _searchDirection * distance);
        _searchPhase = SearchPhase.GoingToGuess;
        return true;
    }

    private void SweepAhead()
    {
        if (_searchDirection == Vector3.Zero)
        {
            WanderTo(Type.WanderRadius * 0.5f);
            return;
        }
        float baseAngle = Mathf.Atan2(_searchDirection.Z, _searchDirection.X);
        float angle = baseAngle + _rng.RandfRange(-0.9f, 0.9f); // ±50° around the heading.
        float distance = _rng.RandfRange(3f, 7f);
        SetDestination(GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance);
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

    // ---------- Attacks ----------

    private void UpdateAttack(float dt)
    {
        _attackCooldown = Mathf.Max(0f, _attackCooldown - dt);
        IAttackable? target = State == ZombieState.Chase ? Perception.Target as IAttackable : null;
        if (target == null || target.Health.IsDead || _staggerLeft > 0f || Health.IsDying)
        {
            CancelAttack();
            Release();
            return;
        }

        float distance = GlobalPosition.DistanceTo(Perception.Target!.GroundPosition);

        if (_grabbing != null)
        {
            // Holding on: keep biting until shoved off or killed. It moves with you (see movement).
            if (distance > Type.ReachDistance + 2.5f)
            {
                Release();
                return;
            }
            _grabAttackTimer -= dt;
            if (_grabAttackTimer <= 0f)
            {
                _grabAttackTimer = Type.GrabAttackInterval;
                _attackVoice.Play();
                LandAttack(target, grabbed: true);
            }
            return;
        }

        if (_windupLeft < 0f)
        {
            if (_attackCooldown <= 0f && distance <= Type.ReachDistance + 0.15f && FacingTargetWithin(50f))
            {
                _windupLeft = Type.AttackWindup;
                _attackVoice.Play();
            }
            return;
        }

        // Lunging: lean in, then the swipe/bite lands if the target is still within reach.
        _windupLeft -= dt;
        if (!IsCrawler)
            _visual.Rotation = new Vector3(Mathf.DegToRad(-18f) * (1f - _windupLeft / Type.AttackWindup), 0f, 0f);
        if (_windupLeft > 0f)
            return;
        _windupLeft = -1f;
        _attackCooldown = Type.AttackCooldown;
        if (!IsCrawler)
            _visual.Rotation = Vector3.Zero;
        if (distance <= Type.ReachDistance + 0.4f && FacingTargetWithin(70f))
        {
            LandAttack(target, grabbed: false);
            if (_rng.Randf() < Type.GrabChance)
                Grab(target);
        }
    }

    private void LandAttack(IAttackable target, bool grabbed)
    {
        BodyPart part = PickBodyPart();
        // Holding on means bites; a passing swipe is mostly scratches.
        float roll = _rng.Randf();
        WoundType type = grabbed
            ? (roll < 0.5f ? WoundType.Bite : roll < 0.8f ? WoundType.Laceration : WoundType.Scratch)
            : (roll < 0.6f ? WoundType.Scratch : roll < 0.95f ? WoundType.Laceration : WoundType.Bite);

        (float damage, float light, float heavy, float infection) = type switch
        {
            WoundType.Bite => (Type.BiteDamage, 0.4f, 0.6f, Type.BiteInfection),
            WoundType.Laceration => (Type.LacerationDamage, 0.7f, 0.15f, Type.LacerationInfection),
            _ => (Type.ScratchDamage, 0.3f, 0f, Type.ScratchInfection),
        };
        // Exposure first: a wound that kills still infects (the dead rise).
        target.Infection?.Expose(infection);
        target.Health.ApplyWound(part, type, damage, light, heavy, DamageSource.ZombieAttack);
    }

    private BodyPart PickBodyPart()
    {
        if (IsCrawler)
            return _rng.Randf() < 0.5f ? BodyPart.LeftLeg : BodyPart.RightLeg;
        float roll = _rng.Randf();
        return roll < 0.18f ? BodyPart.LeftArm
            : roll < 0.36f ? BodyPart.RightArm
            : roll < 0.58f ? BodyPart.Chest
            : roll < 0.68f ? BodyPart.Head
            : roll < 0.82f ? BodyPart.Stomach
            : roll < 0.91f ? BodyPart.LeftLeg
            : BodyPart.RightLeg;
    }

    private bool FacingTargetWithin(float degrees)
    {
        Vector3 to = Perception.Target!.GroundPosition - GlobalPosition;
        to.Y = 0f;
        Vector3 forward = -GlobalBasis.Z;
        forward.Y = 0f;
        return to.LengthSquared() < 0.01f || forward.AngleTo(to) <= Mathf.DegToRad(degrees);
    }

    private void Grab(IAttackable target)
    {
        _grabbing = target;
        _grabAttackTimer = Type.GrabAttackInterval;
        target.OnGrabbed(this);
    }

    private void Release()
    {
        if (_grabbing == null)
            return;
        _grabbing.OnReleased(this);
        _grabbing = null;
    }

    private void CancelAttack()
    {
        if (_windupLeft >= 0f && !IsCrawler && _knockdownLeft <= 0f)
            _visual.Rotation = Vector3.Zero;
        _windupLeft = -1f;
    }

    /// <summary>Shoved by a rifle butt: pushed back, staggered, lets go — and maybe knocked down.</summary>
    public void ApplyShove(Vector3 direction, float strength, bool knockDown)
    {
        if (State == ZombieState.Dead)
            return;
        CancelAttack();
        Release();
        direction.Y = 0f;
        Velocity += direction.Normalized() * 3.2f * strength;
        _staggerLeft = Mathf.Max(_staggerLeft, 1.0f * strength);
        _attackCooldown = Mathf.Max(_attackCooldown, 0.8f);
        if (knockDown && !IsCrawler)
            _knockdownLeft = Type.KnockdownSeconds;
    }

    /// <summary>On the ground after a shove: slides to a stop, then gets back up. Returns true while down.</summary>
    private bool UpdateKnockdown(float dt, ref Vector3 velocity)
    {
        if (_knockdownLeft <= 0f)
            return false;
        _knockdownLeft -= dt;
        float fall = Mathf.Min((Type.KnockdownSeconds - _knockdownLeft) * 4f, 1f);
        float rise = Mathf.Clamp(_knockdownLeft * 2f, 0f, 1f); // Last half second: getting up.
        _visual.Rotation = new Vector3(Mathf.DegToRad(80f) * Mathf.Min(fall, rise), 0f, 0f);
        Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z).MoveToward(Vector3.Zero, 6f * dt);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Z;
        Velocity = velocity;
        MoveAndSlide();
        if (_knockdownLeft <= 0f)
            _visual.Rotation = Vector3.Zero;
        UpdateDebugLabel();
        return true;
    }

    private void OnDied()
    {
        CancelAttack();
        Release();
        _knockdownLeft = 0f;
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
        _debugLabel.Text = $"{State}{(IsCrawler ? " (crawler)" : "")}{(Health.IsDying ? " BLEEDING OUT" : "")}{(IsGrabbing ? " GRABBING" : "")}{(IsKnockedDown ? " DOWN" : "")}{(_windupLeft >= 0f ? " LUNGE" : "")}\n"
            + $"aware {Perception.Awareness:0.0}  {CurrentSpeed():0.0} m/s\n"
            + $"head {Mathf.Ceil(Health.Hp(BodyPart.Head))}  chest {Mathf.Ceil(Health.Hp(BodyPart.Chest))}";
    }
}
