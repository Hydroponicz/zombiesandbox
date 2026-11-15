using Godot;
using Godot.Collections;
using ZombieSandbox.Core;

namespace ZombieSandbox.Character;

/// <summary>
/// Leg stamina for any character. Spending stamina trains Fitness, more so when nearly empty.
/// Hitting zero makes the character exhausted until they recover past a threshold.
/// </summary>
public partial class Stamina : Node, ISaveState
{
    [Export] public StaminaConfig Config { get; set; } = null!;

    private CharacterStats _stats = null!;
    private CharacterSkills _skills = null!;
    private float _regenDelayLeft;

    public float Current { get; private set; }
    /// <summary>Temporary reduction from illness etc. Set by the owner.</summary>
    public float MaxMultiplier { get; set; } = 1f;
    public float Max => _stats.MaxStamina * MaxMultiplier;
    public float Fraction => Max > 0f ? Current / Max : 0f;
    public bool IsExhausted { get; private set; }

    public void Initialize(CharacterStats stats, CharacterSkills skills)
    {
        _stats = stats;
        _skills = skills;
        Current = Max;
    }

    /// <summary>Spends a one-off cost (e.g. a jump). Fails if exhausted or not enough stamina.</summary>
    public bool TrySpend(float amount)
    {
        if (IsExhausted || Current < amount)
            return false;
        Spend(amount);
        return true;
    }

    /// <summary>Spends as much of the amount as is available (even when exhausted). Returns what was spent.</summary>
    public float SpendAvailable(float amount)
    {
        float spent = Mathf.Min(amount, Current);
        if (spent > 0f)
            Spend(spent);
        else
            _regenDelayLeft = Config.RegenDelay;
        return spent;
    }

    /// <summary>Continuous drain (e.g. sprinting). Pass amount for this frame.</summary>
    public void Drain(float amount)
    {
        if (amount > 0f)
            Spend(Mathf.Min(amount, Current));
    }

    public void Tick(float delta)
    {
        if (_regenDelayLeft > 0f)
            _regenDelayLeft -= delta;
        else
            Current = Mathf.Min(Current + _stats.StaminaRegenPerSecond * delta, Max);

        if (IsExhausted && Current >= Max * Config.ExhaustedRecoverFraction)
            IsExhausted = false;

        // Max can drop (later: injuries); keep current within bounds.
        if (Current > Max)
            Current = Max;
    }

    private void Spend(float amount)
    {
        float xpMultiplier = 1f + Config.LowStaminaXpBonus * (1f - Fraction);
        _skills.AddXp(SkillId.Fitness, amount * Config.FitnessXpPerStaminaSpent * xpMultiplier);

        Current = Mathf.Max(Current - amount, 0f);
        _regenDelayLeft = Config.RegenDelay;
        if (Current <= 0f)
            IsExhausted = true;
    }

    public Dictionary SaveState() => new()
    {
        ["current"] = Current,
        ["exhausted"] = IsExhausted,
    };

    public void LoadState(Dictionary state)
    {
        Current = Mathf.Clamp(state["current"].AsSingle(), 0f, Max);
        IsExhausted = state["exhausted"].AsBool();
    }
}
