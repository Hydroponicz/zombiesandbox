using Godot;

namespace ZombieSandbox.Core.Input;

public enum InputDevice
{
    KeyboardMouse,
    Gamepad,
}

public enum PressMode
{
    Hold,
    Toggle,
}

public enum AimAssistStrength
{
    Off,
    Low,
    Standard,
    Strong,
}

public enum ThirdPersonAimMode
{
    SwitchToFirstPerson,
    OverTheShoulder,
}

/// <summary>
/// Player-facing input settings. Later these will be editable in an options menu and saved
/// to user://; for now edit input_settings.tres in the inspector.
/// </summary>
[GlobalClass]
public partial class InputSettings : Resource
{
    [ExportGroup("Hold / Toggle (keyboard)")]
    [Export] public PressMode SprintKeyboard { get; set; } = PressMode.Hold;
    [Export] public PressMode CrouchKeyboard { get; set; } = PressMode.Toggle;
    [Export] public PressMode ProneKeyboard { get; set; } = PressMode.Toggle;
    [Export] public PressMode LeanKeyboard { get; set; } = PressMode.Hold;
    [Export] public PressMode AimKeyboard { get; set; } = PressMode.Toggle;

    [ExportGroup("Hold / Toggle (gamepad)")]
    [Export] public PressMode SprintGamepad { get; set; } = PressMode.Toggle;
    /// <summary>Toggle = tap B to crouch, hold B to go prone. Hold = hold B to crouch (prone moves to D-pad down).</summary>
    [Export] public PressMode CrouchGamepad { get; set; } = PressMode.Toggle;
    [Export] public PressMode ProneGamepad { get; set; } = PressMode.Toggle;
    [Export] public PressMode AimGamepad { get; set; } = PressMode.Hold;
    /// <summary>How long B must be held to go prone instead of crouching.</summary>
    [Export(PropertyHint.Range, "0.1,1.0,0.05")] public float CrouchHoldForProneSeconds { get; set; } = 0.3f;

    [ExportGroup("Mouse")]
    /// <summary>Degrees of rotation per pixel of mouse movement.</summary>
    [Export(PropertyHint.Range, "0.01,0.5,0.005")] public float MouseSensitivity { get; set; } = 0.08f;

    [ExportGroup("Gamepad")]
    [Export(PropertyHint.Range, "0.0,0.5,0.01")] public float StickDeadzone { get; set; } = 0.15f;
    /// <summary>Degrees per second at full right-stick deflection.</summary>
    [Export] public float GamepadYawSpeed { get; set; } = 200f;
    [Export] public float GamepadPitchSpeed { get; set; } = 140f;
    /// <summary>1 = linear. Higher = finer control near the center of the stick.</summary>
    [Export(PropertyHint.Range, "1.0,4.0,0.1")] public float LookResponseCurve { get; set; } = 1.8f;

    [ExportGroup("Aiming")]
    /// <summary>What aiming down sights does in third person.</summary>
    [Export] public ThirdPersonAimMode ThirdPersonAim { get; set; } = ThirdPersonAimMode.SwitchToFirstPerson;
    /// <summary>Look sensitivity multiplier while aiming down sights.</summary>
    [Export(PropertyHint.Range, "0.1,1.5,0.05")] public float MouseAimSensitivity { get; set; } = 0.7f;
    [Export(PropertyHint.Range, "0.1,1.5,0.05")] public float GamepadAimSensitivity { get; set; } = 0.6f;
    /// <summary>Gamepad only: aiming next to a corner leans around it.</summary>
    [Export] public bool GamepadAutoLean { get; set; } = true;
    /// <summary>Gamepad only. Slows your aim over targets and helps track moving ones.</summary>
    [Export] public AimAssistStrength AimAssist { get; set; } = AimAssistStrength.Standard;
    /// <summary>Aiming while crouched behind low cover rises up to aim over it.</summary>
    [Export] public bool PeekOverCover { get; set; } = true;

    [ExportGroup("Comfort")]
    /// <summary>Per-shot camera shake. 0 = off, 1 = full.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float CameraShake { get; set; } = 1f;

    [ExportGroup("General")]
    [Export] public bool InvertY { get; set; }

    public PressMode SprintMode(InputDevice device) => device == InputDevice.Gamepad ? SprintGamepad : SprintKeyboard;
    public PressMode CrouchMode(InputDevice device) => device == InputDevice.Gamepad ? CrouchGamepad : CrouchKeyboard;
    public PressMode ProneMode(InputDevice device) => device == InputDevice.Gamepad ? ProneGamepad : ProneKeyboard;
    public float AimAssistMultiplier => AimAssist switch
    {
        AimAssistStrength.Low => 0.5f,
        AimAssistStrength.Standard => 1f,
        AimAssistStrength.Strong => 1.35f,
        _ => 0f,
    };

    public PressMode AimMode(InputDevice device) => device == InputDevice.Gamepad ? AimGamepad : AimKeyboard;
}
