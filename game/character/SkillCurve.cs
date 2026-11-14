using Godot;

namespace ZombieSandbox.Character;

/// <summary>How much XP each skill level costs. Each level costs Growth times more than the last.</summary>
[GlobalClass]
public partial class SkillCurve : Resource
{
    [Export] public int MaxLevel { get; set; } = 10;
    /// <summary>XP needed to go from level 0 to level 1.</summary>
    [Export] public float BaseXp { get; set; } = 60f;
    [Export] public float Growth { get; set; } = 1.5f;

    public float XpForNextLevel(int currentLevel) => BaseXp * Mathf.Pow(Growth, currentLevel);
}
