using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace ZombieSandbox.Inventory;

/// <summary>What a container (or a body) can hold: a few weighted random picks.</summary>
[GlobalClass]
public partial class LootTable : Resource
{
    [Export] public Array<LootEntry> Entries { get; set; } = new();
    [Export] public int MinItems { get; set; } = 1;
    [Export] public int MaxItems { get; set; } = 4;

    public List<Item> Roll(RandomNumberGenerator rng)
    {
        var items = new List<Item>();
        float total = 0f;
        foreach (LootEntry entry in Entries)
            total += entry.Item != null ? entry.Weight : 0f;
        if (total <= 0f)
            return items;
        int count = rng.RandiRange(MinItems, MaxItems);
        for (int i = 0; i < count; i++)
        {
            float pick = rng.Randf() * total;
            foreach (LootEntry entry in Entries)
            {
                if (entry.Item == null)
                    continue;
                pick -= entry.Weight;
                if (pick > 0f)
                    continue;
                int amount = rng.RandiRange(entry.MinCount, Mathf.Max(entry.MinCount, entry.MaxCount));
                // Found magazines are rarely full.
                int? rounds = entry.Item.Category == ItemCategory.Magazine ? rng.RandiRange(0, entry.Item.Capacity) : null;
                items.Add(Item.Create(entry.Item, amount, rounds));
                break;
            }
        }
        return items;
    }
}
