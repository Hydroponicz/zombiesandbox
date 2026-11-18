using Godot;

namespace ZombieSandbox.Combat;

public enum MeleeKind
{
    Blunt,
    Blade,
}

/// <summary>
/// What a melee weapon is. Melee piece A: how it's held. Combat numbers (damage, swing timing,
/// stamina, wear, sticking) arrive with melee piece B.
/// </summary>
[GlobalClass]
public partial class MeleeData : Resource
{
    [Export] public string DisplayName { get; set; } = "Melee weapon";
    [Export] public MeleeKind Kind { get; set; } = MeleeKind.Blunt;

    [ExportGroup("Pose (relative to the eye, metres / degrees)")]
    [Export] public Vector3 HoldPosition { get; set; } = new(0.28f, -0.32f, -0.45f);
    [Export] public Vector3 HoldRotationDegrees { get; set; } = new(-20f, 10f, -15f);
    [Export] public Vector3 LoweredPosition { get; set; } = new(0.3f, -0.6f, -0.3f);
    [Export] public Vector3 LoweredRotationDegrees { get; set; } = new(-70f, 20f, -10f);
}
