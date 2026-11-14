using Godot;

namespace ZombieSandbox.Character;

/// <summary>Leg stamina costs and recovery. Max stamina and regen rate come from <see cref="CharacterStats"/>.</summary>
[GlobalClass]
public partial class StaminaConfig : Resource
{
    [Export] public float SprintDrainPerSecond { get; set; } = 12f;
    [Export] public float JumpCost { get; set; } = 15f;
    /// <summary>Seconds after spending stamina before it starts to regenerate.</summary>
    [Export] public float RegenDelay { get; set; } = 1.2f;
    /// <summary>After hitting zero you can't sprint until stamina recovers to this fraction of max.</summary>
    [Export(PropertyHint.Range, "0.05,1.0,0.05")] public float ExhaustedRecoverFraction { get; set; } = 0.3f;

    [ExportGroup("Fitness XP")]
    [Export] public float FitnessXpPerStaminaSpent { get; set; } = 0.1f;
    /// <summary>Extra XP multiplier when spending stamina while nearly empty (0 = no bonus, 1 = double at empty).</summary>
    [Export] public float LowStaminaXpBonus { get; set; } = 1f;
}
