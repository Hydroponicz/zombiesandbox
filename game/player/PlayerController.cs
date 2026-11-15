using Godot;
using Godot.Collections;
using ZombieSandbox.Character;
using ZombieSandbox.Combat;
using ZombieSandbox.Core;
using ZombieSandbox.Core.Input;

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
public partial class PlayerController : CharacterBody3D, IPersistent
{
    public const string Group = "player";

    [Export] public MovementConfig Config { get; set; } = null!;
    [Export] public string PersistentId { get; set; } = "player";

    public CharacterSkills Skills { get; private set; } = null!;
    public CharacterStats Stats { get; private set; } = null!;
    public Stamina Stamina { get; private set; } = null!;
    public CameraRig CameraRig { get; private set; } = null!;
    public WeaponHolder Weapons { get; private set; } = null!;

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
    private float _jumpBufferLeft;
    private float _landingRecoveryLeft;

    // Input intent.
    private bool _sprintHeld;
    private bool _sprintToggled;
    private bool _triggerHeld;
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

        Stats.Initialize(Skills);
        Stamina.Initialize(Stats, Skills);
        Weapons.Initialize(Stats, Skills, GetRid(), CollisionMask);

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
        _coverRay.Exclude = new Array<Rid> { GetRid() };

        FloorSnapLength = Config.MaxStepHeight; // Stay on the ground walking down stairs.

        _bodyShape.Radius = Config.Radius;
        CurrentHeight = Config.StandHeight;
        ApplyHeight();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsEcho())
            return;

        InputSettings settings = GameInput.Instance.Settings;
        InputDevice device = GameInput.DeviceOf(e);

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
            _triggerHeld = e.IsPressed();
            if (_triggerHeld)
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
        UpdateStance(dt);

        Vector2 input = Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);
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

        UpdateWeaponIntent();
    }

    private void UpdateWeaponIntent()
    {
        if (!WantsAim || Weapons.IsGasping)
            _breathToggled = false;
        if (!WantsAim)
            _breathHeld = false;

        Weapons.WantsAim = WantsAim;
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

    private void HandleAimInput(InputEvent e, PressMode mode)
    {
        if (e.IsPressed())
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

        bool wantsSprint = (_sprintHeld || _sprintToggled) && !WantsAim && !_triggerHeld;
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

        return stanceSpeed * speedFraction;
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
