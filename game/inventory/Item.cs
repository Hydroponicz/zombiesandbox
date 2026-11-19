using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Combat;

namespace ZombieSandbox.Inventory;

/// <summary>
/// One item (or stack) that exists somewhere: in a grid, worn, or on the ground. Plain data with a
/// stable id. Magazines carry their <see cref="MagazineItem"/>; rigs and backpacks carry their grids.
/// </summary>
public sealed class Item
{
    public string Id { get; private set; }
    public ItemData Data { get; }
    public int Count { get; set; }
    public MagazineItem? Magazine { get; private set; }
    /// <summary>Storage this item gives when worn (empty for ordinary items).</summary>
    public List<ItemGrid> Grids { get; } = new();
    private Node3D? _instance;

    /// <summary>
    /// For weapon items: the live weapon (a gun keeps its magazine, chamber, condition and jams on it).
    /// Created the first time it's needed; it lives in the hands or, while stowed, outside the scene.
    /// </summary>
    public Node3D? Instance => _instance ??= Data.WeaponScene?.Instantiate<Node3D>();

    public Item(ItemData data, int count = 1, string? id = null)
    {
        Data = data;
        Count = Math.Clamp(count, 1, Math.Max(1, data.MaxStack));
        Id = id ?? Guid.NewGuid().ToString("N");
        foreach (Vector2I size in data.Grids)
            Grids.Add(new ItemGrid(size.X, size.Y));
    }

    /// <summary>
    /// A new item from its data, ready to use: a magazine gets its rounds (full unless <paramref name="rounds"/>
    /// says otherwise).
    /// </summary>
    public static Item Create(ItemData data, int count = 1, int? rounds = null)
    {
        if (data.Category == ItemCategory.Magazine && data.Ammo != null)
        {
            var magazine = new MagazineItem(data.Capacity, data.Ammo, rounds ?? data.Capacity);
            return FromMagazine(data, magazine);
        }
        return new Item(data, count);
    }

    /// <summary>A weapon item for a weapon that already exists (keeps its magazine, condition, ...).</summary>
    public static Item FromInstance(ItemData data, Node3D weapon) => new(data) { _instance = weapon };

    /// <summary>Wraps a magazine as an inventory item (same id as the magazine).</summary>
    public static Item FromMagazine(ItemData data, MagazineItem magazine) => new(data, 1, magazine.Id) { Magazine = magazine };

    public float WeightLb
    {
        get
        {
            float weight = Data.WeightLb * Count;
            if (Magazine != null)
                weight += Magazine.Rounds * Data.WeightPerRoundLb;
            // A gun's loaded magazine counts too.
            if (_instance is Weapon { LoadedMagazine: { } loaded } gun && gun.Data.MagazineItemData is { } magData)
                weight += magData.WeightLb + loaded.Rounds * magData.WeightPerRoundLb;
            foreach (ItemGrid grid in Grids)
                weight += grid.WeightLb;
            return weight;
        }
    }

    /// <summary>True if <paramref name="other"/> is somewhere inside this item's grids.</summary>
    public bool Contains(Item other)
    {
        foreach (ItemGrid grid in Grids)
        {
            if (grid.Contains(other))
                return true;
        }
        return false;
    }

    /// <summary>Moves as much of <paramref name="from"/> as fits into the stack <paramref name="into"/>.</summary>
    public static void MergeInto(Item from, Item into)
    {
        if (from == into || from.Data != into.Data || into.Count >= into.Data.MaxStack)
            return;
        int moved = Math.Min(from.Count, into.Data.MaxStack - into.Count);
        into.Count += moved;
        from.Count -= moved;
    }

    public Dictionary SaveState()
    {
        var state = new Dictionary
        {
            ["id"] = Id,
            ["data"] = Data.ResourcePath,
            ["count"] = Count,
        };
        if (Magazine != null)
            state["magazine"] = Magazine.SaveState();
        if (_instance is Weapon gun)
        {
            var weapon = new Dictionary
            {
                ["condition"] = gun.Condition,
                ["loaded"] = gun.Loaded,
                ["chambered"] = gun.Chambered,
                ["fire_mode"] = (int)gun.FireMode,
                ["malfunction"] = (int)gun.Malfunction,
                ["malfunction_known"] = gun.MalfunctionKnown,
            };
            if (gun.LoadedMagazine != null)
                weapon["magazine"] = gun.LoadedMagazine.SaveState();
            state["weapon"] = weapon;
        }
        else if (_instance is MeleeWeapon melee)
        {
            state["weapon"] = new Dictionary { ["condition"] = melee.Condition };
        }
        if (Grids.Count > 0)
        {
            var grids = new Array<Dictionary>();
            foreach (ItemGrid grid in Grids)
                grids.Add(grid.SaveState());
            state["grids"] = grids;
        }
        return state;
    }

    public static Item? Load(Dictionary state)
    {
        if (GD.Load<ItemData>(state["data"].AsString()) is not { } data)
            return null;
        var item = new Item(data, state["count"].AsInt32(), state["id"].AsString());
        if (state.TryGetValue("magazine", out Variant magazine))
        {
            var mag = new MagazineItem(1, null!, 0);
            mag.LoadState(magazine.AsGodotDictionary());
            item.Magazine = mag;
        }
        if (state.TryGetValue("weapon", out Variant weaponState))
        {
            Dictionary w = weaponState.AsGodotDictionary();
            if (item.Instance is Weapon gun)
            {
                gun.Condition = w["condition"].AsSingle();
                gun.Loaded = w["loaded"].AsBool();
                gun.Chambered = w["chambered"].AsBool();
                gun.FireMode = (FireMode)w["fire_mode"].AsInt32();
                gun.Malfunction = (Malfunction)w["malfunction"].AsInt32();
                gun.MalfunctionKnown = w["malfunction_known"].AsBool();
                if (w.TryGetValue("magazine", out Variant mag))
                {
                    var loaded = new MagazineItem(1, null!, 0);
                    loaded.LoadState(mag.AsGodotDictionary());
                    gun.LoadedMagazine = loaded;
                }
            }
            else if (item.Instance is MeleeWeapon melee)
            {
                melee.Condition = w["condition"].AsSingle();
            }
        }
        if (state.TryGetValue("grids", out Variant grids))
        {
            Godot.Collections.Array list = grids.AsGodotArray();
            for (int i = 0; i < list.Count && i < item.Grids.Count; i++)
                item.Grids[i].LoadState(list[i].AsGodotDictionary());
        }
        return item;
    }
}
