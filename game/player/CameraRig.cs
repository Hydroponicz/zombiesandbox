using Godot;
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

    private PlayerController _player = null!;
    private Node3D _pitchPivot = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private float _shoulderSide = 1f;
    private float _shoulderBlend = 1f;

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
        Yaw = _player.GlobalRotation.Y;

        Input.MouseMode = Input.MouseModeEnum.Captured;
        ApplyView();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion)
        {
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                InputSettings s = GameInput.Instance.Settings;
                float radiansPerPixel = Mathf.DegToRad(s.MouseSensitivity);
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
            ApplyView();
        }
        else if (e.IsActionPressed(InputActions.SwapShoulder) && !IsFirstPerson)
        {
            _shoulderSide = -_shoulderSide;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        ApplyGamepadLook(dt);

        _shoulderBlend = Mathf.MoveToward(_shoulderBlend, _shoulderSide, Config.ShoulderSwapSpeed * dt);

        GlobalPosition = _player.GetGlobalTransformInterpolated().Origin;
        Rotation = new Vector3(0f, Yaw, 0f);
        _pitchPivot.Rotation = new Vector3(Pitch, 0f, 0f);

        float eye = _player.EyeHeight;
        if (IsFirstPerson)
        {
            _pitchPivot.Position = new Vector3(0f, eye, -Config.FirstPersonForwardOffset);
            _arm.Position = Vector3.Zero;
        }
        else
        {
            _pitchPivot.Position = new Vector3(0f, eye + Config.ThirdPersonHeightOffset, 0f);
            _arm.Position = new Vector3(Config.ShoulderOffset * _shoulderBlend, 0f, 0f);
        }
    }

    private void ApplyGamepadLook(float dt)
    {
        Vector2 stick = Input.GetVector(InputActions.LookLeft, InputActions.LookRight, InputActions.LookUp, InputActions.LookDown);
        float magnitude = stick.Length();
        if (magnitude < 0.0001f)
            return;

        InputSettings s = GameInput.Instance.Settings;
        Vector2 curved = stick / magnitude * Mathf.Pow(Mathf.Min(magnitude, 1f), s.LookResponseCurve);
        AddLook(
            -curved.X * Mathf.DegToRad(s.GamepadYawSpeed) * dt,
            -curved.Y * Mathf.DegToRad(s.GamepadPitchSpeed) * dt * (s.InvertY ? -1f : 1f));
    }

    private void AddLook(float yawDelta, float pitchDelta)
    {
        Yaw = Mathf.Wrap(Yaw + yawDelta, -Mathf.Pi, Mathf.Pi);
        Pitch = Mathf.Clamp(Pitch + pitchDelta, Mathf.DegToRad(Config.PitchMin), Mathf.DegToRad(Config.PitchMax));
    }

    private void ApplyView()
    {
        _arm.SpringLength = IsFirstPerson ? 0f : Config.ThirdPersonDistance;
        _camera.SetCullMaskValue(FirstPersonHiddenLayer, !IsFirstPerson);
    }
}
