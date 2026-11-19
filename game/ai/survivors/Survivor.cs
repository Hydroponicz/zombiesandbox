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
    /// <summary>The group has the upper hand: move up on the enemy's position while the others cover.</summary>
    Push,
    /// <summary>Solo or outnumbered and losing: run off while it still can.</summary>
    Flee,
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
    /// <summary>Its faction: attitude toward the player and who its enemies are.</summary>
    [Export] public FactionData? Faction { get; set; }
    /// <summary>This one's melee weapon (overrides the random pick from its type's list).</summary>
    [Export] public PackedScene? MeleeWeaponOverride { get; set; }
    /// <summary>Spawned when an infected survivor dies and turns.</summary>
    [Export] public PackedScene? ZombieScene { get; set; }

    public SurvivorState State { get; private set; } = SurvivorState.Idle;
    public BodyHealth Health { get; private set; } = null!;
    public Infection Infection { get; private set; } = null!;
    Infection? IAttackable.Infection => Infection;
    public Perception Perception { get; private set; } = null!;
    public WeaponHolder Weapons { get; private set; } = null!;
    public CharacterSkills Skills { get; private set; } = null!;
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
    /// <summary>Last time it heard an enemy fire (seconds), and roughly where from.</summary>
    private double _enemyShotTime = -100.0;
    private Vector3 _enemyShotPosition;
    private bool _enemyShotNew;
    /// <summary>Last time it knew of a human enemy (seen, heard shooting, or reported by a mate).</summary>
    private double _humanThreatTime = -100.0;
    private double _lastSawEnemyTime = -100.0;

    // Pushing (see DESIGN.md step 5): judging the upper hand, flanking, covering fire.
    /// <summary>How much of an upper hand the group has right now (pushes at Type.PushThreshold).</summary>
    public float Advantage { get; private set; }
    private float _advantageTimer;

    // Attitude toward the player (neutrals read body language).
    /// <summary>0..1: how close a neutral is to turning on the player.</summary>
    public float Nervousness { get; private set; }
    private int _nervousStage;
    private float _attitudeTimer;
    private bool _watchingPlayer;
    private PlayerController? _player;
    private AimAssistTarget? _assist;
    private Vector3 _pushGoal;
    private bool _pushFinalLeg;
    private bool _resumePush;
    private float _sincePushMove;
    private float _flankSign = 1f;
    private bool _coveringFire;
    private SurvivorGroup.Combat _memory = null!;

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
    /// <summary>Per-shot hand wobble (unit disk), re-rolled after every shot: aim never becomes perfect.</summary>
    private Vector3 _wobble;
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

        var skills = Skills = GetNode<CharacterSkills>("Skills");
        var stats = GetNode<CharacterStats>("Stats");
        CallDeferred(MethodName.ApplySkills, skills);
        stats.Initialize(skills);
        Health.Initialize(null);

        _selfExclude.Add(GetRid());
        _hitboxes.AddRidsTo(_selfExclude);
        Weapons.Initialize(stats, skills, _pouch, _selfExclude, worldMask: 1, bulletMask: 1 | HitboxRig.HitboxLayer);
        Weapons.GiveSpareMagazines(3);
        _gun = Weapons.Weapon;
        PackedScene? meleeScene = MeleeWeaponOverride
            ?? (Type.MeleeWeapons.Count > 0 ? Type.MeleeWeapons[_rng.RandiRange(0, Type.MeleeWeapons.Count - 1)] : null);
        _meleeWeapon = meleeScene?.Instantiate<MeleeWeapon>();
        Weapons.MeleeBroke += _ =>
        {
            _meleeWeapon = null;
            Bark("It broke!");
            LeaveMelee();
        };
        if (Weapons.FireMode == FireMode.Auto)
            Weapons.CycleFireMode(); // Semi-auto, aimed shots.

        Perception.SightRangeDay = Type.SightRangeDay;
        Perception.SightRangeNight = Type.SightRangeNight;
        Perception.FieldOfViewDegrees = Type.FieldOfViewDegrees;
        Perception.DetectionTime = Type.DetectionTime;
        Perception.Self = this;
        Perception.TargetFilter = IsEnemy;
        Perception.SelfExclude.AddRange(_selfExclude);

        _ray.Exclude = _selfExclude;
        _memory = SurvivorGroup.Memory(GroupId);
        _flankSign = _rng.Randf() < 0.5f ? -1f : 1f;
        Weapons.ShotLanded += OnShotLanded;
        Health.Attacked += OnAttacked;
        _assist = GetNodeOrNull<AimAssistTarget>("AimAssistTarget");
        if (Faction != null && GetNodeOrNull<MeshInstance3D>("Body/Mesh/Armband") is { } armband)
            armband.MaterialOverride = new StandardMaterial3D { AlbedoColor = Faction.Color };
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
        skills.SetLevel(SkillId.Blunt, Type.Blunt);
        skills.SetLevel(SkillId.Blade, Type.Blade);
    }

    /// <summary>Developer toggle (Ctrl+M): survivors ignore the player, to watch them fight zombies.</summary>
    public static bool DebugIgnorePlayer { get; set; }

    /// <summary>Zombies always; the player if its faction is hostile to him; NPCs by faction.</summary>
    private bool IsEnemy(ISenseTarget target) => target switch
    {
        PlayerController => !DebugIgnorePlayer && AttitudeToPlayer == Attitude.Hostile,
        Survivor other => other.GroupId != GroupId && FactionRelations.AreEnemies(Faction, other.Faction),
        _ => true,
    };

    public Attitude AttitudeToPlayer => Faction == null ? Attitude.Hostile : FactionRelations.Instance.AttitudeOf(Faction);

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
        UpdateAttitude(dt);
        UpdateMeleeMode(dt);
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
        // Targets can be freed (a corpse rising, a zombie removed): forget them.
        if (_target != null && !SenseTargets.IsValid(_target))
            _target = null;
        if (_aimTarget != null && !SenseTargets.IsValid(_aimTarget))
            _aimTarget = null;
        if (Perception.CanSeeTarget && Perception.Target != null && SenseTargets.IsValid(Perception.Target))
        {
            if (_target != Perception.Target)
                StartAimingAt(Perception.Target);
            _target = Perception.Target;
            _threatPosition = _target.GroundPosition;
            _threatTime = Now;
            _lastHeardCount = Perception.HeardCount;
            if (_target.Team == SenseTeam.Living)
            {
                _humanThreatTime = Now;
                _lastSawEnemyTime = Now;
                SurvivorGroup.Report(GroupId, _target, _threatPosition);
            }
        }
        else if (_target != null && !_target.IsAlive)
        {
            _target = null;
        }
    }

    private bool CanSeeTarget => Perception.CanSeeTarget && _target != null && Perception.Target == _target && SenseTargets.IsValid(_target);

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
            else if (State == SurvivorState.Flee && GlobalPosition.DistanceTo(_target!.GroundPosition) > 8f)
            {
                // Running: only turn and fight if cornered.
            }
            else if (State == SurvivorState.Push && _target!.Team == SenseTeam.Living
                && GlobalPosition.DistanceTo(_target.GroundPosition) < 20f)
            {
                // Pushing and found him: press the attack instead of falling back to cover.
            }
            else
            {
                State = SurvivorState.Engage;
            }
        }
        else if (_enemyShotNew && State is SurvivorState.Engage or SurvivorState.Retreat or SurvivorState.Search or SurvivorState.Alert or SurvivorState.Push)
        {
            // Shot at by someone it can't see: stay in (or get back to) cover facing the shots,
            // rather than walking out to look for them.
            _threatPosition = _enemyShotPosition;
            _threatTime = Now;
            SurvivorGroup.ReportShots(GroupId, _enemyShotPosition);
            if (State == SurvivorState.Search)
            {
                Bark("Over there!");
                SurvivorGroup.ReleaseMover(GroupId, this);
                BecomeAlert();
            }
            else if (State == SurvivorState.Alert)
            {
                _stateTimer = 25f;
            }
        }
        else if (State is SurvivorState.Engage or SurvivorState.Retreat && !knowsThreat)
        {
            State = SurvivorState.Idle;
            ReleaseCover();
        }
        else if (State is SurvivorState.Engage or SurvivorState.Retreat && knowsThreat)
        {
            // Lost sight: wait a while in cover (longer if they were shooting at us), then one member
            // of the group goes to look while the others keep watching from cover.
            double quietFor = Mathf.Min(Now - _threatTime, Now - _enemyShotTime);
            if (State == SurvivorState.Engage && Now - _threatTime > 6.0 && quietFor > 10.0
                && _threatPosition.DistanceTo(_home) <= Type.LeashRange)
            {
                if (SurvivorGroup.TryClaimMover(GroupId, this))
                {
                    State = SurvivorState.Search;
                    _stateTimer = Type.SearchSeconds;
                    Bark("Cover me, I'll check it.");
                    ReleaseCover();
                    MoveTo(_threatPosition);
                }
                else
                {
                    BecomeAlert();
                }
            }
        }
        else if (State is SurvivorState.Idle or SurvivorState.Alert)
        {
            // A group mate's sighting or a sound: get ready, facing it.
            SurvivorGroup.Sighting? shared = SurvivorGroup.Recent(GroupId, 6.0);
            Vector3? shots = SurvivorGroup.RecentShots(GroupId, 3.0);
            if (shared is { } s && s.Target.IsAlive && Now - _threatTime > 1.0)
            {
                if (s.Target.Team == SenseTeam.Living)
                    _humanThreatTime = Now;
                if (State == SurvivorState.Idle)
                    Bark("Where?");
                _threatPosition = s.Position;
                _threatTime = s.Time;
                BecomeAlert();
            }
            else if (shots is { } shotPosition && Now - _threatTime > 1.0)
            {
                _humanThreatTime = Now;
                _threatPosition = shotPosition;
                _threatTime = Now;
                BecomeAlert();
            }
            else if (Perception.HeardCount != _lastHeardCount && Now - _humanThreatTime > 10.0)
            {
                _lastHeardCount = Perception.HeardCount;
                _threatPosition = Perception.HeardPosition;
                _threatTime = Now;
                BecomeAlert();
            }
        }

        _enemyShotNew = false;
        ConsiderPushing(dt);
        if (State is not (SurvivorState.Search or SurvivorState.Push))
            SurvivorGroup.ReleaseMover(GroupId, this);
        ConsiderFleeing();
        _coveringFire = State is SurvivorState.Engage or SurvivorState.Alert && !CanSeeTarget
            && SurvivorGroup.Mover(GroupId) is Survivor { State: SurvivorState.Push } mover && mover != this
            && Now - _humanThreatTime < 12.0;

        switch (State)
        {
            case SurvivorState.Idle: DoIdle(dt); break;
            case SurvivorState.Alert: DoAlert(dt); break;
            case SurvivorState.Engage: DoEngage(dt); break;
            case SurvivorState.Retreat: DoRetreat(dt); break;
            case SurvivorState.Search: DoSearch(dt); break;
            case SurvivorState.Push: DoPush(dt); break;
            case SurvivorState.Flee: DoFlee(dt); break;
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
        if (_watchingPlayer)
        {
            StopMoving();
            return;
        }
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
        IsCrouched = ReachedCover() && !_coveringFire;
        if (_coveringFire)
        {
            _stateTimer = Mathf.Max(_stateTimer, 5f);
            HandleWeaponUpkeep();
            TryCoveringShot();
        }
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
            // Target gone (killed): stay ready for a while.
            BecomeAlert();
            return;
        }
        _coverTimer -= dt;
        bool zombie = _target!.Team == SenseTeam.Zombie;
        float distance = GlobalPosition.DistanceTo(_target.GroundPosition);

        // Close zombies: fight with the melee weapon if it's out; otherwise stand and shoot (or shove).
        if (zombie && _meleeMode)
        {
            ReleaseCover();
            IsCrouched = false;
            MeleeFight((Zombie)_target);
            return;
        }
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

        if (_coveringFire)
        {
            // A mate is moving up: rise and keep the enemy's head down at his last position.
            IsCrouched = Suppression > 0.6f;
            AimAtThreatArea();
            HandleWeaponUpkeep();
            TryCoveringShot();
            return;
        }

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
            SurvivorGroup.ReleaseMover(GroupId, this);
            MoveTo(_home);
        }
    }

    // ---------- Attitude toward the player ----------

    /// <summary>
    /// Neutrals read body language: aiming at them, sprinting at them, crowding them and shooting
    /// next to them make them nervous; they watch, warn, warn again, then turn hostile.
    /// Friendlies just complain. Runs five times a second.
    /// </summary>
    private void UpdateAttitude(float dt)
    {
        if (_watchingPlayer && _player != null)
        {
            _aimPoint = _player.SightPoint - Vector3.Up * 0.3f;
            _aimError = Vector3.Zero;
            _hasAimPoint = true;
        }
        _attitudeTimer -= dt;
        if (_attitudeTimer > 0f)
            return;
        float step = 0.2f - _attitudeTimer;
        _attitudeTimer = 0.2f;

        _player ??= GetTree().GetFirstNodeInGroup(PlayerController.Group) as PlayerController;
        Attitude attitude = AttitudeToPlayer;
        if (_assist != null)
            _assist.Hostile = attitude == Attitude.Hostile;

        bool calmState = State is SurvivorState.Idle or SurvivorState.Alert;
        if (_player == null || !_player.IsAlive || attitude == Attitude.Hostile || !calmState)
        {
            Nervousness = Mathf.Max(0f, Nervousness - 0.25f * step);
            _watchingPlayer = false;
            _nervousStage = 0;
            return;
        }

        bool sees = SeesPlayer(out float distance);
        bool aimedAt = sees && PlayerAimingAtMe();
        if (attitude == Attitude.Friendly)
        {
            Nervousness = 0f;
            _watchingPlayer = aimedAt;
            if (aimedAt)
                Bark("Hey, watch where you point that!");
            return;
        }

        float rise = 0f;
        if (sees)
        {
            if (aimedAt)
                rise += 0.65f;
            if (_player.IsSprinting && distance < 15f && SprintingTowardMe())
                rise += 0.4f;
            if (distance < 3f)
                rise += 0.15f;
        }
        Nervousness = rise > 0f ? Mathf.Min(1f, Nervousness + rise * step) : Mathf.Max(0f, Nervousness - 0.25f * step);

        int stage = Nervousness >= 1f ? 4 : Nervousness >= 0.75f ? 3 : Nervousness >= 0.45f ? 2 : Nervousness >= 0.2f ? 1 : 0;
        if (stage > _nervousStage)
        {
            if (stage == 2)
                Bark(aimedAt ? "Lower your weapon!" : "Back off!");
            else if (stage == 3)
                Bark("Last warning!");
            else if (stage == 4)
            {
                Bark("That's it!");
                TurnOnPlayer();
            }
        }
        else if (stage == 0 && _nervousStage >= 2)
        {
            Bark("...Alright.");
        }
        _nervousStage = stage;
        _watchingPlayer = sees && stage >= 1;
    }

    private bool SeesPlayer(out float distance)
    {
        distance = GlobalPosition.DistanceTo(_player!.GlobalPosition);
        if (distance > 30f)
            return false;
        Vector3 eye = Perception.GlobalPosition;
        Vector3 to = _player.SightPoint - eye;
        Vector3 flat = new(to.X, 0f, to.Z);
        Vector3 forward = -GlobalBasis.Z;
        if (distance > 6f && forward.AngleTo(flat) > Mathf.DegToRad(Type.FieldOfViewDegrees * 0.5f))
            return false;
        return !WorldBlocks(eye, _player.SightPoint);
    }

    /// <summary>The player's aim line passes close to this one's body.</summary>
    private bool PlayerAimingAtMe()
    {
        WeaponHolder weapons = _player!.Weapons;
        if (!weapons.IsAiming)
            return false;
        Vector3 origin = weapons.AimPivot.Origin;
        Vector3 direction = -weapons.AimPivot.Basis.Z.Normalized();
        Vector3 toMe = GlobalPosition + Vector3.Up * (_height * 0.65f) - origin;
        float along = toMe.Dot(direction);
        if (along <= 0f)
            return false;
        float miss = (toMe - direction * along).Length();
        return miss < 0.6f + along * 0.015f;
    }

    private bool SprintingTowardMe()
    {
        Vector3 velocity = _player!.Velocity with { Y = 0f };
        Vector3 toMe = (GlobalPosition - _player.GlobalPosition) with { Y = 0f };
        return velocity.LengthSquared() > 1f && toMe.LengthSquared() > 0.01f && velocity.Normalized().Dot(toMe.Normalized()) > 0.8f;
    }

    /// <summary>Shot by someone: the player angers the whole faction (friendlies forgive one hit).</summary>
    private void OnAttacked(Node attacker)
    {
        if (attacker is not PlayerController player || Faction == null)
            return;
        Attitude attitude = AttitudeToPlayer;
        if (attitude == Attitude.Friendly && FactionRelations.Instance.TryForgiveHit(Faction))
        {
            Bark("Watch your fire!");
            return;
        }
        if (attitude != Attitude.Hostile)
        {
            _player = player;
            TurnOnPlayer();
            Bark("You'll pay for that!");
        }
    }

    private void TurnOnPlayer()
    {
        if (Faction != null)
            FactionRelations.Instance.Anger(Faction);
        _watchingPlayer = false;
        Nervousness = 0f;
        _nervousStage = 0;
        if (_player == null)
            return;
        // Everyone in the group knows where he is.
        _threatPosition = _player.GlobalPosition;
        _threatTime = Now;
        _humanThreatTime = Now;
        SurvivorGroup.Report(GroupId, _player, _threatPosition);
        BecomeAlert();
    }

    // ---------- Fleeing ----------

    /// <summary>Living enemies (player, hostile NPCs) within range: a rough head count.</summary>
    private int CountHumanEnemies(float range)
    {
        int count = 0;
        foreach (ISenseTarget target in SenseTargets.All)
        {
            if (target.IsAlive && target.Team == SenseTeam.Living && target != this && IsEnemy(target)
                && target.GroundPosition.DistanceTo(GlobalPosition) < range)
                count++;
        }
        return count;
    }

    private float NearestHumanEnemyDistance() =>
        NearestHumanEnemy() is { } enemy ? enemy.GroundPosition.DistanceTo(GlobalPosition) : float.MaxValue;

    private ISenseTarget? NearestHumanEnemy()
    {
        ISenseTarget? nearest = null;
        float best = float.MaxValue;
        foreach (ISenseTarget target in SenseTargets.All)
        {
            if (!target.IsAlive || target.Team != SenseTeam.Living || target == this || !IsEnemy(target))
                continue;
            float distance = target.GroundPosition.DistanceTo(GlobalPosition);
            if (distance < best)
            {
                best = distance;
                nearest = target;
            }
        }
        return nearest;
    }

    /// <summary>Solo or outnumbered and losing, with room to get away: run.</summary>
    private void ConsiderFleeing()
    {
        if (Type.FleeThreshold <= -50f || _advantageTimer < 0.49f)
            return; // Never runs, or the advantage wasn't just re-measured.
        bool humanThreat = Now - _humanThreatTime < 15.0 && (_target == null || _target.Team == SenseTeam.Living);
        if (State is not (SurvivorState.Engage or SurvivorState.Alert or SurvivorState.Retreat) || !humanThreat)
            return;
        if (Advantage >= Type.FleeThreshold || NearestHumanEnemyDistance() < 8f || ZombieNearby())
            return;
        StartFlee();
    }

    private void StartFlee()
    {
        State = SurvivorState.Flee;
        _stateTimer = 30f;
        ReleaseCover();
        IsCrouched = false;
        Bark("I'm getting out of here!");
        // Run away from the threat; try a few directions and take the one that ends furthest from it.
        Vector3 from = NearestHumanEnemy() is { } enemy ? enemy.GroundPosition : _threatPosition;
        _threatPosition = from;
        Vector3 away = (GlobalPosition - from) with { Y = 0f };
        away = away.LengthSquared() > 0.01f ? away.Normalized() : GlobalBasis.Z;
        Vector3 best = GlobalPosition;
        float bestDistance = 0f;
        foreach (float degrees in new[] { 0f, 35f, -35f, 70f, -70f })
        {
            Vector3 wanted = GlobalPosition + away.Rotated(Vector3.Up, Mathf.DegToRad(degrees)) * 35f;
            Vector3 point = NavReady ? NavigationServer3D.MapGetClosestPoint(GetWorld3D().NavigationMap, wanted) : wanted;
            float distance = point.DistanceTo(_threatPosition);
            if (distance > bestDistance)
            {
                best = point;
                bestDistance = distance;
            }
        }
        _sincePushMove = 0f;
        MoveTo(best);
    }

    private void DoFlee(float dt)
    {
        _stateTimer -= dt;
        _sincePushMove += dt;
        _hasAimPoint = false;
        IsCrouched = false;
        bool arrived = NavReady && _agent.IsNavigationFinished() && _sincePushMove > 0.5f;
        if ((arrived || _stateTimer <= 0f) && !CanSeeTarget && Now - _lastSawEnemyTime > 5.0)
        {
            // Got away: settle down here.
            State = SurvivorState.Idle;
            _home = GlobalPosition;
            _target = null;
        }
        else if (arrived)
        {
            StartFlee(); // Still too close: keep going.
        }
    }

    // ---------- Pushing ----------

    /// <summary>
    /// Judge the upper hand a couple of times a second; when it's big enough and the enemy is hiding,
    /// one member moves up on him while the others give covering fire.
    /// </summary>
    private void ConsiderPushing(float dt)
    {
        _advantageTimer -= dt;
        if (_advantageTimer > 0f)
            return;
        _advantageTimer = 0.5f;
        Advantage = MeasureAdvantage();

        bool humanThreat = Now - _humanThreatTime < 15.0 && (_target == null || _target.Team == SenseTeam.Living);
        // He's hiding from this one (mates may still see him: they'll cover).
        bool hiddenEnemy = !CanSeeTarget && Now - _lastSawEnemyTime > 2.0 && Now - _threatTime < Type.SearchSeconds;
        if (State is not (SurvivorState.Engage or SurvivorState.Alert) || !humanThreat || !hiddenEnemy)
            return;
        if (Advantage < Type.PushThreshold || Health.Danger >= Type.RetreatDanger * 0.6f || Suppression > 0.4f
            || Now - _memory.LastPushTime < Type.PushCooldown || ZombieNearby()
            || _threatPosition.DistanceTo(_home) > Type.LeashRange) // Don't chase fights far from home.
            return;
        if (!SurvivorGroup.TryClaimMover(GroupId, this))
            return;
        StartPush();
    }

    /// <summary>
    /// Numbers, hits landed on the enemy, the enemy reloading, and the enemy's fire not hitting
    /// anyone add up; being hurt, pinned down or losing members takes away.
    /// </summary>
    private float MeasureAdvantage()
    {
        int alive = 0;
        foreach (Node node in GetTree().GetNodesInGroup(Group))
        {
            if (node is Survivor { IsAlive: true } mate && mate.GroupId == GroupId)
                alive++;
        }
        _memory.MostMembers = Mathf.Max(_memory.MostMembers, alive);
        int lost = _memory.MostMembers - alive;

        float score = 0.25f * (alive - 1) - 0.3f * lost - 0.25f * Mathf.Max(0, CountHumanEnemies(40f) - 1);
        if (Now - _memory.EnemyHitTime < 20.0)
            score += 0.35f;
        if (Now - _memory.EnemyReloadTime < 4.0)
            score += 0.4f;
        if (Now - _memory.AllyHitTime > 15.0)
            score += 0.2f;
        score -= Suppression * 0.6f + Health.Danger * 0.8f;
        return score;
    }

    private bool ZombieNearby()
    {
        foreach (Node node in GetTree().GetNodesInGroup(Zombie.Group))
        {
            if (node is Zombie { IsAlive: true } zombie && zombie.GlobalPosition.DistanceTo(GlobalPosition) < 10f)
                return true;
        }
        return false;
    }

    private void StartPush()
    {
        State = SurvivorState.Push;
        _stateTimer = 20f;
        _memory.LastPushTime = Now;
        ReleaseCover();
        IsCrouched = false;
        // Come at the hiding spot from the side, a few metres out, rather than straight down his sights.
        Vector3 toThreat = _threatPosition - GlobalPosition;
        toThreat.Y = 0f;
        Vector3 forward = toThreat.LengthSquared() > 0.01f ? toThreat.Normalized() : -GlobalBasis.Z;
        Vector3 side = forward.Cross(Vector3.Up) * _flankSign;
        float back = Mathf.Min(5f, toThreat.Length() * 0.5f);
        _pushGoal = _threatPosition - forward * back + side * 5f;
        _pushFinalLeg = false;
        MoveTo(_pushGoal);
        _sincePushMove = 0f;
        Bark(Now - _memory.EnemyReloadTime < 4.0 ? "Push him, he's reloading!" : "Moving up!");
    }

    private void DoPush(float dt)
    {
        _stateTimer -= dt;
        AimAtThreatArea();
        HandleWeaponUpkeep();
        IsCrouched = false;
        if (Suppression > 0.6f)
        {
            AbortPush();
            return;
        }
        if (CanSeeTarget)
        {
            // Found him: stop and shoot; keep coming if he ducks out of sight again.
            _stateTimer = Mathf.Max(_stateTimer, 6f);
            StopMoving();
            AimAtTarget();
            TryShoot();
            _pushFinalLeg = true;
            _pushGoal = _target!.GroundPosition;
            _resumePush = true;
            return;
        }
        _sincePushMove += dt;
        if (_resumePush)
        {
            _resumePush = false;
            MoveTo(_pushGoal);
            _sincePushMove = 0f;
        }
        Vector3 flat = new(GlobalPosition.X - _pushGoal.X, 0f, GlobalPosition.Z - _pushGoal.Z);
        // (Navigation reports "finished" for a moment after a new target is set: ignore the first half second.)
        bool arrived = flat.Length() < 1.5f || (NavReady && _agent.IsNavigationFinished() && _sincePushMove > 0.5f);
        if (arrived && !_pushFinalLeg)
        {
            // Flank reached and still no sight of him: go to where he was.
            _pushFinalLeg = true;
            _pushGoal = _threatPosition;
            MoveTo(_pushGoal);
            _sincePushMove = 0f;
        }
        else if ((arrived && _pushFinalLeg) || _stateTimer <= 0f)
        {
            // Nobody there: look around for him.
            State = SurvivorState.Search;
            _stateTimer = Type.SearchSeconds;
        }
    }

    private void AbortPush()
    {
        Bark("Pinned down!");
        State = SurvivorState.Engage;
        _coverTimer = 0f;
        _memory.LastPushTime = Now;
        SurvivorGroup.ReleaseMover(GroupId, this);
    }

    private void OnShotLanded(bool hitTarget)
    {
        if (hitTarget && _aimTarget is { Team: SenseTeam.Living } && _aimTarget.IsAlive)
        {
            _memory.EnemyHitTime = Now;
            if (_rng.Randf() < 0.5f)
                Bark("He's hit!");
        }
    }

    /// <summary>Fire at where the enemy is hiding (walls and all) so he keeps his head down.</summary>
    private void TryCoveringShot()
    {
        if (_shotTimer > 0f || !Weapons.IsAiming || Weapons.AdsBlend < 0.9f || !_hasAimPoint || Weapons.IsBusy)
            return;
        if (Weapons.IsJammed || NeedsReload())
        {
            HandleWeaponUpkeep();
            return;
        }
        if (FriendlyInLine())
            return;
        Weapons.PullTrigger();
        RerollWobble();
        _shotTimer = _rng.RandfRange(Type.CoveringFireIntervalMin, Type.CoveringFireIntervalMax);
    }

    private bool FriendlyInLine()
    {
        _ray.From = Weapons.AimPivot.Origin;
        _ray.To = _aimPoint;
        _ray.CollisionMask = HitboxRig.HitboxLayer;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        if (hit.Count == 0)
            return false;
        for (Node? node = hit["collider"].AsGodotObject() as Node; node != null; node = node.GetParent())
        {
            if (node is Survivor other)
                return !IsEnemy(other);
            if (node is PlayerController player)
                return !IsEnemy(player);
        }
        return false;
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
            float wobble = Type.AimWobble * DistanceScale() * (1f + 2f * Suppression);
            _aimError = _aimErrorDirection * (InitialErrorLength() * remaining) + _wobble * wobble;
        }
        else
        {
            AimAtThreatArea();
        }
    }

    private float InitialErrorLength() => Type.InitialAimError * DistanceScale();

    /// <summary>Aim errors are given for 20 m and grow with distance.</summary>
    private float DistanceScale()
    {
        if (_aimTarget == null)
            return 0f;
        float distance = GlobalPosition.DistanceTo(_aimTarget.GroundPosition);
        return Mathf.Clamp(distance / 20f, 0.5f, 3f);
    }

    private void RerollWobble()
    {
        float angle = _rng.Randf() * Mathf.Tau;
        float radius = Mathf.Sqrt(_rng.Randf());
        Vector3 side = Weapons.AimPivot.Basis.X;
        _wobble = (side * Mathf.Cos(angle) + Vector3.Up * Mathf.Sin(angle)) * radius;
    }

    private void AimAtThreatArea()
    {
        _aimPoint = _threatPosition + Vector3.Up * 1.3f;
        _aimError = _wobble * Type.AimWobble * 1.5f; // Firing blind: scattered.
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
        RerollWobble();
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
            if (node == _target as Node)
                return true;
            if (node is Survivor other)
                return IsEnemy(other);
            if (node is PlayerController player)
                return IsEnemy(player);
        }
        return false; // Something solid in the way.
    }

    private bool NeedsReload() =>
        Weapons.Magazine == null || (Weapons.Magazine.Rounds <= 3 && !Weapons.IsBusy && _pouch.FullestRounds() > Weapons.Magazine.Rounds);

    private void HandleWeaponUpkeep()
    {
        if (Weapons.IsBusy || Weapons.Weapon == null)
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
        bool fighting = State is SurvivorState.Engage or SurvivorState.Retreat or SurvivorState.Push;
        Weapons.WantsAim = (fighting && CanSeeTarget && (!IsCrouched || _coverKind != CoverKind.Low))
            || (_coveringFire && !IsCrouched)
            || (_watchingPlayer && _nervousStage >= 2); // Warning: weapon up, finger off the trigger.
        Weapons.TriggerHeld = _meleeHoldLeft > 0f; // Charging a heavy swing.
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
        // With a melee weapon in hand it only shoves to break a grab.
        bool armed = _meleeMode && Weapons.Melee != null;
        foreach (Node node in GetTree().GetNodesInGroup(Zombie.Group))
        {
            if (node is not Zombie zombie || !zombie.IsAlive)
                continue;
            Vector3 to = zombie.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            bool grabbingMe = zombie.IsGrabbing && _grabbedBy > 0;
            if ((armed || to.Length() > 1.6f) && !grabbingMe)
                continue;
            zombie.ApplyShove(to.LengthSquared() > 0.01f ? to : -GlobalBasis.Z, 1f, _rng.Randf() < 0.3f);
            zombie.Health.ApplyDamage(BodyPart.Head, 5f, DamageSource.Shove);
            Weapons.PlayShove();
            Bark("Get off!");
            _shoveCooldown = 1.2f;
            return;
        }
    }

    // ---------- Melee ----------

    private Weapon? _gun;
    private MeleeWeapon? _meleeWeapon;
    private bool _meleeMode;
    private float _meleeClearTime;
    private float _meleeHoldLeft;

    /// <summary>A zombie up close: get the melee weapon out. All clear for a moment: back to the gun.</summary>
    private void UpdateMeleeMode(float dt)
    {
        _meleeHoldLeft -= dt;
        if (_meleeWeapon == null)
            return;
        float nearest = NearestZombieDistance();
        if (!_meleeMode)
        {
            if (nearest < Type.MeleeSwitchRange && !Weapons.IsSwitching && !Weapons.IsBusy)
            {
                _meleeMode = true;
                _meleeClearTime = 0f;
                Weapons.SwitchTo(_meleeWeapon);
                Bark("Too close!");
            }
            return;
        }
        // Keep it out while zombies are near (or a blade is stuck); a human enemy in view also means the gun.
        bool humanInView = CanSeeTarget && _target!.Team == SenseTeam.Living;
        _meleeClearTime = nearest > 5f || (humanInView && nearest > Type.MeleeSwitchRange) ? _meleeClearTime + dt : 0f;
        if (_meleeClearTime > 1.2f && !Weapons.IsMeleeStuck)
            LeaveMelee();
    }

    private void LeaveMelee()
    {
        _meleeMode = false;
        _meleeHoldLeft = 0f;
        if (_gun != null && Weapons.Held != _gun)
            Weapons.SwitchTo(_gun);
    }

    private float NearestZombieDistance()
    {
        float nearest = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup(Zombie.Group))
        {
            if (node is Zombie { IsAlive: true } zombie)
                nearest = Mathf.Min(nearest, zombie.GlobalPosition.DistanceTo(GlobalPosition));
        }
        return nearest;
    }

    /// <summary>
    /// Melee against a zombie, by the same rules as the player: aim at the head, swing when it's in reach
    /// (sometimes a charged heavy swing), stomp it when it's down, pull a stuck blade free.
    /// </summary>
    private void MeleeFight(Zombie zombie)
    {
        if (Weapons.Melee is not { } melee)
        {
            StopMoving(); // Still getting it out.
            return;
        }
        // The head, plus a little human error that shrinks with skill.
        int skill = Skills.GetLevel(melee.Data.Kind == MeleeKind.Blade ? SkillId.Blade : SkillId.Blunt);
        _aimPoint = zombie.HeadPoint;
        _aimError = _wobble * Mathf.Lerp(0.18f, 0.04f, skill / 10f);
        _hasAimPoint = true;

        if (Weapons.IsMeleeStuck)
        {
            StopMoving();
            if (Weapons.MeleeState == MeleePhase.Stuck)
                Weapons.PullTrigger(); // Start pulling it free.
            return;
        }

        Vector3 eye = GlobalPosition + Vector3.Up * (_height - 0.15f);
        float reach = zombie.HeadPoint.DistanceTo(eye);
        bool down = zombie.IsKnockedDown || zombie.IsCrawler;
        // A downed zombie: step up to its head and stomp (or swing down at it).
        if (down && reach > 1.3f)
            MoveTo(zombie.HeadPoint);
        else
            StopMoving();

        if (Weapons.MeleeState != MeleePhase.Ready || _meleeHoldLeft > 0f)
            return;
        if (reach <= melee.Data.Reach - 0.15f)
        {
            if (!down && _rng.Randf() < Type.HeavySwingChance)
                _meleeHoldLeft = melee.Data.WindupTime + melee.Data.ChargeTime * _rng.RandfRange(0.4f, 1f);
            else
                Weapons.PullTrigger();
            RerollWobble();
        }
    }

    // ---------- Body ----------

    private float MoveSpeed()
    {
        float speed = State switch
        {
            SurvivorState.Engage or SurvivorState.Retreat or SurvivorState.Push => Type.RunSpeed,
            SurvivorState.Flee => Type.RunSpeed * 1.1f,
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
        Vector3 look = _hasAimPoint && (State != SurvivorState.Idle || _watchingPlayer) ? _aimPoint - GlobalPosition : moving;
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
        if (_barkLeft < 1.9f) // Don't drown out what it just said ("Watch your fire!").
            Bark("I'm hit!");
        _memory.AllyHitTime = Now;
        if (State == SurvivorState.Push)
            AbortPush();
        if (State is SurvivorState.Idle or SurvivorState.Alert)
            BecomeAlert();
    }

    /// <summary>Bullets landing close by keep its head down; enemy gunfire tells it where the shooter is.</summary>
    private void OnNoise(NoiseEvent noise)
    {
        if (State == SurvivorState.Dead)
            return;
        if (noise.Kind == NoiseKind.Gunshot && noise.Source is PlayerController && AttitudeToPlayer == Attitude.Neutral
            && State is SurvivorState.Idle or SurvivorState.Alert && GlobalPosition.DistanceTo(noise.Position) < 15f)
        {
            Nervousness = Mathf.Min(1f, Nervousness + 0.35f); // Shooting right next to them.
            return;
        }
        if (noise.Kind == NoiseKind.Gunshot && noise.Source is ISenseTarget shooter && IsEnemy(shooter))
        {
            float range = GlobalPosition.DistanceTo(noise.Position);
            if (range > noise.Radius * Perception.HearingMultiplier)
                return;
            // Gunfire is easy to place roughly, not exactly (a few metres off at 30 m).
            float error = range * 0.1f * _rng.Randf();
            float angle = _rng.Randf() * Mathf.Tau;
            _enemyShotPosition = noise.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * error;
            _enemyShotPosition.Y = GlobalPosition.Y;
            _enemyShotTime = Now;
            _humanThreatTime = Now;
            _enemyShotNew = true;
            return;
        }
        if (noise.Kind == NoiseKind.Handling && noise.Source is ISenseTarget handler && IsEnemy(handler)
            && handler.Team == SenseTeam.Living && GlobalPosition.DistanceTo(noise.Position) <= noise.Radius * Perception.HearingMultiplier * 2f)
        {
            // Heard them reloading or dry-firing: a window to move.
            _memory.EnemyReloadTime = Now;
            Bark("He's reloading!");
            return;
        }
        if (noise.Kind != NoiseKind.Impact)
            return;
        if (AttitudeToPlayer == Attitude.Neutral && State is SurvivorState.Idle
            && (GlobalPosition + Vector3.Up * 1.2f).DistanceTo(noise.Position) < 3f)
            Nervousness = Mathf.Min(1f, Nervousness + 0.3f); // Bullets landing next to them, nobody else fighting.
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
        SurvivorGroup.ReleaseMover(GroupId, this);
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
            + $"target {target}  seen {(CanSeeTarget ? "yes" : "no")}  aim {_aimSettle:0.0}  supp {Suppression:0.0}{(_meleeMode ? $"  MELEE {Weapons.MeleeState}" : "")}\n"
            + $"{Faction?.DisplayName ?? "-"}: {AttitudeToPlayer}{(Nervousness > 0.01f ? $"  nervous {Nervousness:0.00}" : "")}\n"
            + $"mag {Weapons.Magazine?.Rounds ?? 0}{(Weapons.IsBusy ? " BUSY" : "")}  head {Mathf.Ceil(Health.Hp(BodyPart.Head))} chest {Mathf.Ceil(Health.Hp(BodyPart.Chest))}";
    }
}
