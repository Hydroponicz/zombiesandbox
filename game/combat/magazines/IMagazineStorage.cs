using System.Collections.Generic;

namespace ZombieSandbox.Combat;

/// <summary>
/// Where a character's spare magazines live: a simple pouch (NPCs) or the inventory (the player:
/// chest rig and pockets are within reach for reloads; anything else is not).
/// </summary>
public interface IMagazineStorage
{
    /// <summary>Magazines that can be grabbed for a reload.</summary>
    IReadOnlyList<MagazineItem> Magazines { get; }
    /// <summary>Room to stow one more magazine.</summary>
    bool HasSpace { get; }
    /// <summary>Stow a magazine (false = no room; the caller drops it).</summary>
    bool Add(MagazineItem magazine);
    /// <summary>Remove and return the reachable magazine with the most rounds.</summary>
    MagazineItem? TakeFullest();
    /// <summary>Rounds in the fullest reachable magazine (-1 if none).</summary>
    int FullestRounds();
}
