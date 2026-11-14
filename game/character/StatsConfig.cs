using Godot;

namespace ZombieSandbox.Character;

/// <summary>How skills scale stats. Values are given at level 0 and level 10 and blended linearly.</summary>
[GlobalClass]
public partial class StatsConfig : Resource
{
    [ExportGroup("Fitness → stamina")]
    [Export] public float MaxStaminaAtLevel0 { get; set; } = 100f;
    [Export] public float MaxStaminaAtLevel10 { get; set; } = 150f;
    [Export] public float StaminaRegenAtLevel0 { get; set; } = 10f;
    [Export] public float StaminaRegenAtLevel10 { get; set; } = 16f;

    [ExportGroup("Fitness → movement (level 0 = 1.0)")]
    [Export] public float SprintSpeedMultiplierAtLevel10 { get; set; } = 1.08f;
    [Export] public float AccelerationMultiplierAtLevel10 { get; set; } = 1.10f;
}
