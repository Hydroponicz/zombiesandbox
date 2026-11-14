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

    [ExportGroup("Hold / Toggle (gamepad)")]
    [Export] public PressMode SprintGamepad { get; set; } = PressMode.Toggle;
    /// <summary>Toggle = tap B to crouch, hold B to go prone. Hold = hold B to crouch (prone moves to D-pad down).</summary>
    [Export] public PressMode CrouchGamepad { get; set; } = PressMode.Toggle;
    [Export] public PressMode ProneGamepad { get; set; } = PressMode.Toggle;
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

    [ExportGroup("General")]
    [Export] public bool InvertY { get; set; }

    public PressMode SprintMode(InputDevice device) => device == InputDevice.Gamepad ? SprintGamepad : SprintKeyboard;
    public PressMode CrouchMode(InputDevice device) => device == InputDevice.Gamepad ? CrouchGamepad : CrouchKeyboard;
    public PressMode ProneMode(InputDevice device) => device == InputDevice.Gamepad ? ProneGamepad : ProneKeyboard;
}
