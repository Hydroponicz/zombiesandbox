using System;
using Godot;
using Godot.Collections;
using ZombieSandbox.Core;

namespace ZombieSandbox.Combat;

/// <summary>
/// One magazine and the rounds in it. Plain data (not a node) so it can live in a weapon, a pouch,
/// on the ground, or later in any inventory container, and be saved anywhere.
/// </summary>
public sealed class MagazineItem : ISaveState
{
    public string Id { get; private set; } = Guid.NewGuid().ToString("N");
    public int Capacity { get; private set; }
    public int Rounds { get; set; }
    public AmmoData Ammo { get; private set; }

    public MagazineItem(int capacity, AmmoData ammo, int rounds)
    {
        Capacity = capacity;
        Ammo = ammo;
        Rounds = Math.Clamp(rounds, 0, capacity);
    }

    public bool IsEmpty => Rounds <= 0;
    public float Fill => Capacity > 0 ? (float)Rounds / Capacity : 0f;

    public Dictionary SaveState() => new()
    {
        ["id"] = Id,
        ["capacity"] = Capacity,
        ["rounds"] = Rounds,
        ["ammo"] = Ammo.ResourcePath,
    };

    public void LoadState(Dictionary state)
    {
        Id = state["id"].AsString();
        Capacity = state["capacity"].AsInt32();
        Rounds = state["rounds"].AsInt32();
        Ammo = GD.Load<AmmoData>(state["ammo"].AsString());
    }
}
