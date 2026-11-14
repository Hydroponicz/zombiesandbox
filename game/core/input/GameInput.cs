using System;
using Godot;

namespace ZombieSandbox.Core.Input;

/// <summary>
/// Autoload. Registers input actions, owns input settings, and tracks which device the player
/// used last (so speed curves, hold/toggle modes and button prompts can follow it).
/// </summary>
public partial class GameInput : Node
{
    private const string SettingsPath = "res://game/core/input/input_settings.tres";
    private const float StickActivityThreshold = 0.3f;

    public static GameInput Instance { get; private set; } = null!;

    public InputSettings Settings { get; private set; } = null!;
    public InputDevice LastDevice { get; private set; } = InputDevice.KeyboardMouse;

    public event Action<InputDevice>? DeviceChanged;

    public override void _EnterTree()
    {
        Instance = this;
        Settings = ResourceLoader.Exists(SettingsPath)
            ? GD.Load<InputSettings>(SettingsPath)
            : new InputSettings();
        InputActions.RegisterDefaults(Settings.StickDeadzone);
    }

    public override void _Input(InputEvent e)
    {
        InputDevice? device = e switch
        {
            InputEventJoypadButton => InputDevice.Gamepad,
            InputEventJoypadMotion m when Mathf.Abs(m.AxisValue) > StickActivityThreshold => InputDevice.Gamepad,
            InputEventKey or InputEventMouseButton => InputDevice.KeyboardMouse,
            InputEventMouseMotion mm when mm.Relative.LengthSquared() > 4f => InputDevice.KeyboardMouse,
            _ => null,
        };

        if (device is { } d && d != LastDevice)
        {
            LastDevice = d;
            DeviceChanged?.Invoke(d);
        }
    }

    public static InputDevice DeviceOf(InputEvent e) =>
        e is InputEventJoypadButton or InputEventJoypadMotion ? InputDevice.Gamepad : InputDevice.KeyboardMouse;
}
