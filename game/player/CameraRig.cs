using Godot;
using Godot.Collections;
using ZombieSandbox.Combat;
using ZombieSandbox.Core.Input;

namespace ZombieSandbox.Player;

/// <summary>
/// First/third-person camera. The rig owns the look direction (yaw/pitch); the player body
/// turns to match it every physics tick.
///
/// The rig is top-level and not physics-interpolated: it follows the player's interpolated
/// position but applies mouse look instantly, so looking never feels laggy on high-refresh screens.
/// </summary>
public partial class CameraRig : Node3D
{
    /// <summary>Render layer for meshes hidden from the first-person camera (head, hair...). They still cast shadows.</summary>
    public const int FirstPersonHiddenLayer = 2;

    [Export] public CameraConfig Config { get; set; } = null!;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    public bool IsFirstPerson { get; private set; }
    public bool IsRightShoulder => _shoulderSide > 0f;
    /// <summary>The camera is actually rendering first person right now (chosen view, or aiming in "switch to first person" mode).</summary>
    public bool IsViewFirstPerson { get; private set; }
    /// <summary>Third-person over-the-shoulder aiming is (nearly) fully engaged; the HUD shows the aim dot.</summary>
    public bool ShowAimDot { get; private set; }
    public Camera3D Camera => _camera;

    private PlayerController _player = null!;
    private Node3D _pitchPivot = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private float _shoulderSide = 1f;
    private float _shoulderBlend = 1f;
    private PhysicsShapeQueryParameters3D _fpCollisionQuery = null!;
    private readonly PhysicsRayQueryParameters3D _convergenceRay = new();
    private readonly PhysicsRayQueryParameters3D _assistRay = new();
    private AimAssistTarget? _assistTarget;
    private Vector2 _assistLastAngles;
    private float _assistSlowdown = 1f;
    private int _lastShotCount;
    private float _shake;
    private Vector3 _shakeDirection;
    private readonly RandomNumberGenerator _shakeRng = new();
    /// <summary>The target aim assist is currently helping with (null if none).</summary>
    public AimAssistTarget? AimAssistTarget => _assistTarget;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _pitchPivot = GetNode<Node3D>("Pitch");
        _arm = GetNode<SpringArm3D>("Pitch/SpringArm3D");
        _camera = GetNode<Camera3D>("Pitch/SpringArm3D/Camera3D");

        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _arm.AddExcludedObject(_player.GetRid());
        _camera.Fov = Config.FieldOfView;
        _fpCollisionQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = Config.FirstPersonCollisionRadius },
            Exclude = new Array<Rid> { _player.GetRid() },
        };
        _assistRay.Exclude = new Array<Rid> { _player.GetRid() };
        Yaw = _player.GlobalRotation.Y;

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion)
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                InputSettings s = GameInput.Instance.Settings;
                float radiansPerPixel = Mathf.DegToRad(s.MouseSensitivity) * Mathf.Lerp(1f, s.MouseAimSensitivity, _player.Weapons.AdsBlend);
                AddLook(-motion.Relative.X * radiansPerPixel, -motion.Relative.Y * radiansPerPixel * (s.InvertY ? -1f : 1f));
            }
            return;
        }

        if (e.IsActionPressed(InputActions.ReleaseMouse))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // Clicking the game window grabs the mouse again (not a gameplay input).
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed(InputActions.ToggleView))
        {
            IsFirstPerson = !IsFirstPerson;
        }
        else if (e.IsActionPressed(InputActions.SwapShoulder) && !IsFirstPerson)
        {
            _shoulderSide = -_shoulderSide;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateAimAssist();
        ApplyGamepadLook(dt);

        _shoulderBlend = Mathf.MoveToward(_shoulderBlend, _shoulderSide, Config.ShoulderSwapSpeed * dt);

        GlobalPosition = _player.GetGlobalTransformInterpolated().Origin;
        Rotation = new Vector3(0f, Yaw, 0f);

        // The camera shows part of the weapon's recoil kick (it springs back on its own).
        Vector2 cameraKick = _player.Weapons.CameraRecoilDegrees;
        float kickPitch = Mathf.DegToRad(cameraKick.X);
        float kickYaw = Mathf.DegToRad(cameraKick.Y);
        _pitchPivot.Rotation = new Vector3(Pitch + kickPitch, kickYaw, 0f);
        UpdateCameraShake(dt);

        float eye = _player.EyeHeight;
        float lean = _player.Lean;
        float leanOffset = lean * _player.Config.LeanOffset;

        // In third person, leaning moves the camera to the shoulder you lean toward.
        if (!IsFirstPerson && Mathf.Abs(lean) > 0.1f)
            _shoulderSide = Mathf.Sign(lean);

        WeaponHolder weapons = _player.Weapons;
        InputSettings settings = GameInput.Instance.Settings;
        float ads = weapons.AdsBlend;

        IsViewFirstPerson = IsFirstPerson
            || (weapons.IsAiming && settings.ThirdPersonAim == ThirdPersonAimMode.SwitchToFirstPerson);
        _camera.SetCullMaskValue(FirstPersonHiddenLayer, !IsViewFirstPerson);

        if (IsViewFirstPerson)
        {
            _pitchPivot.Position = KeepFirstPersonCameraOutOfWalls(new Vector3(leanOffset, eye, -Config.FirstPersonForwardOffset), eye);
            _pitchPivot.Rotation = new Vector3(Pitch + kickPitch, kickYaw, -lean * Mathf.DegToRad(Config.FirstPersonLeanRoll));
            _arm.Position = Vector3.Zero;
            _arm.SpringLength = 0f;
            float adsFov = weapons.Weapon?.Data.AdsFieldOfView ?? Config.FieldOfView;
            _camera.Fov = Mathf.Lerp(Config.FieldOfView, adsFov, ads);
            ShowAimDot = false;

            // First person: the weapon is aimed exactly where the camera looks (without the
            // cosmetic shake, which lives on the camera node itself and must never move shots).
            weapons.AimPivot = _pitchPivot.GlobalTransform;
        }
        else
        {
            float shoulderAim = settings.ThirdPersonAim == ThirdPersonAimMode.OverTheShoulder ? Mathf.SmoothStep(0f, 1f, ads) : 0f;
            _pitchPivot.Position = new Vector3(leanOffset, eye + Config.ThirdPersonHeightOffset, 0f);
            float shoulder = Mathf.Lerp(Config.ShoulderOffset, Config.ShoulderAimOffset, shoulderAim);
            _arm.Position = new Vector3(shoulder * _shoulderBlend, 0f, 0f);
            _arm.SpringLength = Mathf.Lerp(Config.ThirdPersonDistance, Config.ShoulderAimDistance, shoulderAim);
            _camera.Fov = Mathf.Lerp(Config.FieldOfView, Config.ShoulderAimFieldOfView, shoulderAim);
            ShowAimDot = shoulderAim > 0.8f;

            // Third person: the weapon is held at the character's eye and aimed at whatever the
            // camera's center is looking at, so the shot lands where the player is looking.
            Vector3 eyeWorld = GlobalTransform * new Vector3(leanOffset, eye, -Config.FirstPersonForwardOffset);
            weapons.AimPivot = new Transform3D(Basis.Identity, eyeWorld).LookingAt(ConvergencePoint(eyeWorld), Vector3.Up);
        }
    }

    /// <summary>The point the third-person camera's center is looking at (or a far point along its view).</summary>
    private Vector3 ConvergencePoint(Vector3 eyeWorld)
    {
        Transform3D cam = _camera.GlobalTransform;
        Vector3 forward = -cam.Basis.Z;
        // Start the ray level with the character so we never "aim" at things between the camera and the player.
        float startAlong = Mathf.Max((eyeWorld - cam.Origin).Dot(forward), 0f);
        _convergenceRay.From = cam.Origin + forward * startAlong;
        _convergenceRay.To = cam.Origin + forward * 1000f;
        _convergenceRay.CollisionMask = _player.CollisionMask | ZombieSandbox.Health.HitboxRig.HitboxLayer;
        _convergenceRay.Exclude = _player.SelfExclude;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_convergenceRay);
        Vector3 point = hit.Count > 0 ? hit["position"].AsVector3() : _convergenceRay.To;

        // Too close to aim at sensibly (e.g. a wall right next to you): fall back to a point straight ahead.
        if ((point - eyeWorld).Dot(forward) < 1f)
            point = eyeWorld + forward * 10f;
        return point;
    }

    /// <summary>
    /// Sweeps a small sphere from the center of the head (always inside the body, so always clear)
    /// to the wanted camera spot, and stops it at the first wall. Covers leaning, the forward
    /// offset and anything else that could push the camera into geometry.
    /// </summary>
    private Vector3 KeepFirstPersonCameraOutOfWalls(Vector3 wantedLocal, float eye)
    {
        Vector3 fromLocal = new(0f, eye, 0f);
        Vector3 from = GlobalTransform * fromLocal;
        _fpCollisionQuery.CollisionMask = _player.CollisionMask;
        _fpCollisionQuery.Transform = new Transform3D(Basis.Identity, from);
        _fpCollisionQuery.Motion = GlobalTransform * wantedLocal - from;
        float[] fractions = GetWorld3D().DirectSpaceState.CastMotion(_fpCollisionQuery);
        float safe = fractions.Length > 0 ? fractions[0] : 1f;
        return fromLocal.Lerp(wantedLocal, safe);
    }

    /// <summary>A quick, decaying jolt per shot. Scaled (or disabled) by the Camera Shake setting.</summary>
    private void UpdateCameraShake(float dt)
    {
        int shots = _player.Weapons.ShotsFired;
        if (shots != _lastShotCount)
        {
            _lastShotCount = shots;
            _shake = Mathf.Min(_shake + 1f, 1.5f);
            _shakeDirection = new Vector3(_shakeRng.RandfRange(0.4f, 1f), _shakeRng.RandfRange(-1f, 1f), _shakeRng.RandfRange(-1f, 1f));
        }

        _shake *= Mathf.Exp(-20f * dt);
        float amount = _shake * GameInput.Instance.Settings.CameraShake;
        _camera.Rotation = new Vector3(
            Mathf.DegToRad(Config.ShakePitchDegrees * _shakeDirection.X * amount),
            Mathf.DegToRad(Config.ShakeYawDegrees * _shakeDirection.Y * amount),
            Mathf.DegToRad(Config.ShakeRollDegrees * _shakeDirection.Z * amount));
    }

    private void ApplyGamepadLook(float dt)
    {
        Vector2 stick = Input.GetVector(InputActions.LookLeft, InputActions.LookRight, InputActions.LookUp, InputActions.LookDown);
        float magnitude = stick.Length();
        if (magnitude < 0.0001f)
            return;

        InputSettings s = GameInput.Instance.Settings;
        Vector2 curved = stick / magnitude * Mathf.Pow(Mathf.Min(magnitude, 1f), s.LookResponseCurve);
        float aimScale = Mathf.Lerp(1f, s.GamepadAimSensitivity, _player.Weapons.AdsBlend) * _assistSlowdown;
        AddLook(
            -curved.X * Mathf.DegToRad(s.GamepadYawSpeed) * aimScale * dt,
            -curved.Y * Mathf.DegToRad(s.GamepadPitchSpeed) * aimScale * dt * (s.InvertY ? -1f : 1f));
    }

    /// <summary>
    /// Gamepad aim assist ("grippy", Apex/modern-CoD style):
    /// - Slowdown: look speed drops as the screen center nears a target.
    /// - Tracking: while you're moving or aiming, the camera follows part of the target's movement
    ///   relative to you, so strafing or a moving target doesn't drag your aim off.
    /// Only hostile targets in line of sight, only with a gamepad.
    /// </summary>
    private void UpdateAimAssist()
    {
        _assistSlowdown = 1f;
        InputSettings settings = GameInput.Instance.Settings;
        float strength = settings.AimAssistMultiplier;
        if (strength <= 0f || GameInput.Instance.LastDevice != InputDevice.Gamepad || _player.Weapons.Weapon == null)
        {
            _assistTarget = null;
            return;
        }

        float ads = _player.Weapons.AdsBlend;
        Transform3D cam = _camera.GlobalTransform;
        Vector3 forward = -cam.Basis.Z;
        float range = Mathf.Lerp(Config.AimAssistRangeHip, Config.AimAssistRangeAimed, ads);

        AimAssistTarget? best = null;
        float bestCloseness = 0f;
        foreach (AimAssistTarget target in AimAssistTarget.All)
        {
            if (!target.Hostile || !target.IsInsideTree())
                continue;
            Vector3 toTarget = target.GlobalPosition - cam.Origin;
            float distance = toTarget.Length();
            if (distance > range || distance < 0.5f)
                continue;

            float cone = Mathf.Max(Mathf.Atan((target.Radius + Config.AimAssistPadding) / distance), Mathf.DegToRad(Config.AimAssistMinConeDegrees));
            float angle = forward.AngleTo(toTarget);
            if (angle > cone)
                continue;

            float closeness = 1f - angle / cone;
            if (closeness <= bestCloseness || !HasLineOfSight(cam.Origin, target))
                continue;
            best = target;
            bestCloseness = closeness;
        }

        if (best == null)
        {
            _assistTarget = null;
            return;
        }

        float slowdown = Mathf.Lerp(Config.AimAssistSlowdownHip, Config.AimAssistSlowdownAimed, ads) * strength;
        _assistSlowdown = 1f - Mathf.Clamp(slowdown * bestCloseness, 0f, 0.9f);

        // Tracking: measure the target's direction from the player's eye (not the orbiting camera,
        // which would feed our own turning back into the assist).
        Vector3 eye = GlobalPosition + Vector3.Up * _player.EyeHeight;
        Vector3 toBest = best.GlobalPosition - eye;
        var angles = new Vector2(Mathf.Atan2(-toBest.X, -toBest.Z), Mathf.Asin(Mathf.Clamp(toBest.Normalized().Y, -1f, 1f)));

        if (best == _assistTarget)
        {
            Vector2 stick = Input.GetVector(InputActions.LookLeft, InputActions.LookRight, InputActions.LookUp, InputActions.LookDown);
            Vector2 move = Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveForward, InputActions.MoveBack);
            // Only while the player is actively moving or aiming the stick: assist, never an aimbot.
            bool engaged = stick.LengthSquared() > 0.01f || move.LengthSquared() > 0.01f;
            if (engaged)
            {
                float tracking = Mathf.Lerp(Config.AimAssistTrackingHip, Config.AimAssistTrackingAimed, ads) * strength;
                float yawChange = Mathf.Wrap(angles.X - _assistLastAngles.X, -Mathf.Pi, Mathf.Pi);
                float pitchChange = angles.Y - _assistLastAngles.Y;
                AddLook(yawChange * tracking, pitchChange * tracking);
            }
        }

        _assistTarget = best;
        _assistLastAngles = angles;
    }

    private bool HasLineOfSight(Vector3 from, AimAssistTarget target)
    {
        _assistRay.From = from;
        _assistRay.To = target.GlobalPosition;
        _assistRay.CollisionMask = _player.CollisionMask;
        Dictionary hit = GetWorld3D().DirectSpaceState.IntersectRay(_assistRay);
        return hit.Count == 0 || hit["collider"].AsGodotObject() == target.Body;
    }

    private void AddLook(float yawDelta, float pitchDelta)
    {
        Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + pitchDelta, Mathf.DegToRad(Config.PitchMin), Mathf.DegToRad(Config.PitchMax));
    }

}
