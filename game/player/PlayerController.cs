using Godot;
using Godot.Collections;
using ZombieSandbox.AI;
using ZombieSandbox.Character;
using ZombieSandbox.Combat;
using ZombieSandbox.Core;
using ZombieSandbox.Core.Input;
using ZombieSandbox.Core.Interaction;
using ZombieSandbox.Health;

namespace ZombieSandbox.Player;

public enum Stance
{
    Stand,
    Crouch,
    Prone,
}

/// <summary>
/// Player movement: momentum-based walking/running/sprinting, stand/crouch/prone, jump,
/// and leg stamina. All numbers come from <see cref="MovementConfig"/> and <see cref="CharacterStats"/>.
///
/// Input is turned into intent (wanted stance, sprint request, jump request) in _UnhandledInput,
/// and intent is applied in _PhysicsProcess.
/// </summary>
public partial class PlayerController : CharacterBody3D, IPersistent, ISenseTarget, IAttackable
{
    public const string Group = "player";

    [Export] public MovementConfig Config { get; set; } = null!;
    [Export] public string PersistentId { get; set; } = "player";

    public CharacterSkills Skills { get; private set; } = null!;
    public CharacterStats Stats { get; private set; } = null!;
    public Stamina Stamina { get; private set; } = null!;
    public CameraRig CameraRig { get; private set; } = null!;
    public WeaponHolder Weapons { get; private set; } = null!;
    public MagazinePouch Pouch { get; private set; } = null!;
    public BodyHealth Health { get; private set; } = null!;
    public Infection Infection { get; private set; } = null!;
    Infection? IAttackable.Infection => Infection;
    /// <summary>Zombies currently holding on to the player.</summary>
    public int GrabbedByCount => _grabbedBy.Count;
    private readonly System.Collections.Generic.HashSet<Node3D> _grabbedBy = new();
    private float _shoveCooldown;
    private AudioStreamPlayer _shoveAudio = null!;
    public HitboxRig Hitboxes { get; private set; } = null!;
    /// <summary>The player's own body and hitboxes: own bullets and aiming rays ignore them.</summary>
    public Array<Rid> SelfExclude { get; } = new();
    public bool IsDead => Health.IsDead;
    /// <summary>The health screen (body diagram) is open.</summary>
    public bool HealthScreenOpen { get; private set; }
    /// <summary>The usable thing the player is looking at, within reach (null if none).</summary>
    public IInteractable? InteractTarget { get; private set; }

    /// <summary>The stance the body is in (or moving into).</summary>
    public Stance Stance { get; private set; } = Stance.Stand;
    /// <summary>The stance the player asked for. Can differ from Stance when something overhead blocks standing up.</summary>
    public Stance WantedStance { get; private set; } = Stance.Stand;
    public bool IsSprinting { get; private set; }
    /// <summary>The player is asking to aim down sights (the weapon may still be coming up).</summary>
    public bool WantsAim => _aimHeld || _aimToggled;
    /// <summary>Crouched and risen up to aim over low cover.</summary>
    public bool IsPeeking { get; private set; }
    /// <summary>-1 = full left, 0 = upright, 1 = full right. Smoothed and limited by nearby walls.</summary>
    public float Lean { get; private set; }
    public float HorizontalSpeed => new Vector2(Velocity.X, Velocity.Z).Length();
    public float CurrentHeight { get; private set; }
    /// <summary>0..1. Builds up with quick repeated jumps; makes jumps lower and landings harsher.</summary>
    public float JumpFatigue { get; private set; }
    public float EyeHeight => CurrentHeight - Config.EyeOffsetFromTop;

    private CollisionShape3D _collider = null!;
    private CylinderShape3D _bodyShape = null!;
    private Node3D _body = null!;
    private float _gravity;
    private bool _wasOnFloor = true;
    private bool _inJump;
    private float _airPeakY;
    private float _strideDistance;
    private float _jumpBufferLeft;
    private float _landingRecoveryLeft;

    // Input intent.
    private bool _sprintHeld;
    private bool _sprintToggled;
    private bool _triggerHeld;
    private ulong _lastReloadPressMs;
    private bool _padReloadHeld;
    private bool _jamButtonHeld;
    private float _jamButtonHeldTime;
    private bool _checkButtonHeld;
    private float _checkButtonHeldTime;
    private const float HoldSeconds = 0.35f;
    private float _padReloadHeldTime;
    private bool _padFastReloadSent;
    private readonly PhysicsRayQueryParameters3D _interactRay = new();
    private const float InteractReach = 2.2f;
    /// <summary>Small items count as targeted within this angle of the view center (no crosshair to aim with).</summary>
    private const float InteractConeDegrees = 10f;
    private readonly PhysicsShapeQueryParameters3D _nearbyItemsQuery = new();
    private const uint ItemsLayerMask = 1u << 2;
    private const int StartingSpareMagazines = 4;
    private bool _aimInputDown; // Physical state of the aim button/trigger (edge detection).
    private bool _aimHeld;
    private bool _aimToggled;
    private bool _breathHeld;
    private bool _breathToggled;
    private int _autoLeanSide;
    private bool _leanLeftHeld;
    private bool _leanRightHeld;
    private int _leanToggled; // -1, 0, 1
    private int _lastLeanPressed;
    private bool _padCrouchPending; // Gamepad B pressed; tap = crouch, hold = prone.
    private float _padCrouchHeldTime;

    // Overhead clearance check, reused to avoid per-frame allocations.
    private CylinderShape3D _clearanceShape = null!;
    private PhysicsShapeQueryParameters3D _clearanceQuery = null!;

    // Step-up tests, reused to avoid per-frame allocations.
    private SphereShape3D _leanHeadShape = null!;
    private PhysicsShapeQueryParameters3D _leanQuery = null!;

    private readonly PhysicsRayQueryParameters3D _coverRay = new();

    private readonly PhysicsTestMotionParameters3D _stepParams = new();
    private readonly PhysicsTestMotionResult3D _stepResult = new();

    public override void _Ready()
    {
        AddToGroup(Group);

        _collider = GetNode<CollisionShape3D>("CollisionShape3D");
        _bodyShape = (CylinderShape3D)_collider.Shape;
        _body = GetNode<Node3D>("Body");
        CameraRig = GetNode<CameraRig>("CameraRig");
        Skills = GetNode<CharacterSkills>("Skills");
        Stats = GetNode<CharacterStats>("Stats");
        Stamina = GetNode<Stamina>("Stamina");
        Weapons = GetNode<WeaponHolder>("WeaponHolder");
        Pouch = GetNode<MagazinePouch>("MagazinePouch");
        Health = GetNode<BodyHealth>("Health");
        Infection = GetNode<Infection>("Infection");
        _shoveAudio = new AudioStreamPlayer { Stream = ZombieSandbox.Core.Audio.PlaceholderSounds.ShoveThud };
        AddChild(_shoveAudio);
        Hitboxes = GetNode<HitboxRig>("Hitboxes");

        SelfExclude.Add(GetRid());
        Hitboxes.AddRidsTo(SelfExclude);

        Stats.Initialize(Skills);
        Stamina.Initialize(Stats, Skills);
        Health.Initialize(Skills);
        // Bullets hit the world and body hit zones, never the invisible movement colliders around characters.
        Weapons.Initialize(Stats, Skills, Pouch, SelfExclude, worldMask: CollisionMask, bulletMask: 1u | HitboxRig.HitboxLayer);
        Weapons.GiveSpareMagazines(StartingSpareMagazines);

        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle() * Config.GravityMultiplier;

        _clearanceShape = new CylinderShape3D { Radius = Config.Radius - 0.05f };
        _clearanceQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = _clearanceShape,
            CollisionMask = CollisionMask,
            Exclude = new Array<Rid> { GetRid() },
        };

        _leanHeadShape = new SphereShape3D { Radius = Config.LeanHeadRadius };
        _leanQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = _leanHeadShape,
            CollisionMask = CollisionMask,
            Exclude = new Array<Rid> { GetRid() },
        };

        _coverRay.CollisionMask = CollisionMask;
        _interactRay.CollisionMask = CollisionMask | ItemsLayerMask;
        _interactRay.Exclude = new Array<Rid> { GetRid() };
        _nearbyItemsQuery.Shape = new SphereShape3D { Radius = InteractReach };
        _nearbyItemsQuery.CollisionMask = ItemsLayerMask;
        _coverRay.Exclude = new Array<Rid> { GetRid() };

        FloorSnapLength = Config.MaxStepHeight; // Stay on the ground walking down stairs.

        _bodyShape.Radius = Config.Radius;
        CurrentHeight = Config.StandHeight;
        ApplyHeight();
    }

    // ---------- What AI can see and hear ----------

    /// <summary>The head, including lean: peeking around a corner shows your head.</summary>
    public Vector3 SightPoint => GlobalPosition + Vector3.Up * Mathf.Max(EyeHeight - 0.2f, 0.25f) + GlobalBasis.X * (Lean * Config.LeanOffset);
    public Vector3 GroundPosition => GlobalPosition;
    public Array<Rid> SightExclude => SelfExclude;
    public bool IsAlive => !IsDead;
    public SenseTeam Team => SenseTeam.Living;

    /// <summary>How easy the player is to spot: stance and movement.</summary>
    public float Visibility
    {
        get
        {
            float stance = Stance switch { Stance.Crouch => 0.6f, Stance.Prone => 0.35f, _ => 1f };
            float movement = IsSprinting ? 1.15f : HorizontalSpeed > Config.WalkSpeed * 1.1f ? 1f : HorizontalSpeed > 0.3f ? 0.9f : 0.75f;
            return stance * movement;
        }
    }

    public override void _EnterTree() => SenseTargets.All.Add(this);

    public override void _ExitTree() => SenseTargets.All.Remove(this);

    /// <summary>Footsteps make noise each stride; louder the faster you move, quieter crouched/prone.</summary>
    private void EmitFootsteps(float dt)
    {
        if (!IsOnFloor() || HorizontalSpeed < 0.3f)
            return;
        _strideDistance += HorizontalSpeed * dt;
        float stride = IsSprinting ? 1.0f : 0.75f;
        if (_strideDistance < stride)
            return;
        _strideDistance = 0f;
        float loudness = Stance switch
        {
            Stance.Prone => GameNoise.Prone,
            Stance.Crouch => GameNoise.CrouchWalking,
            _ when IsSprinting => GameNoise.Sprinting,
            _ when HorizontalSpeed > Config.WalkSpeed * 1.1f => GameNoise.Running,
            _ => GameNoise.Walking,
        };
        GameNoise.Emit(GlobalPosition, loudness, NoiseKind.Footstep, this);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho())
            return;

        InputSettings settings = GameInput.Instance.Settings;
        InputDevice device = GameInput.DeviceOf(e);

        if (HandleHealthDebugInput(e))
            return;
        if (IsDead)
            return; // The death screen handles restarting.

        if (e.IsActionPressed(InputActions.Shove))
        {
            TryShove();
            return;
        }

        if (e.IsActionPressed(InputActions.HealthScreen))
        {
            HealthScreenOpen = !HealthScreenOpen;
            return;
        }

        // Gamepad X is both interact and reload: interacting wins when looking at something usable.
        if (e.IsActionPressed(InputActions.Interact, exactMatch: true) && InteractTarget != null)
        {
            InteractTarget.Interact(this);
            return;
        }

        if (e.IsAction(InputActions.Reload, exactMatch: true))
        {
            HandleReloadInput(e, device);
            return;
        }

        if (e.IsAction(InputActions.CheckMagazine, exactMatch: true))
        {
            // Tap = check the magazine, hold = inspect the weapon (decided in UpdateHoldButtons).
            if (e.IsEcho())
                return;
            if (e.IsPressed())
            {
                _checkButtonHeld = true;
                _checkButtonHeldTime = 0f;
                StopSprintForHands();
            }
            else if (_checkButtonHeld)
            {
                _checkButtonHeld = false;
                Weapons.RequestMagazineCheck();
            }
            return;
        }

        if (e.IsActionPressed(InputActions.Jump))
        {
            // From crouch/prone, jump stands you up instead.
            if (WantedStance != Stance.Stand)
                WantedStance = Stance.Stand;
            else
                _jumpBufferLeft = Config.JumpBufferTime;
        }
        else if (e.IsAction(InputActions.Fire))
        {
            bool wasHeld = _triggerHeld;
            _triggerHeld = ReadPressed(e, wasHeld);
            if (_triggerHeld && !wasHeld)
            {
                Weapons.PullTrigger();
                // Pulling the trigger ends a sprint; press sprint again to resume.
                _sprintToggled = false;
                _sprintHeld = false;
            }
        }
        else if (e.IsActionPressed(InputActions.CycleFireMode))
        {
            Weapons.CycleFireMode();
        }
        else if (e.IsAction(InputActions.Aim))
        {
            HandleAimInput(e, settings.AimMode(device));
        }
        else if (e.IsAction(InputActions.Sprint))
        {
            // While aiming, the sprint button holds your breath instead.
            if (WantsAim && e.IsPressed())
                HandleHoldBreathInput(device);
            else
                HandleSprintInput(e, settings.SprintMode(device));
        }
        else if (e.IsAction(InputActions.Crouch))
        {
            PressMode mode = settings.CrouchMode(device);
            if (device == InputDevice.Gamepad && mode == PressMode.Toggle)
                HandlePadCrouchTapHold(e);
            else
                HandleStanceInput(e, Stance.Crouch, mode);
        }
        else if (e.IsAction(InputActions.Prone))
        {
            HandleStanceInput(e, Stance.Prone, settings.ProneMode(device));
        }
        else if (e.IsAction(InputActions.LeanLeft))
        {
            HandleLeanInput(e, -1, settings.LeanKeyboard);
        }
        else if (e.IsAction(InputActions.LeanRight))
        {
            HandleLeanInput(e, 1, settings.LeanKeyboard);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugFitnessUp))
        {
            Skills.SetLevel(SkillId.Fitness, Skills.GetLevel(SkillId.Fitness) + 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugFitnessDown))
        {
            Skills.SetLevel(SkillId.Fitness, Skills.GetLevel(SkillId.Fitness) - 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugStrengthUp))
        {
            Skills.SetLevel(SkillId.Strength, Skills.GetLevel(SkillId.Strength) + 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugStrengthDown))
        {
            Skills.SetLevel(SkillId.Strength, Skills.GetLevel(SkillId.Strength) - 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugMaintenanceUp))
        {
            Skills.SetLevel(SkillId.Maintenance, Skills.GetLevel(SkillId.Maintenance) + 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugMaintenanceDown))
        {
            Skills.SetLevel(SkillId.Maintenance, Skills.GetLevel(SkillId.Maintenance) - 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugCycleCondition))
        {
            Weapons.DebugCycleCondition();
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugForceJam))
        {
            Weapons.DebugForceJam();
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugRefillAmmo))
        {
            Weapons.DebugRefill(StartingSpareMagazines);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugReloadingUp))
        {
            Skills.SetLevel(SkillId.Reloading, Skills.GetLevel(SkillId.Reloading) + 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugReloadingDown))
        {
            Skills.SetLevel(SkillId.Reloading, Skills.GetLevel(SkillId.Reloading) - 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugAimingUp))
        {
            Skills.SetLevel(SkillId.Aiming, Skills.GetLevel(SkillId.Aiming) + 1);
        }
        else if (OS.IsDebugBuild() && e.IsActionPressed(InputActions.DebugAimingDown))
        {
            Skills.SetLevel(SkillId.Aiming, Skills.GetLevel(SkillId.Aiming) - 1);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Body always faces where the camera looks.
        Rotation = new Vector3(0f, CameraRig.Yaw, 0f);

        UpdatePadCrouchHold(dt);
        UpdatePadReloadHold(dt);
        UpdateHoldButtons(dt);
        _shoveCooldown = Mathf.Max(0f, _shoveCooldown - dt);
        if (IsDead)
            _grabbedBy.Clear();
        UpdateInteractTarget();
        UpdateStance(dt);

        Vector2 input = IsDead
            ? Vector2.Zero
            : Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);
        if (IsDead)
        {
            WantedStance = Stance.Prone;
            _sprintHeld = _sprintToggled = _triggerHeld = _aimHeld = _aimToggled = false;
        }
        UpdateSprint(input);
        UpdateLean(dt);

        Vector3 velocity = Velocity;
        bool onFloor = IsOnFloor();

        if (!onFloor)
            velocity.Y -= _gravity * dt;

        // Landing from a jump costs momentum; more when jump-fatigued (spamming).
        if (onFloor && !_wasOnFloor && _inJump)
        {
            _inJump = false;
            float keep = Mathf.Lerp(Config.LandingSpeedKeptRested, Config.LandingSpeedKeptFatigued, JumpFatigue);
            velocity.X *= keep;
            velocity.Z *= keep;
            _landingRecoveryLeft = Mathf.Lerp(Config.LandingRecoveryRested, Config.LandingRecoveryFatigued, JumpFatigue);
        }
        // Fall damage: the drop from the highest point reached while airborne.
        if (!onFloor)
            _airPeakY = Mathf.Max(_airPeakY, GlobalPosition.Y);
        else
        {
            if (!_wasOnFloor)
            {
                float drop = _airPeakY - GlobalPosition.Y;
                Health.ApplyFall(drop);
                if (drop > 0.4f)
                    GameNoise.Emit(GlobalPosition, GameNoise.JumpLanding, NoiseKind.Landing, this);
            }
            _airPeakY = GlobalPosition.Y;
        }
        _wasOnFloor = onFloor;

        if (onFloor)
        {
            JumpFatigue = Mathf.Max(JumpFatigue - Config.JumpFatigueRecoveryPerSecond * dt, 0f);
            _landingRecoveryLeft = Mathf.Max(_landingRecoveryLeft - dt, 0f);
        }

        // Jumping always works on the ground; stamina and fatigue only change how high.
        _jumpBufferLeft = Mathf.Max(_jumpBufferLeft - dt, 0f);
        if (_jumpBufferLeft > 0f && onFloor && Stance == Stance.Stand)
        {
            _jumpBufferLeft = 0f;
            float cost = Stamina.Config.JumpCost;
            float staminaFactor = Mathf.Lerp(Config.JumpHeightWithoutStamina, 1f, Stamina.SpendAvailable(cost) / cost);
            float fatigueFactor = Mathf.Lerp(1f, Config.JumpHeightAtFullFatigue, JumpFatigue);
            velocity.Y = Mathf.Sqrt(2f * _gravity * Config.JumpHeight * staminaFactor * fatigueFactor);
            JumpFatigue = Mathf.Min(JumpFatigue + Config.JumpFatiguePerJump, 1f);
            _inJump = true;
        }

        // Momentum: horizontal velocity moves toward the wanted velocity at a limited rate.
        Vector3 wishDirection = GlobalBasis * new Vector3(input.X, 0f, input.Y);
        wishDirection.Y = 0f;
        wishDirection = wishDirection.LengthSquared() > 0.0001f ? wishDirection.Normalized() : Vector3.Zero;
        Vector3 targetVelocity = wishDirection * TargetSpeed(input);

        float rate = targetVelocity == Vector3.Zero
            ? Config.Deceleration
            : (IsSprinting ? Config.SprintAcceleration : Config.Acceleration);
        rate *= Stats.AccelerationMultiplier;
        if (_landingRecoveryLeft > 0f)
            rate *= Config.LandingRecoveryAcceleration;
        if (!onFloor)
            rate *= Config.AirControl;

        Vector3 horizontal = new Vector3(velocity.X, 0f, velocity.Z).MoveToward(targetVelocity, rate * dt);
        velocity.X = horizontal.X;
        velocity.Z = horizontal.Z;

        Velocity = velocity;
        if (!TryStepUp(targetVelocity, dt))
            MoveAndSlide();

        if (IsSprinting && IsOnFloor() && HorizontalSpeed > Config.WalkSpeed)
            Stamina.Drain(Stamina.Config.SprintDrainPerSecond * dt);
        Stamina.Tick(dt);
        EmitFootsteps(dt);

        UpdateWeaponIntent();
    }

    private void UpdateWeaponIntent()
    {
        if (!WantsAim || Weapons.IsGasping)
            _breathToggled = false;
        if (!WantsAim)
            _breathHeld = false;

        Weapons.WantsAim = WantsAim;
        Weapons.InjurySwayMultiplier = Health.SwayMultiplier * Infection.SwayMultiplier;
        Stamina.MaxMultiplier = Infection.StaminaMultiplier;
        Weapons.TriggerHeld = _triggerHeld;
        Weapons.WantsHoldBreath = _breathHeld || _breathToggled;
        Weapons.Lowered = IsSprinting;
        Weapons.MoveFraction = Mathf.Clamp(HorizontalSpeed / Config.RunSpeed, 0f, 1f);
        Weapons.Breathlessness = 1f - Stamina.Fraction;
        Weapons.RecoilStanceMultiplier = Stance switch
        {
            Stance.Crouch => Weapons.Config.CrouchRecoilMultiplier,
            Stance.Prone => Weapons.Config.ProneRecoilMultiplier,
            _ => 1f,
        };
        Weapons.StanceSwayMultiplier = Stance switch
        {
            Stance.Crouch => Weapons.Config.CrouchSwayMultiplier,
            Stance.Prone => Weapons.Config.ProneSwayMultiplier,
            _ => 1f,
        };
    }

    // ---------- Input intent ----------

    private void HandleSprintInput(InputEvent e, PressMode mode)
    {
        if (e.IsPressed())
        {
            // Sprinting from crouch/prone stands you up first.
            WantedStance = Stance.Stand;
            if (mode == PressMode.Hold)
                _sprintHeld = true;
            else
                _sprintToggled = !_sprintToggled;
        }
        else
        {
            // The button may have been pressed as sprint and released as hold-breath (or vice versa).
            if (mode == PressMode.Hold)
                _sprintHeld = false;
            _breathHeld = false;
        }
    }

    /// <summary>
    /// Pressed state for buttons and analog triggers. A trigger sends a stream of position events,
    /// so callers must only react when this changes (an edge), never to every event. Hysteresis
    /// (press past 55%, release below 30%) stops a half-squeezed trigger from flickering.
    /// </summary>
    private static bool ReadPressed(InputEvent e, bool wasPressed)
    {
        if (e is InputEventJoypadMotion motion)
        {
            float amount = Mathf.Abs(motion.AxisValue);
            return wasPressed ? amount > 0.3f : amount > 0.55f;
        }
        return e.IsPressed();
    }

    // ---------- Reloading & interaction ----------

    /// <summary>Hands are needed (reload, magazine check): a sprint ends, like pulling the trigger.</summary>
    private void StopSprintForHands()
    {
        _sprintToggled = false;
        _sprintHeld = false;
    }

    private void HandleReloadInput(InputEvent e, InputDevice device)
    {
        // Jammed: the reload button deals with the jam instead. Tap = tap-rack, hold = inspect & clear.
        if (Weapons.IsJammed || _jamButtonHeld)
        {
            if (e.IsEcho())
                return;
            if (e.IsPressed())
            {
                _jamButtonHeld = true;
                _jamButtonHeldTime = 0f;
                StopSprintForHands();
            }
            else if (_jamButtonHeld)
            {
                _jamButtonHeld = false;
                Weapons.RequestClearMalfunction(inspectFirst: false);
            }
            return;
        }

        if (device == InputDevice.Gamepad)
        {
            // Tap = tactical. Holding past 0.3 s turns it into a fast reload (see UpdatePadReloadHold).
            _padReloadHeld = e.IsPressed();
            if (!e.IsPressed())
                return;
            _padReloadHeldTime = 0f;
            _padFastReloadSent = false;
            StopSprintForHands();
            Weapons.RequestReload(fast: false);
            return;
        }

        if (!e.IsPressed() || e.IsEcho())
            return;
        // Keyboard: a second press within 0.3 s upgrades the reload to a fast one (old mag dropped).
        ulong now = Time.GetTicksMsec();
        bool doubleTap = now - _lastReloadPressMs < 300;
        _lastReloadPressMs = now;
        StopSprintForHands();
        Weapons.RequestReload(fast: doubleTap);
    }

    private void UpdateHoldButtons(float dt)
    {
        if (_jamButtonHeld)
        {
            _jamButtonHeldTime += dt;
            if (_jamButtonHeldTime >= HoldSeconds)
            {
                _jamButtonHeld = false;
                Weapons.RequestClearMalfunction(inspectFirst: true);
            }
        }

        if (_checkButtonHeld)
        {
            _checkButtonHeldTime += dt;
            if (_checkButtonHeldTime >= HoldSeconds)
            {
                _checkButtonHeld = false;
                Weapons.RequestInspect();
            }
        }
    }

    private void UpdatePadReloadHold(float dt)
    {
        if (!_padReloadHeld || _padFastReloadSent)
            return;
        _padReloadHeldTime += dt;
        if (_padReloadHeldTime >= 0.3f)
        {
            _padFastReloadSent = true;
            Weapons.RequestReload(fast: true);
        }
    }

    /// <summary>
    /// Find the usable thing the player is looking at, within arm's reach of the body.
    /// 1. Whatever is exactly at the center of the view.
    /// 2. Otherwise the small item closest to the view center within a narrow cone, with nothing
    ///    in between: items on the floor are tiny and there is no crosshair to aim with.
    /// </summary>
    private void UpdateInteractTarget()
    {
        InteractTarget = null;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Transform3D view = CameraRig.Camera.GlobalTransform;
        Vector3 forward = -view.Basis.Z.Normalized();
        Vector3 eye = GlobalPosition + Vector3.Up * EyeHeight;

        _interactRay.From = view.Origin;
        _interactRay.To = view.Origin + forward * (view.Origin.DistanceTo(eye) + InteractReach);
        Dictionary hit = space.IntersectRay(_interactRay);
        if (hit.Count > 0 && hit["position"].AsVector3().DistanceTo(eye) <= InteractReach
            && hit["collider"].AsGodotObject() is IInteractable direct && direct.CanInteract(this))
        {
            InteractTarget = direct;
            return;
        }

        _nearbyItemsQuery.Transform = new Transform3D(Basis.Identity, eye);
        float bestAngle = Mathf.DegToRad(InteractConeDegrees);
        foreach (Dictionary result in space.IntersectShape(_nearbyItemsQuery, 16))
        {
            if (result["collider"].AsGodotObject() is not Node3D node || node is not IInteractable candidate || !candidate.CanInteract(this))
                continue;
            float angle = forward.AngleTo(node.GlobalPosition - view.Origin);
            if (angle >= bestAngle)
                continue;

            // Line of sight from the eye: the first thing hit must be the item itself.
            _interactRay.From = eye;
            _interactRay.To = node.GlobalPosition;
            Dictionary sight = space.IntersectRay(_interactRay);
            if (sight.Count > 0 && sight["collider"].AsGodotObject() != node)
                continue;

            bestAngle = angle;
            InteractTarget = candidate;
        }
    }

    private void HandleAimInput(InputEvent e, PressMode mode)
    {
        bool wasDown = _aimInputDown;
        _aimInputDown = ReadPressed(e, wasDown);
        if (_aimInputDown == wasDown)
            return; // Only react to presses and releases, not every trigger movement.

        if (_aimInputDown)
        {
            _sprintToggled = false; // Aiming stops a sprint.
            if (mode == PressMode.Hold)
                _aimHeld = true;
            else
                _aimToggled = !_aimToggled;
        }
        else if (mode == PressMode.Hold)
        {
            _aimHeld = false;
        }
    }

    /// <summary>Keyboard: hold Shift while aiming. Gamepad: click L3 while aiming (click again to let go).</summary>
    private void HandleHoldBreathInput(InputDevice device)
    {
        if (device == InputDevice.Gamepad)
            _breathToggled = !_breathToggled;
        else
            _breathHeld = true;
    }

    private void HandleStanceInput(InputEvent e, Stance stance, PressMode mode)
    {
        if (mode == PressMode.Toggle)
        {
            if (e.IsPressed())
                ToggleWantedStance(stance);
        }
        else if (e.IsPressed())
        {
            WantedStance = stance;
        }
        else if (WantedStance == stance)
        {
            WantedStance = Stance.Stand;
        }
    }

    private void HandlePadCrouchTapHold(InputEvent e)
    {
        if (e.IsPressed())
        {
            _padCrouchPending = true;
            _padCrouchHeldTime = 0f;
        }
        else if (_padCrouchPending)
        {
            // Released before the hold time: it was a tap.
            _padCrouchPending = false;
            ToggleWantedStance(Stance.Crouch);
        }
    }

    private void UpdatePadCrouchHold(float dt)
    {
        if (!_padCrouchPending)
            return;

        _padCrouchHeldTime += dt;
        if (_padCrouchHeldTime >= GameInput.Instance.Settings.CrouchHoldForProneSeconds)
        {
            _padCrouchPending = false;
            ToggleWantedStance(Stance.Prone);
        }
    }

    private void ToggleWantedStance(Stance stance) =>
        WantedStance = WantedStance == stance ? Stance.Stand : stance;

    // ---------- Movement rules ----------

    private void UpdateSprint(Vector2 input)
    {
        float forward = -input.Y;
        bool sideways = Mathf.Abs(input.X) > forward; // Sprint is forward-only (within 45°).

        if (_sprintToggled && (forward < Config.SprintCancelForward || Stamina.IsExhausted))
            _sprintToggled = false;

        bool wantsSprint = (_sprintHeld || _sprintToggled) && !WantsAim && !_triggerHeld && Health.CanSprint && _grabbedBy.Count == 0;
        IsSprinting = wantsSprint
            && Stance == Stance.Stand
            && !Stamina.IsExhausted
            && forward >= (IsSprinting ? Config.SprintCancelForward : Config.SprintStartForward)
            && !sideways;
    }

    private float TargetSpeed(Vector2 input)
    {
        float tilt = Mathf.Min(input.Length(), 1f);
        if (tilt < 0.01f)
            return 0f;

        if (IsSprinting)
            return Config.SprintSpeed * Stats.SprintSpeedMultiplier;

        float stanceSpeed = Stance switch
        {
            Stance.Crouch => Config.CrouchSpeed,
            Stance.Prone => Config.ProneSpeed,
            _ => Config.RunSpeed,
        };

        // Walking is a fraction of the stance's full speed, so it also slows crouch/prone.
        float walkFraction = Config.WalkSpeed / Config.RunSpeed;
        float speedFraction;
        if (GameInput.Instance.LastDevice == InputDevice.Gamepad)
        {
            // Light tilt ramps up to walking speed, the rest of the tilt ramps up to running.
            float walkTilt = Config.GamepadWalkTilt;
            speedFraction = tilt <= walkTilt
                ? tilt / walkTilt * walkFraction
                : Mathf.Lerp(walkFraction, 1f, (tilt - walkTilt) / (1f - walkTilt));
        }
        else
        {
            speedFraction = Input.IsActionPressed(InputActions.Walk) ? walkFraction : 1f;
        }

        // You can only walk while leaning or aiming.
        if (Mathf.Abs(Lean) > 0.1f || WantsAim)
            speedFraction = Mathf.Min(speedFraction, walkFraction);

        // Held by a zombie: barely able to move (≈0.7 m/s), less with more of them. Shove them off.
        float grabbed = _grabbedBy.Count == 0 ? 1f : Mathf.Max(0.08f, 0.2f - 0.06f * (_grabbedBy.Count - 1));
        return stanceSpeed * speedFraction * Health.SpeedMultiplier * grabbed;
    }

    // ---------- Lean ----------

    private void HandleLeanInput(InputEvent e, int side, PressMode mode)
    {
        if (mode == PressMode.Toggle)
        {
            if (e.IsPressed())
                _leanToggled = _leanToggled == side ? 0 : side;
            return;
        }

        if (side < 0)
            _leanLeftHeld = e.IsPressed();
        else
            _leanRightHeld = e.IsPressed();
        if (e.IsPressed())
            _lastLeanPressed = side;
    }

    private int WantedLeanSide()
    {
        int manual = ManualLeanSide();
        return manual != 0 ? manual : _autoLeanSide;
    }

    private int ManualLeanSide()
    {
        if (_leanToggled != 0)
            return _leanToggled;
        if (_leanLeftHeld && _leanRightHeld)
            return _lastLeanPressed; // Both held: the most recent press wins.
        return _leanLeftHeld ? -1 : (_leanRightHeld ? 1 : 0);
    }

    private void UpdateLean(float dt)
    {
        bool canLean = !IsSprinting && Stance != Stance.Prone;
        if (!canLean)
            _leanToggled = 0;

        _autoLeanSide = canLean ? AutoLeanSide() : 0;
        int side = canLean ? WantedLeanSide() : 0;
        float target = side == 0 ? 0f : side * LeanRoom(side);
        Lean = Mathf.MoveToward(Lean, target, Config.LeanSpeed * dt);

        // A wall may have moved closer (or we walked up to one): never stay leaned into it.
        if (side != 0 && Mathf.Abs(Lean) > Mathf.Abs(target))
            Lean = target;
    }

    /// <summary>
    /// Gamepad auto-lean: while aiming with cover right in front of you, lean toward the side
    /// that has a clear view past it (CoD-style corner peeking).
    /// </summary>
    private int AutoLeanSide()
    {
        InputSettings settings = GameInput.Instance.Settings;
        if (!settings.GamepadAutoLean || GameInput.Instance.LastDevice != InputDevice.Gamepad || !WantsAim)
            return 0;

        Vector3 eye = GlobalPosition + Vector3.Up * EyeHeight;
        Vector3 ahead = -GlobalBasis.Z * Config.CoverCheckDistance;
        if (!RayHits(eye, eye + ahead))
            return 0; // Nothing in front to lean around.

        for (int side = 1; side >= -1; side -= 2)
        {
            Vector3 leaned = eye + GlobalBasis.X * (side * Config.LeanOffset);
            if (!RayHits(eye, leaned) && !RayHits(leaned, leaned + ahead))
                return side; // Right is checked first, so it wins when both sides are open.
        }
        return 0;
    }

    /// <summary>Crouched and aiming with low cover in front that you could see over if you rose up.</summary>
    private bool WantsToPeekOverCover()
    {
        if (!GameInput.Instance.Settings.PeekOverCover || !WantsAim || WantedStance != Stance.Crouch)
            return false;

        Vector3 ahead = -GlobalBasis.Z * Config.CoverCheckDistance;
        Vector3 crouchEye = GlobalPosition + Vector3.Up * (Config.CrouchHeight - Config.EyeOffsetFromTop);
        Vector3 peekEye = GlobalPosition + Vector3.Up * (Config.PeekHeight - Config.EyeOffsetFromTop);
        return RayHits(crouchEye, crouchEye + ahead) && !RayHits(peekEye, peekEye + ahead);
    }

    private bool RayHits(Vector3 from, Vector3 to)
    {
        _coverRay.From = from;
        _coverRay.To = to;
        return GetWorld3D().DirectSpaceState.IntersectRay(_coverRay).Count > 0;
    }

    /// <summary>Fraction (0..1) of a full lean that fits before the head would hit something.</summary>
    private float LeanRoom(int side)
    {
        Vector3 eye = GlobalPosition + Vector3.Up * EyeHeight;
        _leanQuery.Transform = new Transform3D(Basis.Identity, eye);
        _leanQuery.Motion = GlobalBasis.X * (side * Config.LeanOffset);
        float[] fractions = GetWorld3D().DirectSpaceState.CastMotion(_leanQuery);
        return fractions.Length > 0 ? fractions[0] : 1f;
    }

    // ---------- Steps ----------

    /// <summary>
    /// If walking into a low ledge (curb, stair), lift the body onto it: test up, forward, then down.
    /// Physics interpolation smooths the small vertical pop.
    /// </summary>
    private bool TryStepUp(Vector3 wishVelocity, float dt)
    {
        if (!IsOnFloor() || Velocity.Y > 0f)
            return false;

        // Probe with the velocity the player wants, not the current one: a ledge that already
        // stopped us has zeroed our velocity, which would make the probe too short to clear the edge.
        Vector3 motion = wishVelocity * dt;
        if (motion.LengthSquared() < 1e-6f)
            return false;

        Transform3D start = GlobalTransform;
        if (!TestBodyMotion(start, motion))
            return false; // Nothing in the way.
        if (_stepResult.GetCollisionNormal().AngleTo(Vector3.Up) <= FloorMaxAngle)
            return false; // It's a slope MoveAndSlide can handle.

        // Up.
        TestBodyMotion(start, Vector3.Up * Config.MaxStepHeight);
        Transform3D raised = start.Translated(_stepResult.GetTravel());

        // Forward.
        TestBodyMotion(raised, motion);
        Vector3 forwardTravel = _stepResult.GetTravel();
        if (forwardTravel.LengthSquared() < motion.LengthSquared() * 0.25f)
            return false; // Still blocked: it's a wall, not a step.
        Transform3D advanced = raised.Translated(forwardTravel);

        // Down onto the step.
        if (!TestBodyMotion(advanced, Vector3.Down * (Config.MaxStepHeight + 0.05f)))
            return false; // No ground there.
        if (_stepResult.GetCollisionNormal().AngleTo(Vector3.Up) > FloorMaxAngle)
            return false; // Landing surface too steep.

        Vector3 landed = advanced.Origin + _stepResult.GetTravel();
        if (landed.Y - start.Origin.Y < 0.01f)
            return false;

        GlobalPosition = landed;
        // The riser we just stepped over may have bled off speed; carry the intended speed onto the step.
        Velocity = new Vector3(wishVelocity.X, 0f, wishVelocity.Z);
        return true;
    }

    /// <summary>Returns true if the body would hit something. Details go to _stepResult.</summary>
    private bool TestBodyMotion(Transform3D from, Vector3 motion)
    {
        _stepParams.From = from;
        _stepParams.Motion = motion;
        return PhysicsServer3D.BodyTestMotion(GetRid(), _stepParams, _stepResult);
    }

    // ---------- Stance ----------

    private float HeightFor(Stance stance) => stance switch
    {
        Stance.Crouch => Config.CrouchHeight,
        Stance.Prone => Config.ProneHeight,
        _ => Config.StandHeight,
    };

    private void UpdateStance(float dt)
    {
        Stance next = WantedStance;

        // Getting taller needs room overhead. If blocked, fall back to the tallest stance that fits.
        if (HeightFor(next) > CurrentHeight + 0.01f && !HasClearance(HeightFor(next)))
        {
            bool crouchFits = Config.CrouchHeight <= CurrentHeight + 0.01f || HasClearance(Config.CrouchHeight);
            next = next == Stance.Stand && crouchFits ? Stance.Crouch : Stance.Prone;
        }
        Stance = next;

        float target = HeightFor(Stance);
        IsPeeking = Stance == Stance.Crouch && WantsToPeekOverCover()
            && (CurrentHeight >= Config.PeekHeight - 0.01f || HasClearance(Config.PeekHeight));
        if (IsPeeking)
            target = Config.PeekHeight;
        bool involvesProne = Stance == Stance.Prone || CurrentHeight < Config.CrouchHeight - 0.01f;
        float speed = involvesProne ? Config.ProneTransitionSpeed : Config.CrouchTransitionSpeed;
        CurrentHeight = Mathf.MoveToward(CurrentHeight, target, speed * dt);
        ApplyHeight();
    }

    private bool HasClearance(float height)
    {
        float rise = height - CurrentHeight;
        if (rise <= 0f)
            return true;

        // Sweep the current (slightly thinner) body shape upward by the height we want to gain.
        // A sweep catches thin ceilings reliably; an overlap test can miss them.
        const float footLift = 0.1f; // Keep the floor out of the test.
        float testHeight = Mathf.Max(CurrentHeight - footLift, 0.1f);
        _clearanceShape.Height = testHeight;
        _clearanceQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * (footLift + testHeight * 0.5f));
        _clearanceQuery.Motion = Vector3.Up * rise;
        float[] fractions = GetWorld3D().DirectSpaceState.CastMotion(_clearanceQuery);
        return fractions.Length > 0 && fractions[0] >= 1f;
    }

    private void ApplyHeight()
    {
        _bodyShape.Height = CurrentHeight;
        _collider.Position = new Vector3(0f, CurrentHeight * 0.5f, 0f);
        // Placeholder body: squash the graybox mesh to the current height, and tilt it
        // from the feet so the head ends up where the leaning camera is.
        _body.Scale = new Vector3(1f, CurrentHeight / Config.StandHeight, 1f);
        float tilt = Mathf.Atan2(Lean * Config.LeanOffset, EyeHeight);
        _body.Rotation = new Vector3(0f, 0f, -tilt);
        Hitboxes.SetHeightFraction(CurrentHeight / Config.StandHeight);
        Hitboxes.Rotation = _body.Rotation;
    }

    // ---------- Grabs & shove ----------

    public void OnGrabbed(Node3D grabber) => _grabbedBy.Add(grabber);

    public void OnReleased(Node3D grabber) => _grabbedBy.Remove(grabber);

    private const float ShoveReach = 1.9f;
    private const float ShoveConeDegrees = 70f;
    private const float ShoveStaminaCost = 8f;
    private const float ShoveCooldown = 0.9f;

    /// <summary>
    /// Rifle-butt shove: knocks back zombies in front, staggers them, breaks grabs, sometimes knocks
    /// them down. Costs stamina (weaker when tired). Short cooldown.
    /// </summary>
    private void TryShove()
    {
        if (_shoveCooldown > 0f || IsDead)
            return;
        _shoveCooldown = ShoveCooldown;
        StopSprintForHands();
        Weapons.PlayShove();
        float strength = Mathf.Lerp(0.5f, 1f, Stamina.SpendAvailable(ShoveStaminaCost) / ShoveStaminaCost);
        GameNoise.Emit(GlobalPosition, 4f, NoiseKind.Handling, this);

        Vector3 forward = -GlobalBasis.Z;
        forward.Y = 0f;
        bool hitAny = false;
        foreach (Node node in GetTree().GetNodesInGroup(Zombie.Group))
        {
            if (node is not Zombie zombie || zombie.State == ZombieState.Dead)
                continue;
            Vector3 to = zombie.GlobalPosition - GlobalPosition;
            to.Y = 0f;
            bool holdingMe = zombie.IsGrabbing && _grabbedBy.Contains(zombie);
            if (to.Length() > ShoveReach || (!holdingMe && forward.AngleTo(to) > Mathf.DegToRad(ShoveConeDegrees * 0.5f)))
                continue;

            hitAny = true;
            // Wounded legs or a full-strength shove topple it more easily.
            float legDamage = 1f - 0.5f * (zombie.Health.Fraction(BodyPart.LeftLeg) + zombie.Health.Fraction(BodyPart.RightLeg));
            bool knockDown = GD.Randf() < (0.3f + 0.4f * legDamage) * strength;
            zombie.ApplyShove(to.LengthSquared() > 0.01f ? to : forward, strength, knockDown);
            BodyPart part = GD.Randf() < 0.3f ? BodyPart.Head : (GD.Randf() < 0.5f ? BodyPart.Chest : BodyPart.LeftArm);
            zombie.Health.ApplyDamage(part, 6f * strength, DamageSource.Shove);
        }
        if (hitAny)
            _shoveAudio.Play();
    }

    /// <summary>Developer keys for health testing. Works even when dead (Ctrl+G revives).</summary>
    private bool HandleHealthDebugInput(InputEvent e)
    {
        if (!OS.IsDebugBuild())
            return false;
        if (e.IsActionPressed(InputActions.DebugHealAll))
        {
            Health.HealAll();
            Infection.Cure();
            return true;
        }
        if (IsDead)
            return false;
        if (e.IsActionPressed(InputActions.DebugDamageSelf))
        {
            BodyPart part = BodyParts.All[(int)(GD.Randi() % BodyParts.Count)];
            Health.ApplyBulletHit(part, 25f);
            return true;
        }
        if (e.IsActionPressed(InputActions.DebugInfectSelf))
        {
            Infection.Infect();
            return true;
        }
        if (e.IsActionPressed(InputActions.DebugBreakLeg))
        {
            Health.Fracture(GD.Randf() < 0.5f ? BodyPart.LeftLeg : BodyPart.RightLeg);
            return true;
        }
        if (e.IsActionPressed(InputActions.DebugMedicalUp))
        {
            Skills.SetLevel(SkillId.Medical, Skills.GetLevel(SkillId.Medical) + 1);
            return true;
        }
        if (e.IsActionPressed(InputActions.DebugMedicalDown))
        {
            Skills.SetLevel(SkillId.Medical, Skills.GetLevel(SkillId.Medical) - 1);
            return true;
        }
        return false;
    }

    // ---------- Persistence ----------

    public Dictionary SaveState() => new()
    {
        ["stance"] = (int)Stance,
        ["skills"] = Skills.SaveState(),
        ["stamina"] = Stamina.SaveState(),
        // Position is saved once the chunked world exists (build step 8/9).
    };

    public void LoadState(Dictionary state)
    {
        Skills.LoadState(state["skills"].AsGodotDictionary());
        Stamina.LoadState(state["stamina"].AsGodotDictionary());
        WantedStance = Stance = (Stance)state["stance"].AsInt32();
        CurrentHeight = HeightFor(Stance);
        ApplyHeight();
    }
}
