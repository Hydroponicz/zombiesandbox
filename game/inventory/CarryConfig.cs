using Godot;

namespace ZombieSandbox.Inventory;

/// <summary>How much a character can carry and what being over the limit does.</summary>
[GlobalClass]
public partial class CarryConfig : Resource
{
    /// <summary>Carry limit (pounds) at Strength 0 and 10; linear in between.</summary>
    [Export] public float CarryLimitAtStrength0Lb { get; set; } = 23f;
    [Export] public float CarryLimitAtStrength10Lb { get; set; } = 65f;
    /// <summary>Weight ÷ limit where you're heavily overloaded: walk only, no sprint or jump.</summary>
    [Export] public float HeavyRatio { get; set; } = 1.6f;
    /// <summary>Weight ÷ limit where you can't move at all.</summary>
    [Export] public float ImmobileRatio { get; set; } = 2.2f;
    /// <summary>Speed multiplier just before heavily overloaded (1 at the limit).</summary>
    [Export] public float SpeedAtHeavy { get; set; } = 0.65f;
    /// <summary>Walking speed multiplier when heavily overloaded, falling to 0 at immobile ("barely move").</summary>
    [Export] public float HeavyWalkSpeed { get; set; } = 0.45f;
    /// <summary>Sprint stamina drain multiplier just before heavily overloaded.</summary>
    [Export] public float SprintDrainAtHeavy { get; set; } = 2.2f;
    /// <summary>Strength XP per second while moving over the limit (at the heavy threshold; less when barely over).</summary>
    [Export] public float StrengthXpPerSecond { get; set; } = 0.6f;
}
