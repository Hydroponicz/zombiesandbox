using Godot;

namespace ZombieSandbox.AI;

/// <summary>How a faction feels about the player.</summary>
public enum Attitude
{
    Hostile,
    Neutral,
    Friendly,
}

/// <summary>
/// A faction: a named set of NPC groups that share one attitude toward the player
/// (anger one group and the whole faction remembers).
/// </summary>
[GlobalClass]
public partial class FactionData : Resource
{
    /// <summary>Stable id (save files key on it).</summary>
    [Export] public string Id { get; set; } = "faction";
    [Export] public string DisplayName { get; set; } = "Faction";
    [Export] public Attitude StartingAttitude { get; set; } = Attitude.Neutral;
    /// <summary>Raiders: enemies of every other faction (and of you).</summary>
    [Export] public bool HostileToEveryone { get; set; }
    /// <summary>In-game days before an angered faction goes back to its starting attitude.</summary>
    [Export] public float CalmDownDays { get; set; } = 21f;
    /// <summary>Armband colour, so you can tell factions apart.</summary>
    [Export] public Color Color { get; set; } = new(0.8f, 0.7f, 0.1f);
}
