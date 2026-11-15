using Godot;

namespace ZombieSandbox.Character;

/// <summary>How skills scale stats. Values are given at level 0 and level 10 and blended linearly.</summary>
[GlobalClass]
public partial class StatsConfig : Resource
{
    [ExportGroup("Fitness → stamina")]
    [Export] public float MaxStaminaAtLevel0 { get; set; } = 42f;
    [Export] public float MaxStaminaAtLevel10 { get; set; } = 150f;
    [Export] public float StaminaRegenAtLevel0 { get; set; } = 8f;
    [Export] public float StaminaRegenAtLevel10 { get; set; } = 14f;

    [ExportGroup("Aiming → weapon handling")]
    [Export] public float HoldBreathSecondsAtLevel0 { get; set; } = 4f;
    [Export] public float HoldBreathSecondsAtLevel10 { get; set; } = 10f;
    /// <summary>Sway multiplier at Aiming 10 (level 0 = 1.0).</summary>
    [Export] public float AimSwayMultiplierAtLevel10 { get; set; } = 0.6f;

    [ExportGroup("Maintenance → weapon care")]
    /// <summary>Jam clearing / inspection time multiplier at Maintenance 10 (level 0 = 1.0).</summary>
    [Export] public float MaintenanceTimeMultiplierAtLevel10 { get; set; } = 0.5f;
    /// <summary>Weapon wear multiplier at Maintenance 10 (level 0 = 1.0).</summary>
    [Export] public float WearMultiplierAtLevel10 { get; set; } = 0.5f;

    [ExportGroup("Reloading → weapon handling")]
    /// <summary>Reload and magazine-check time multiplier at Reloading 10 (level 0 = 1.0).</summary>
    [Export] public float ReloadTimeMultiplierAtLevel10 { get; set; } = 0.5f;

    [ExportGroup("Strength → weapon handling")]
    [Export] public float RecoilMultiplierAtLevel0 { get; set; } = 1.25f;
    [Export] public float RecoilMultiplierAtLevel10 { get; set; } = 0.7f;

    [ExportGroup("Fitness → movement (level 0 = 1.0)")]
    [Export] public float SprintSpeedMultiplierAtLevel10 { get; set; } = 1.08f;
    [Export] public float AccelerationMultiplierAtLevel10 { get; set; } = 1.10f;
}
