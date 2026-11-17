using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core;

namespace ZombieSandbox.Combat;

/// <summary>
/// A simple magazine pouch for NPCs (the player's magazines live in the inventory's chest rig and pockets).
/// </summary>
public partial class MagazinePouch : Node, ISaveState, IMagazineStorage
{
    [Export] public int Slots { get; set; } = 6;

    private readonly List<MagazineItem> _magazines = new();

    public IReadOnlyList<MagazineItem> Magazines => _magazines;
    public bool HasSpace => _magazines.Count < Slots;

    public bool Add(MagazineItem magazine)
    {
        if (!HasSpace)
            return false;
        _magazines.Add(magazine);
        return true;
    }

    /// <summary>Removes and returns the magazine with the most rounds (null if the pouch is empty).</summary>
    public MagazineItem? TakeFullest()
    {
        if (_magazines.Count == 0)
            return null;
        MagazineItem best = _magazines[0];
        foreach (MagazineItem magazine in _magazines)
        {
            if (magazine.Rounds > best.Rounds)
                best = magazine;
        }
        _magazines.Remove(best);
        return best;
    }

    public int FullestRounds()
    {
        int best = -1;
        foreach (MagazineItem magazine in _magazines)
            best = Mathf.Max(best, magazine.Rounds);
        return best;
    }

    public Dictionary SaveState()
    {
        var list = new Array<Dictionary>();
        foreach (MagazineItem magazine in _magazines)
            list.Add(magazine.SaveState());
        return new Dictionary { ["magazines"] = list };
    }

    public void LoadState(Dictionary state)
    {
        _magazines.Clear();
        foreach (Variant entry in state["magazines"].AsGodotArray())
        {
            var magazine = new MagazineItem(1, null!, 0);
            magazine.LoadState(entry.AsGodotDictionary());
            _magazines.Add(magazine);
        }
    }
}
