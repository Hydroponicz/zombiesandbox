using Godot;

namespace ZombieSandbox.Core.Input;

/// <summary>
/// Action names and their default bindings. Every action has a keyboard/mouse and a gamepad
/// binding. Game code only ever asks about these actions, never about raw keys or buttons,
/// so a rebinding menu can change them later.
/// </summary>
public static class InputActions
{
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string LookLeft = "look_left";
    public const string LookRight = "look_right";
    public const string LookUp = "look_up";
    public const string LookDown = "look_down";
    public const string Sprint = "sprint";
    public const string Walk = "walk";
    public const string Jump = "jump";
    public const string Crouch = "crouch";
    public const string Prone = "prone";
    public const string ToggleView = "toggle_view";
    public const string SwapShoulder = "swap_shoulder";
    public const string LeanLeft = "lean_left";
    public const string LeanRight = "lean_right";
    /// <summary>Aim down sights. Sprint doubles as hold breath while aiming.</summary>
    public const string Aim = "aim";
    public const string ReleaseMouse = "release_mouse";

    // Developer-only (debug builds).
    public const string DebugFitnessUp = "debug_fitness_up";
    public const string DebugFitnessDown = "debug_fitness_down";
    public const string DebugAimingUp = "debug_aiming_up";
    public const string DebugAimingDown = "debug_aiming_down";

    /// <summary>InputMap events with this device id match any connected device.</summary>
    private const int AllDevices = -1;

    public static void RegisterDefaults(float stickDeadzone)
    {
        // Movement: WASD + left stick.
        Bind(MoveForward, stickDeadzone, KeyEvent(Key.W), Axis(JoyAxis.LeftY, -1f));
        Bind(MoveBack, stickDeadzone, KeyEvent(Key.S), Axis(JoyAxis.LeftY, 1f));
        Bind(MoveLeft, stickDeadzone, KeyEvent(Key.A), Axis(JoyAxis.LeftX, -1f));
        Bind(MoveRight, stickDeadzone, KeyEvent(Key.D), Axis(JoyAxis.LeftX, 1f));

        // Look: mouse is read as motion events; the right stick uses these actions.
        Bind(LookLeft, stickDeadzone, Axis(JoyAxis.RightX, -1f));
        Bind(LookRight, stickDeadzone, Axis(JoyAxis.RightX, 1f));
        Bind(LookUp, stickDeadzone, Axis(JoyAxis.RightY, -1f));
        Bind(LookDown, stickDeadzone, Axis(JoyAxis.RightY, 1f));

        Bind(Sprint, 0.5f, KeyEvent(Key.Shift), Button(JoyButton.LeftStick));
        Bind(Walk, 0.5f, KeyEvent(Key.Alt)); // Gamepad walks with light stick tilt.
        Bind(Jump, 0.5f, KeyEvent(Key.Space), Button(JoyButton.A));
        Bind(Crouch, 0.5f, KeyEvent(Key.C), Button(JoyButton.B));
        Bind(Prone, 0.5f, KeyEvent(Key.X), Button(JoyButton.DpadDown));
        Bind(ToggleView, 0.5f, KeyEvent(Key.V), Button(JoyButton.DpadUp));
        Bind(SwapShoulder, 0.5f, KeyEvent(Key.Z), Button(JoyButton.RightStick));
        // Gamepad has no manual lean: it auto-leans when aiming next to cover (build step 2).
        Bind(LeanLeft, 0.5f, KeyEvent(Key.Q));
        Bind(LeanRight, 0.5f, KeyEvent(Key.E));
        Bind(Aim, 0.5f, MouseButtonEvent(MouseButton.Right), Axis(JoyAxis.TriggerLeft, 1f));
        Bind(ReleaseMouse, 0.5f, KeyEvent(Key.Escape));

        if (OS.IsDebugBuild())
        {
            Bind(DebugFitnessUp, 0.5f, KeyEvent(Key.F3));
            Bind(DebugFitnessDown, 0.5f, KeyEvent(Key.F4));
            Bind(DebugAimingUp, 0.5f, KeyEvent(Key.F5));
            Bind(DebugAimingDown, 0.5f, KeyEvent(Key.F6));
        }
    }

    private static void Bind(string action, float deadzone, params InputEvent[] events)
    {
        // If the action was defined in the editor's Input Map, keep the user's version.
        if (InputMap.HasAction(action))
            return;

        InputMap.AddAction(action, deadzone);
        foreach (InputEvent e in events)
            InputMap.ActionAddEvent(action, e);
    }

    // Physical keycodes keep WASD in the same place on non-QWERTY keyboards.
    private static InputEventKey KeyEvent(Key key) => new() { PhysicalKeycode = key, Device = AllDevices };

    private static InputEventMouseButton MouseButtonEvent(MouseButton button) => new() { ButtonIndex = button, Device = AllDevices };

    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button, Device = AllDevices };

    private static InputEventJoypadMotion Axis(JoyAxis axis, float direction) =>
        new() { Axis = axis, AxisValue = direction, Device = AllDevices };
}
