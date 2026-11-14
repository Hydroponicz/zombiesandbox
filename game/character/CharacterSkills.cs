using System;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core;

namespace ZombieSandbox.Character;

public enum SkillId
{
    Fitness,
    Strength, // Defined now; has no effect until inventory (build step 6).
}

/// <summary>
/// Skill levels and XP for any character. Skills level up by doing: other components call
/// <see cref="AddXp"/> when the character performs the relevant activity.
/// </summary>
public partial class CharacterSkills : Node, ISaveState
{
    private static readonly int SkillCount = Enum.GetValues<SkillId>().Length;

    [Export] public SkillCurve Curve { get; set; } = null!;
    [Export(PropertyHint.Range, "0,10")] public int StartingLevel { get; set; } = 3;

    private readonly int[] _levels = new int[SkillCount];
    private readonly float[] _xp = new float[SkillCount];

    /// <summary>Raised with (skill, new level).</summary>
    public event Action<SkillId, int>? LevelChanged;

    public override void _Ready()
    {
        for (int i = 0; i < SkillCount; i++)
            _levels[i] = Mathf.Clamp(StartingLevel, 0, Curve.MaxLevel);
    }

    public int GetLevel(SkillId skill) => _levels[(int)skill];

    public float GetXp(SkillId skill) => _xp[(int)skill];

    /// <summary>0 at level 0, 1 at max level. Handy for scaling stats.</summary>
    public float GetLevelFraction(SkillId skill) => (float)GetLevel(skill) / Curve.MaxLevel;

    public bool IsMaxLevel(SkillId skill) => GetLevel(skill) >= Curve.MaxLevel;

    public float XpForNextLevel(SkillId skill) => Curve.XpForNextLevel(GetLevel(skill));

    public void AddXp(SkillId skill, float amount)
    {
        int i = (int)skill;
        if (amount <= 0f || IsMaxLevel(skill))
            return;

        _xp[i] += amount;
        while (!IsMaxLevel(skill) && _xp[i] >= Curve.XpForNextLevel(_levels[i]))
        {
            _xp[i] -= Curve.XpForNextLevel(_levels[i]);
            _levels[i]++;
            LevelChanged?.Invoke(skill, _levels[i]);
        }

        if (IsMaxLevel(skill))
            _xp[i] = 0f;
    }

    /// <summary>Sets a level directly (debug tools, character creation, loading).</summary>
    public void SetLevel(SkillId skill, int level)
    {
        int i = (int)skill;
        int clamped = Mathf.Clamp(level, 0, Curve.MaxLevel);
        _xp[i] = 0f;
        if (clamped == _levels[i])
            return;
        _levels[i] = clamped;
        LevelChanged?.Invoke(skill, clamped);
    }

    public Dictionary SaveState()
    {
        var state = new Dictionary();
        foreach (SkillId skill in Enum.GetValues<SkillId>())
        {
            state[skill.ToString()] = new Dictionary
            {
                ["level"] = GetLevel(skill),
                ["xp"] = GetXp(skill),
            };
        }
        return state;
    }

    public void LoadState(Dictionary state)
    {
        foreach (SkillId skill in Enum.GetValues<SkillId>())
        {
            if (!state.TryGetValue(skill.ToString(), out Variant value))
                continue;
            var entry = value.AsGodotDictionary();
            SetLevel(skill, entry["level"].AsInt32());
            _xp[(int)skill] = entry["xp"].AsSingle();
        }
    }
}
