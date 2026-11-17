using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Character;
using ZombieSandbox.Combat;
using ZombieSandbox.Health;
using ZombieSandbox.Player;
using ZombieSandbox.World;

namespace ZombieSandbox.AI;

public enum SurvivorState
{
    Idle,
    /// <summary>Heard something or a group mate spotted an enemy: get to cover, weapon ready.</summary>
    Alert,
    Engage,
    /// <summary>Badly hurt: fall back to cover away from the threat.</summary>
    Retreat,
    /// <summary>Lost the enemy: check where it was last seen.</summary>
    Search,
    Dead,
}

/// <summary>
/// A human survivor in combat. Uses the same body, weapons and senses as everyone else: its accuracy
/// comes from the real weapon sway/recoil and its skills, plus a human aim-settling delay.
/// Takes low cover (crouch to hide, rise to shoot), reloads behind it, keeps its head down under fire,
/// falls back when badly hurt, shoves zombies off and shoots them in the head.
/// </summary>
public partial class Survivor : CharacterBody3D, ISenseTarget, IAttackable
{
    public const string Group = "survivors";

    [Export] public SurvivorType Type { get; set; } = null!;
    /// <summary>Survivors with the same group id share sightings and never shoot each other.</summary>
    [Export] public string GroupId { get; set; } = "bandits";
    /// <summary>Spawned when an infected survivor dies and turns.</summary>
    [Export] public PackedScene? ZombieScene { get; set; }

    public SurvivorState State { get; private set; } = SurvivorState.Idle;
    public BodyHealth Health { get; private set; } = null!;
    public Infection Infection { get; private set; } = null!;
    Infection? IAttackable.Infection => Infection;
    public Perception Perception { get; private set; } = null!;
    public WeaponHolder Weapons { get; private set; } = null!;
    public float Suppression { get; private set; }
    public bool IsCrouched { get; private set; }

    private const float StandHeight = 1.8f;
    private const float CrouchHeight = 1.2f;

    private NavigationAgent3D _agent = null!;
    private Node3D _body = null!;
    private Node3D _mesh = null!;
    private CollisionShape3D _collider = null!;
    private HitboxRig _hitboxes = null!;
    private MagazinePouch _pouch = null!;
    private Label3D _bark = null!;
    private Label3D? _debug;
    private readonly Array<Rid> _selfExclude = new();
    private readonly RandomNumberGenerator _rng = new();
    private readonly PhysicsRayQueryParameters3D _ray = new();
    private float _gravity;
    private float _height = StandHeight;

    // Knowledge of the enemy.
    private ISenseTarget? _target;
    private Vector3 _threatPosition;
    private double _threatTime = -999;
    private int _lastHeardCount;

    // Cover.
    private enum CoverKind { None, Low, Full }
    private Vector3? _cover;
    private CoverKind _coverKind;
    private float _coverTimer;
    private bool _exposed;
    private float _phaseTimer;
    private static readonly List<(Survivor Owner, Vector3 Point)> ClaimedCover = new();

    // Aiming and firing.
    private ISenseTarget? _aimTarget;
    private float _aimSettle;
    private Vector3 _aimError;
    private Vector3 _aimErrorDirection;
    private Vector3 _aimPoint;
    private bool _hasAimPoint;
    private float _shotTimer;

    // Misc.
    private float _stateTimer;
    private float _shoveCooldown;
    private int _grabbedBy;
    private float _barkLeft;
    private string _lastBark = "";
    private float _barkCooldown;
    private float _deadTime;
    private float _riseTimer = -1f;
    private Vector3 _home;
    private float _idleTimer;

    private bool NavReady => RuntimeNavigationBaker.MapReady;
    private double Now => Time.GetTicksMsec() / 1000.0;

    // ---------- ISenseTarget / IAttackable ----------

    public Vector3 SightPoint => GlobalPosition + Vector3.Up * (_height - 0.25f);
    public Vector3 GroundPosition => GlobalPosition;
    public float Visibility => IsCrouched ? 0.6f : 1f;
    public Array<Rid> SightExclude => _selfExclude;
    public bool IsAlive => State != SurvivorState.Dead;
    public SenseTeam Team => SenseTeam.Living;

    public void OnGrabbed(Node3D grabber) => _grabbedBy++;

    public void OnReleased(Node3D grabber) => _grabbedBy = Mathf.Max(0, _grabbedBy - 1);

    public override void _EnterTree()
    {
        SenseTargets.All.Add(this);
        GameNoise.Emitted += OnNoise;
    }

    public override void _ExitTree()
    {
        SenseTargets.All.Remove(this);
        GameNoise.Emitted -= OnNoise;
        ClaimedCover.RemoveAll(c => c.Owner == this);
    }

    public override void _Ready()
    {
        AddToGroup(Group);
        _rng.Randomize();
        _home = GlobalPosition;
        _idleTimer = _rng.RandfRange(2f, 8f);
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

        Health = GetNode<BodyHealth>("Health");
        Infection = GetNode<Infection>("Infection");
        Perception = GetNode<Perception>("Perception");
        Weapons = GetNode<WeaponHolder>("WeaponHolder");
        _agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        _body = GetNode<Node3D>("Body");
        _mesh = GetNode<Node3D>("Body/Mesh");
        _collider = GetNode<CollisionShape3D>("CollisionShape3D");
        _hitboxes = GetNode<HitboxRig>("Body/Hitboxes");
        _pouch = GetNode<MagazinePouch>("MagazinePouch");
        _bark = GetNode<Label3D>("Bark");
        _debug = GetNodeOrNull<Label3D>("DebugLabel");
        if (_debug != null)
            _debug.Visible = OS.IsDebugBuild();
        _bark.Visible = false;

        var skills = GetNode<CharacterSkills>("Skills");
        var stats = GetNode<CharacterStats>("Stats");
        CallDeferred(MethodName.ApplySkills, skills);
        stats.Initialize(skills);
        Health.Initialize(null);

        _selfExclude.Add(GetRid());
        _hitboxes.AddRidsTo(_selfExclude);
        Weapons.Initialize(stats, skills, _pouch, _selfExclude, worldMask: 1, bulletMask: 1 | HitboxRig.HitboxLayer);
        Weapons.GiveSpareMagazines(3);
        if (Weapons.FireMode == FireMode.Auto)
            Weapons.CycleFireMode(); // Semi-auto, aimed shots.

        Perception.SightRangeDay = Type.SightRangeDay;
        Perception.SightRangeNight = Type.SightRangeNight;
        Perception.FieldOfViewDegrees = Type.FieldOfViewDegrees;
        Perception.DetectionTime = 0.8f;
        Perception.Self = this;
        Perception.TargetFilter = IsEnemy;
        Perception.SelfExclude.AddRange(_selfExclude);

        _ray.Exclude = _selfExclude;
        Health.Damaged += OnDamaged;
        Health.Died += OnDied;
    }

    /// <summary>Skill levels are set after the skills node has initialized its defaults.</summary>
    private void ApplySkills(CharacterSkills skills)
    {
        skills.SetLevel(SkillId.Aiming, Type.Aiming);
        skills.SetLevel(SkillId.Strength, Type.Strength);
        skills.SetLevel(SkillId.Reloading, Type.Reloading);
        skills.SetLevel(SkillId.Maintenance, Type.Maintenance);
        skills.SetLevel(SkillId.Fitness, Type.Fitness);
    }

    /// <summary>Piece A: zombies, the player and other groups are enemies. Factions come in piece B.</summary>
    private bool IsEnemy(ISenseTarget target) => target switch
    {
        Survivor other => other.GroupId != GroupId,
        _ => true,
    };

    // ---------- Main loop ----------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        Vector3 velocity = Velocity;
        if (!IsOnFloor())
            velocity.Y -= _gravity * dt;

        if (State == SurvivorState.Dead)
        {
            UpdateDead(dt);
            velocity.X = velocity.Z = 0f;
            Velocity = velocity;
            MoveAndSlide();
            return;
        }

        Suppression = Mathf.Max(0f, Suppression - 0.3f * dt);
        _shoveCooldown -= dt;
        _shotTimer -= dt;
        _barkCooldown -= dt;
        UpdateKnowledge();
        Decide(dt);
        DefendAgainstZombies();

        Vector3 desired = Vector3.Zero;
        float speed = MoveSpeed();
        if (_agent.TargetPosition != Vector3.Zero && NavReady && !_agent.IsNavigationFinished())
        {
            Vector3 to = _agent.GetNextPathPosition() - GlobalPosition;
            to.Y = 0f;
            if (to.LengthSquared() > 0.0004f)
                desired = to.Normalized() * speed;
        }
        Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z).MoveToward(desired, 10f * dt);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Z;
        Velocity = velocity;
        MoveAndSlide();

        UpdateStance(dt);
        Face(dt, desired);
        UpdateWeaponIntent(speed);
        UpdateBark(dt);
        UpdateDebug();
    }

    public override void _Process(double delta)
    {
        // Point the weapon: from the eye toward the aim point (plus the settling aim error).
        if (State == SurvivorState.Dead)
            return;
        Vector3 forward = -GlobalBasis.Z;
        Vector3 eye = GlobalPosition + Vector3.Up * (_height - 0.15f) + forward * 0.1f;
        Vector3 aimAt = _hasAimPoint ? _aimPoint + _aimError : eye + forward * 10f;
        if ((aimAt - eye).LengthSquared() < 0.01f)
            aimAt = eye + forward;
        Weapons.AimPivot = new Transform3D(Basis.Identity, eye).LookingAt(aimAt, Vector3.Up);
    }

    // ---------- Knowledge ----------

    private void UpdateKnowledge()
    {
        if (Perception.CanSeeTarget && Perception.Target != null)
        {
            if (_target != Perception.Target)
                StartAimingAt(Perception.Target);
            _target = Perception.Target;
            _threatPosition = _target.GroundPosition;
            _threatTime = Now;
            _lastHeardCount = Perception.HeardCount;
            if (_target.Team == SenseTeam.Living)
                SurvivorGroup.Report(GroupId, _target, _threatPosition);
        }
        else if (_target != null && !_target.IsAlive)
        {
            _target = null;
        }
    }

    private bool CanSeeTarget => Perception.CanSeeTarget && _target != null && Perception.Target == _target;

    private void StartAimingAt(ISenseTarget target)
    {
        _aimTarget = target;
        _aimSettle = 0f;
        float distance = GlobalPosition.DistanceTo(target.GroundPosition);
        _aimErrorDirection = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-0.5f, 1f), _rng.RandfRange(-1f, 1f)).Normalized();
        _aimError = _aimErrorDirection * Type.InitialAimError * Mathf.Clamp(distance / 20f, 0.5f, 2f);
    }

    // ---------- Decisions ----------

    private void Decide(float dt)
    {
        bool knowsThreat = Now - _threatTime < Type.SearchSeconds;
        bool hurt = Health.Danger >= Type.RetreatDanger;

        if (CanSeeTarget)
        {
            if (State is SurvivorState.Idle or SurvivorState.Alert or SurvivorState.Search)
                Bark(_target!.Team == SenseTeam.Zombie ? "Zombie!" : "Contact!");
            if (hurt && _target!.Team == SenseTeam.Living)
            {
                if (State != SurvivorState.Retreat)
                    Bark("Fall back!");
                State = SurvivorState.Retreat;
            }
            else
            {
                State = SurvivorState.Engage;
            }
        }
        else if (State is SurvivorState.Engage or SurvivorState.Retreat && !knowsThreat)
        {
            State = SurvivorState.Idle;
            ReleaseCover();
        }
        else if (State is SurvivorState.Engage or SurvivorState.Retreat && knowsThreat)
        {
            if (State == SurvivorState.Engage && Now - _threatTime > 4.0)
            {
                State = SurvivorState.Search;
                _stateTimer = Type.SearchSeconds;
                Bark("Where'd they go?");
                ReleaseCover();
                MoveTo(_threatPosition);
            }
        }
        else if (State is SurvivorState.Idle or SurvivorState.Alert)
        {
            // A group mate's sighting or a sound: get ready, facing it.
            SurvivorGroup.Sighting? shared = SurvivorGroup.Recent(GroupId, 6.0);
            if (shared is { } s && s.Target.IsAlive && Now - _threatTime > 1.0)
            {
                if (State == SurvivorState.Idle)
                    Bark("Where?");
                _threatPosition = s.Position;
                _threatTime = s.Time;
                BecomeAlert();
            }
            else if (Perception.HeardCount != _lastHeardCount)
            {
                _lastHeardCount = Perception.HeardCount;
                _threatPosition = Perception.HeardPosition;
                _threatTime = Now;
                BecomeAlert();
            }
        }

        switch (State)
        {
            case SurvivorState.Idle: DoIdle(dt); break;
            case SurvivorState.Alert: DoAlert(dt); break;
            case SurvivorState.Engage: DoEngage(dt); break;
            case SurvivorState.Retreat: DoRetreat(dt); break;
            case SurvivorState.Search: DoSearch(dt); break;
        }
    }

    private void BecomeAlert()
    {
        if (State != SurvivorState.Alert)
        {
            State = SurvivorState.Alert;
            _stateTimer = 25f;
            _cover = null;
        }
    }

    private void DoIdle(float dt)
    {
        _hasAimPoint = false;
        IsCrouched = false;
        _idleTimer -= dt;
        if (_idleTimer <= 0f)
        {
            _idleTimer = _rng.RandfRange(6f, 14f);
            float angle = _rng.Randf() * Mathf.Tau;
            MoveTo(_home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _rng.RandfRange(1f, 5f));
        }
    }

    private void DoAlert(float dt)
    {
        _stateTimer -= dt;
        AimAtThreatArea();
        if (_cover == null || _coverTimer <= 0f)
        {
            FindCover(_threatPosition, preferFar: false);
            _coverTimer = 4f;
        }
        _coverTimer -= dt;
        IsCrouched = ReachedCover();
        if (_stateTimer <= 0f)
        {
            State = SurvivorState.Idle;
            ReleaseCover();
        }
    }

    private void DoEngage(float dt)
    {
        if (_target == null)
        {
            State = SurvivorState.Search;
            _stateTimer = Type.SearchSeconds;
            MoveTo(_threatPosition);
            return;
        }
        _coverTimer -= dt;
        bool zombie = _target!.Team == SenseTeam.Zombie;
        float distance = GlobalPosition.DistanceTo(_target.GroundPosition);

        // Close zombies: stand and shoot (or shove, handled separately); no time to hide.
        if (zombie && distance < 8f)
        {
            ReleaseCover();
            StopMoving();
            IsCrouched = false;
            AimAtTarget();
            TryShoot();
            return;
        }

        if (_cover == null || _coverTimer <= 0f)
        {
            FindCover(_threatPosition, preferFar: false);
            _coverTimer = 3f;
        }

        if (_cover != null && !ReachedCover())
        {
            // Running to cover: weapon up only if very close.
            IsCrouched = false;
            AimAtTarget();
            if (distance < 10f)
                TryShoot();
            return;
        }

        StopMoving();
        AimAtTarget();

        if (_coverKind == CoverKind.Low)
        {
            // Pop up to shoot, duck down to reload / when suppressed.
            _phaseTimer -= dt;
            bool needsReload = NeedsReload();
            bool pinned = Suppression > 0.6f;
            if (_exposed && (_phaseTimer <= 0f || needsReload || pinned))
            {
                _exposed = false;
                _phaseTimer = Type.HideSeconds * (1f + Suppression) * _rng.RandfRange(0.7f, 1.3f);
            }
            else if (!_exposed && _phaseTimer <= 0f && !pinned && !Weapons.IsBusy)
            {
                _exposed = true;
                _phaseTimer = Type.ExposeSeconds * _rng.RandfRange(0.7f, 1.3f);
            }
            IsCrouched = !_exposed;
            if (!_exposed)
                HandleWeaponUpkeep();
            else
                TryShoot();
        }
        else
        {
            // No good cover: crouch where it stands (steadier) and fight.
            IsCrouched = true;
            HandleWeaponUpkeep();
            TryShoot();
        }
    }

    private void DoRetreat(float dt)
    {
        _coverTimer -= dt;
        if (_cover == null || _coverTimer <= 0f || _coverKind == CoverKind.None)
        {
            FindCover(_threatPosition, preferFar: true);
            _coverTimer = 6f;
        }
        AimAtTarget();
        IsCrouched = ReachedCover();
        HandleWeaponUpkeep();
        // Only shoot back if they come close.
        if (CanSeeTarget && GlobalPosition.DistanceTo(_target!.GroundPosition) < 12f)
            TryShoot();
    }

    private void DoSearch(float dt)
    {
        _stateTimer -= dt;
        IsCrouched = false;
        AimAtThreatArea();
        HandleWeaponUpkeep();
        if (_agent.IsNavigationFinished() && _rng.Randf() < dt * 0.4f)
        {
            float angle = _rng.Randf() * Mathf.Tau;
            MoveTo(_threatPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _rng.RandfRange(2f, 6f));
        }
        if (_stateTimer <= 0f)
        {
            State = SurvivorState.Idle;
            MoveTo(_home);
        }
    }

    // ---------- Weapon ----------

    private void AimAtTarget()
    {
        if (CanSeeTarget)
        {
            _aimPoint = _target is Zombie z ? z.HeadPoint : _target!.SightPoint - Vector3.Up * 0.25f;
            _hasAimPoint = true;
            float settleRate = 1f / Type.AimSettleTime * (1f - 0.5f * Suppression);
            _aimSettle = Mathf.Min(1f, _aimSettle + settleRate * (float)GetPhysicsProcessDeltaTime());
            // The starting error shrinks to nothing as the aim settles.
            float remaining = 1f - Mathf.SmoothStep(0f, 1f, _aimSettle);
            _aimError = _aimErrorDirection * (InitialErrorLength() * remaining);
        }
        else
        {
            AimAtThreatArea();
        }
    }

    private float InitialErrorLength()
    {
        if (_aimTarget == null)
            return 0f;
        float distance = GlobalPosition.DistanceTo(_aimTarget.GroundPosition);
        return Type.InitialAimError * Mathf.Clamp(distance / 20f, 0.5f, 2f);
    }

    private void AimAtThreatArea()
    {
        _aimPoint = _threatPosition + Vector3.Up * 1.3f;
        _hasAimPoint = Now - _threatTime < Type.SearchSeconds;
    }

    private void TryShoot()
    {
        if (!CanSeeTarget || _shotTimer > 0f || !Weapons.IsAiming || Weapons.AdsBlend < 0.9f || _aimSettle < 0.6f)
            return;
        if (Weapons.IsJammed || NeedsReload())
        {
            HandleWeaponUpkeep();
            return;
        }
        if (!ClearShot())
            return;
        Weapons.PullTrigger();
        float distance = GlobalPosition.DistanceTo(_target!.GroundPosition);
        _shotTimer = _rng.RandfRange(Type.ShotIntervalMin, Type.ShotIntervalMax) * (distance < 12f ? 0.6f : 1f);
    }

    /// <summary>Nothing friendly in the way: the first thing the shot would hit is the target (or nothing).</summary>
    private bool ClearShot()
    {
        _ray.From = Weapons.AimPivot.Origin;
        _ray.To = _aimPoint;
        _ray.CollisionMask = 1 | HitboxRig.HitboxLayer;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        if (hit.Count == 0)
            return true;
        Node? node = hit["collider"].AsGodotObject() as Node;
        for (; node != null; node = node.GetParent())
        {
            if (node is Survivor other)
                return other.GroupId != GroupId;
            if (node == _target as Node)
                return true;
        }
        return false; // Something solid in the way.
    }

    private bool NeedsReload() =>
        Weapons.Magazine == null || (Weapons.Magazine.Rounds <= 3 && !Weapons.IsBusy && _pouch.FullestRounds() > Weapons.Magazine.Rounds);

    private void HandleWeaponUpkeep()
    {
        if (Weapons.IsBusy)
            return;
        if (Weapons.IsJammed)
        {
            Weapons.RequestClearMalfunction(inspectFirst: false);
            Bark("Jammed!");
        }
        else if (NeedsReload() || (!Weapons.Chambered && Weapons.Magazine is { IsEmpty: true }))
        {
            Weapons.RequestReload(fast: Suppression > 0.5f);
            Bark("Reloading!");
        }
    }

    private void UpdateWeaponIntent(float speed)
    {
        bool fighting = State is SurvivorState.Engage or SurvivorState.Retreat;
        Weapons.WantsAim = fighting && CanSeeTarget && (!IsCrouched || _coverKind != CoverKind.Low);
        Weapons.TriggerHeld = false;
        Weapons.Lowered = false;
        Weapons.MoveFraction = Mathf.Clamp(speed / 3.5f, 0f, 1f) * (Velocity.LengthSquared() > 0.1f ? 1f : 0f);
        Weapons.StanceSwayMultiplier = IsCrouched ? 0.7f : 1f;
        Weapons.Breathlessness = 0f;
        Weapons.InjurySwayMultiplier = Health.SwayMultiplier * Infection.SwayMultiplier * (1f + Suppression * 0.8f);
    }

    // ---------- Cover ----------

    /// <summary>
    /// Look around for cover against <paramref name="threat"/>: low cover hides it crouched but lets it
    /// shoot standing; full cover hides it either way. Retreating prefers full cover further away.
    /// </summary>
    private void FindCover(Vector3 threat, bool preferFar)
    {
        if (!NavReady)
            return;
        Rid map = GetWorld3D().NavigationMap;
        Vector3 threatPoint = threat + Vector3.Up * 1.3f;
        Vector3 best = Vector3.Zero;
        CoverKind bestKind = CoverKind.None;
        float bestScore = float.MinValue;

        // Rings every ≈1.5 m, 16 directions: cover spots behind small crates are easy to miss.
        int rings = Mathf.Max(2, Mathf.CeilToInt(Type.CoverSearchRadius / 1.5f));
        for (int ring = 0; ring <= rings; ring++)
        {
            float radius = ring * Type.CoverSearchRadius / rings;
            int samples = ring == 0 ? 1 : 16;
            for (int i = 0; i < samples; i++)
            {
                float angle = i * Mathf.Tau / samples + ring * 0.2f;
                Vector3 wanted = GlobalPosition + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 point = NavigationServer3D.MapGetClosestPoint(map, wanted);
                if (point.DistanceTo(wanted) > 1.2f || IsClaimed(point))
                    continue;
                float threatDistance = point.DistanceTo(threat);
                if (threatDistance < 5f)
                    continue;

                // Navigation points float a little above the floor: measure eye heights from the real ground.
                Vector3 ground = GroundBelow(point);
                bool crouchHidden = WorldBlocks(ground + Vector3.Up * (CrouchHeight - 0.15f), threatPoint);
                bool standHidden = WorldBlocks(ground + Vector3.Up * (StandHeight - 0.15f), threatPoint);
                CoverKind kind = crouchHidden && !standHidden ? CoverKind.Low : crouchHidden ? CoverKind.Full : CoverKind.None;
                // Fighting needs cover it can shoot over; full cover is only for falling back.
                if (kind == CoverKind.None || (!preferFar && kind != CoverKind.Low))
                    continue;

                float travel = GlobalPosition.DistanceTo(point);
                float score = preferFar
                    ? (kind == CoverKind.Full ? 60f : 45f) + threatDistance * 1.5f - travel * 1f
                    : (kind == CoverKind.Low ? 100f : 40f) - travel * 4f - Mathf.Abs(threatDistance - 20f) * 0.4f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = point;
                    bestKind = kind;
                }
            }
        }

        ReleaseCover();
        if (bestKind == CoverKind.None)
        {
            _coverKind = CoverKind.None;
            if (preferFar)
                MoveTo(GlobalPosition + (GlobalPosition - threat).Normalized() * 12f);
            return;
        }
        _cover = best;
        _coverKind = bestKind;
        _exposed = false;
        _phaseTimer = Type.HideSeconds * 0.5f;
        ClaimedCover.Add((this, best));
        MoveTo(best);
    }

    private Vector3 GroundBelow(Vector3 point)
    {
        _ray.From = point + Vector3.Up * 0.5f;
        _ray.To = point + Vector3.Down * 1.5f;
        _ray.CollisionMask = 1;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        return hit.Count > 0 ? hit["position"].AsVector3() : point;
    }

    private bool WorldBlocks(Vector3 from, Vector3 to)
    {
        _ray.From = from;
        _ray.To = to;
        _ray.CollisionMask = 1;
        return GetWorld3D().DirectSpaceState.IntersectRay(_ray).Count > 0;
    }

    private static bool IsClaimed(Vector3 point)
    {
        foreach ((Survivor owner, Vector3 claimed) in ClaimedCover)
        {
            if (owner.IsAlive && claimed.DistanceTo(point) < 1.5f)
                return true;
        }
        return false;
    }

    private void ReleaseCover()
    {
        ClaimedCover.RemoveAll(c => c.Owner == this);
        _cover = null;
        _coverKind = CoverKind.None;
    }

    private bool ReachedCover() => _cover is { } c && new Vector2(GlobalPosition.X - c.X, GlobalPosition.Z - c.Z).Length() < 0.8f;

    // ---------- Zombies up close ----------

    /// <summary>Shove off zombies that grab it or get within arm's reach.</summary>
    private void DefendAgainstZombies()
    {
        if (_shoveCooldown > 0f)
            return;
        foreach (Node node in GetTree().GetNodesInGroup(Zombie.Group))
        {
            if (node is not Zombie zombie || !zombie.IsAlive)
                continue;
            Vector3 to = zombie.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            if (to.Length() > 1.6f && !(zombie.IsGrabbing && _grabbedBy > 0))
                continue;
            zombie.ApplyShove(to.LengthSquared() > 0.01f ? to : -GlobalBasis.Z, 1f, _rng.Randf() < 0.3f);
            zombie.Health.ApplyDamage(BodyPart.Head, 5f, DamageSource.Shove);
            Weapons.PlayShove();
            _shoveCooldown = 1.2f;
            return;
        }
    }

    // ---------- Body ----------

    private float MoveSpeed()
    {
        float speed = State switch
        {
            SurvivorState.Engage or SurvivorState.Retreat => Type.RunSpeed,
            SurvivorState.Idle => Type.WalkSpeed * 0.7f,
            _ => Type.WalkSpeed,
        };
        if (IsCrouched)
            speed = Mathf.Min(speed, Type.CrouchSpeed);
        float grabbed = _grabbedBy > 0 ? 0.2f : 1f;
        return speed * Health.SpeedMultiplier * grabbed;
    }

    private void MoveTo(Vector3 point)
    {
        if (!NavReady)
            return;
        _agent.TargetPosition = NavigationServer3D.MapGetClosestPoint(GetWorld3D().NavigationMap, point);
    }

    private void StopMoving() => _agent.TargetPosition = GlobalPosition;

    private void UpdateStance(float dt)
    {
        float target = IsCrouched ? CrouchHeight : StandHeight;
        _height = Mathf.MoveToward(_height, target, 3.5f * dt);
        if (_collider.Shape is CylinderShape3D cylinder)
            cylinder.Height = _height;
        _collider.Position = new Vector3(0f, _height * 0.5f, 0f);
        _mesh.Scale = new Vector3(1f, _height / StandHeight, 1f);
        _hitboxes.SetHeightFraction(_height / StandHeight);
        Perception.Position = new Vector3(0f, _height - 0.15f, -0.1f);
    }

    private void Face(float dt, Vector3 moving)
    {
        Vector3 look = _hasAimPoint && State != SurvivorState.Idle ? _aimPoint - GlobalPosition : moving;
        look.Y = 0f;
        if (look.LengthSquared() < 0.0001f)
            return;
        float targetYaw = Mathf.Atan2(-look.X, -look.Z);
        Rotation = new Vector3(0f, Mathf.RotateToward(Rotation.Y, targetYaw, Mathf.DegToRad(300f) * dt), 0f);
    }

    // ---------- Reactions ----------

    private void OnDamaged(BodyPart part, float amount)
    {
        Suppression = Mathf.Min(1f, Suppression + 0.5f);
        Bark("I'm hit!");
        if (State is SurvivorState.Idle or SurvivorState.Alert)
            BecomeAlert();
    }

    /// <summary>Bullets landing close by keep its head down.</summary>
    private void OnNoise(NoiseEvent noise)
    {
        if (State == SurvivorState.Dead || noise.Kind != NoiseKind.Impact)
            return;
        float distance = (GlobalPosition + Vector3.Up * 1.2f).DistanceTo(noise.Position);
        if (distance < 2.5f)
            Suppression = Mathf.Min(1f, Suppression + 0.35f * (1f - distance / 2.5f) + 0.1f);
    }

    private void Bark(string text)
    {
        if (_barkCooldown > 0f && text == _lastBark)
            return;
        _bark.Text = text;
        _bark.Visible = true;
        _barkLeft = 2f;
        _lastBark = text;
        _barkCooldown = 3f;
    }

    private void UpdateBark(float dt)
    {
        if (_barkLeft <= 0f)
            return;
        _barkLeft -= dt;
        if (_barkLeft <= 0f)
            _bark.Visible = false;
    }

    // ---------- Death ----------

    private void OnDied()
    {
        State = SurvivorState.Dead;
        ReleaseCover();
        CollisionLayer = 0;
        Weapons.Visible = false;
        Weapons.SetProcess(false);
        Weapons.TriggerHeld = false;
        _bark.Visible = false;
        Perception.Enabled = false;
        if (GetNodeOrNull<AimAssistTarget>("AimAssistTarget") is { } assist)
            assist.Hostile = false;

        // Spare magazines spill out next to the body.
        PackedScene? magScene = Weapons.Weapon?.Data.DroppedMagazineScene;
        if (magScene != null)
        {
            int i = 0;
            while (_pouch.TakeFullest() is { } magazine)
            {
                var dropped = magScene.Instantiate<DroppedMagazine>();
                dropped.Magazine = magazine;
                GetTree().CurrentScene.AddChild(dropped);
                dropped.GlobalPosition = GlobalPosition + new Vector3(0.4f * Mathf.Cos(i * 2.1f), 0.3f, 0.4f * Mathf.Sin(i * 2.1f));
                i++;
            }
        }

        if (Infection.IsInfected && ZombieScene != null)
            _riseTimer = _rng.RandfRange(8f, 15f);
    }

    private void UpdateDead(float dt)
    {
        _deadTime += dt;
        _body.Rotation = new Vector3(Mathf.DegToRad(-85f) * Mathf.Min(_deadTime * 2.5f, 1f), 0f, 0f);
        if (_riseTimer < 0f)
            return;
        _riseTimer -= dt;
        if (_riseTimer > 0f)
            return;
        // The infected dead get back up.
        var zombie = ZombieScene!.Instantiate<Node3D>();
        GetTree().CurrentScene.AddChild(zombie);
        zombie.GlobalPosition = GlobalPosition + Vector3.Up * 0.05f;
        zombie.Rotation = Rotation;
        QueueFree();
    }

    private void UpdateDebug()
    {
        if (_debug == null || !_debug.Visible)
            return;
        string target = _target == null ? "-" : _target.Team == SenseTeam.Zombie ? "zombie" : "player/npc";
        _debug.Text = $"{State} {(_cover != null ? _coverKind + " cover" : "no cover")}{(IsCrouched ? " crouched" : "")}\n"
            + $"target {target}  seen {(CanSeeTarget ? "yes" : "no")}  aim {_aimSettle:0.0}  supp {Suppression:0.0}\n"
            + $"mag {Weapons.Magazine?.Rounds ?? 0}{(Weapons.IsBusy ? " BUSY" : "")}  head {Mathf.Ceil(Health.Hp(BodyPart.Head))} chest {Mathf.Ceil(Health.Hp(BodyPart.Chest))}";
    }
}
