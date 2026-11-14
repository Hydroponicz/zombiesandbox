using Godot;

namespace ZombieSandbox.Character;

/// <summary>
/// Turns skills (and later injuries, carry weight, ...) into the final numbers other systems use.
/// Movement and stamina read from here instead of using constants, so every modifier stacks in one place.
/// </summary>
public partial class CharacterStats : Node
{
    [Export] public StatsConfig Config { get; set; } = null!;

    private CharacterSkills _skills = null!;

    public void Initialize(CharacterSkills skills) => _skills = skills;

    private float Fitness => _skills.GetLevelFraction(SkillId.Fitness);
    private float Aiming => _skills.GetLevelFraction(SkillId.Aiming);

    public float MaxStamina => Mathf.Lerp(Config.MaxStaminaAtLevel0, Config.MaxStaminaAtLevel10, Fitness);

    public float StaminaRegenPerSecond => Mathf.Lerp(Config.StaminaRegenAtLevel0, Config.StaminaRegenAtLevel10, Fitness);

    public float SprintSpeedMultiplier => Mathf.Lerp(1f, Config.SprintSpeedMultiplierAtLevel10, Fitness);

    public float AccelerationMultiplier => Mathf.Lerp(1f, Config.AccelerationMultiplierAtLevel10, Fitness);

    public float HoldBreathSeconds => Mathf.Lerp(Config.HoldBreathSecondsAtLevel0, Config.HoldBreathSecondsAtLevel10, Aiming);

    public float AimSwayMultiplier => Mathf.Lerp(1f, Config.AimSwayMultiplierAtLevel10, Aiming);
}
