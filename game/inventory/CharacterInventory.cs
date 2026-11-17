using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ZombieSandbox.Character;
using ZombieSandbox.Combat;
using ZombieSandbox.Core;

namespace ZombieSandbox.Inventory;

/// <summary>
/// What a character wears and carries: gear slots (headwear, body armor, chest rig, backpack), four
/// fixed 1×1 pockets, and the storage grids that worn gear provides. Also the character's magazine
/// storage: reloads reach the chest rig and pockets only, never the backpack (Tarkov rules).
/// </summary>
public partial class CharacterInventory : Node, ISaveState, IMagazineStorage
{
    public const int PocketCount = 4;
    private static readonly GearSlot[] ReachableSlots = { GearSlot.ChestRig, GearSlot.Top, GearSlot.Bottoms };

    [Export] public CarryConfig Carry { get; set; } = null!;
    [Export] public ItemData? StartingRig { get; set; }
    [Export] public ItemData? StartingBackpack { get; set; }

    /// <summary>The inventory item for this character's magazines (set from the weapon).</summary>
    public ItemData? MagazineData { get; set; }
    /// <summary>Weight carried outside the inventory (the weapon in hand), in pounds.</summary>
    public Func<float>? ExtraWeight { get; set; }

    private readonly System.Collections.Generic.Dictionary<GearSlot, Item?> _equipped = new()
    {
        [GearSlot.Headwear] = null,
        [GearSlot.BodyArmor] = null,
        [GearSlot.ChestRig] = null,
        [GearSlot.Backpack] = null,
        [GearSlot.Top] = null,
        [GearSlot.Bottoms] = null,
    };
    public List<ItemGrid> Pockets { get; } = new();

    private CharacterSkills? _skills;
    private readonly List<MagazineItem> _reachableMagazines = new();
    private bool _magazinesDirty = true;

    /// <summary>Raised whenever anything is added, removed, moved, worn or taken off.</summary>
    public event Action? Changed;

    public override void _Ready()
    {
        for (int i = 0; i < PocketCount; i++)
            Pockets.Add(new ItemGrid(1, 1));
        if (StartingRig != null)
            Equip(new Item(StartingRig));
        if (StartingBackpack != null)
            Equip(new Item(StartingBackpack));
    }

    public void Initialize(CharacterSkills skills) => _skills = skills;

    /// <summary>Call after any change made directly to a grid.</summary>
    public void NotifyChanged()
    {
        _magazinesDirty = true;
        Changed?.Invoke();
    }

    // ---------- Gear ----------

    public Item? Equipped(GearSlot slot) => _equipped.TryGetValue(slot, out Item? item) ? item : null;

    public bool CanEquip(Item item) => item.Data.Slot != GearSlot.None && Equipped(item.Data.Slot) == null;

    public bool Equip(Item item)
    {
        if (!CanEquip(item))
            return false;
        _equipped[item.Data.Slot] = item;
        NotifyChanged();
        return true;
    }

    /// <summary>Takes off whatever is in the slot (the caller puts it somewhere).</summary>
    public Item? Unequip(GearSlot slot)
    {
        Item? item = Equipped(slot);
        if (item == null)
            return null;
        _equipped[slot] = null;
        NotifyChanged();
        return item;
    }

    // ---------- Grids ----------

    /// <summary>Chest rig, clothing pockets, then fixed pockets: what your hands reach in a fight.</summary>
    public IEnumerable<ItemGrid> ReachableGrids()
    {
        foreach (GearSlot slot in ReachableSlots)
        {
            if (Equipped(slot) is not { } worn)
                continue;
            foreach (ItemGrid grid in worn.Grids)
                yield return grid;
        }
        foreach (ItemGrid pocket in Pockets)
            yield return pocket;
    }

    /// <summary>Every grid: rig, pockets, backpack.</summary>
    public IEnumerable<ItemGrid> AllGrids()
    {
        foreach (ItemGrid grid in ReachableGrids())
            yield return grid;
        if (Equipped(GearSlot.Backpack) is { } backpack)
        {
            foreach (ItemGrid grid in backpack.Grids)
                yield return grid;
        }
    }

    /// <summary>Puts an item in the first place it fits: rig, pockets, then backpack.</summary>
    public bool TryAddAnywhere(Item item)
    {
        foreach (ItemGrid grid in AllGrids())
        {
            if (grid.TryMergeIntoStacks(item))
            {
                NotifyChanged();
                return true;
            }
        }
        foreach (ItemGrid grid in AllGrids())
        {
            if (grid.FindSpot(item, out int x, out int y, out bool rotated) && grid.TryPlace(item, x, y, rotated))
            {
                NotifyChanged();
                return true;
            }
        }
        if (item.Count > 0)
            NotifyChanged(); // A partial stack merge still changed things.
        return false;
    }

    public bool HasRoomFor(Item item)
    {
        foreach (ItemGrid grid in AllGrids())
        {
            if (grid.FindSpot(item, out _, out _, out _))
                return true;
        }
        return false;
    }

    /// <summary>Removes an item from wherever it is in this inventory (grid or slot).</summary>
    public bool Remove(Item item)
    {
        foreach (ItemGrid grid in AllGrids())
        {
            if (grid.Remove(item))
            {
                NotifyChanged();
                return true;
            }
        }
        foreach ((GearSlot slot, Item? worn) in _equipped)
        {
            if (worn == item)
            {
                Unequip(slot);
                return true;
            }
        }
        return false;
    }

    // ---------- Weight ----------

    public float WeightLb
    {
        get
        {
            float total = ExtraWeight?.Invoke() ?? 0f;
            foreach (Item? worn in _equipped.Values)
                total += worn?.WeightLb ?? 0f;
            foreach (ItemGrid pocket in Pockets)
                total += pocket.WeightLb;
            return total;
        }
    }

    public float CarryLimitLb
    {
        get
        {
            int strength = _skills?.GetLevel(SkillId.Strength) ?? 3;
            return Mathf.Lerp(Carry.CarryLimitAtStrength0Lb, Carry.CarryLimitAtStrength10Lb, strength / 10f);
        }
    }

    /// <summary>Weight ÷ carry limit (1 = at the limit).</summary>
    public float LoadRatio => WeightLb / Mathf.Max(CarryLimitLb, 1f);
    public bool IsOverweight => LoadRatio > 1f;
    public bool IsHeavilyOverloaded => LoadRatio >= Carry.HeavyRatio;
    public bool IsImmobile => LoadRatio >= Carry.ImmobileRatio;

    /// <summary>Movement speed multiplier from the load.</summary>
    public float SpeedMultiplier
    {
        get
        {
            float r = LoadRatio;
            if (r <= 1f)
                return 1f;
            if (r < Carry.HeavyRatio)
                return Mathf.Lerp(1f, Carry.SpeedAtHeavy, (r - 1f) / (Carry.HeavyRatio - 1f));
            if (r < Carry.ImmobileRatio)
                return Mathf.Lerp(Carry.HeavyWalkSpeed, 0.08f, (r - Carry.HeavyRatio) / (Carry.ImmobileRatio - Carry.HeavyRatio));
            return 0f;
        }
    }

    public float SprintDrainMultiplier
    {
        get
        {
            float r = LoadRatio;
            return r <= 1f ? 1f : Mathf.Lerp(1f, Carry.SprintDrainAtHeavy, Mathf.Min((r - 1f) / (Carry.HeavyRatio - 1f), 1f));
        }
    }

    // ---------- Magazines (IMagazineStorage) ----------

    public IReadOnlyList<MagazineItem> Magazines
    {
        get
        {
            if (_magazinesDirty)
            {
                _reachableMagazines.Clear();
                foreach (ItemGrid grid in ReachableGrids())
                {
                    foreach (ItemGrid.Placement p in grid.Items)
                    {
                        if (p.Item.Magazine != null)
                            _reachableMagazines.Add(p.Item.Magazine);
                    }
                }
                _magazinesDirty = false;
            }
            return _reachableMagazines;
        }
    }

    public bool HasSpace => MagazineData != null && HasRoomFor(new Item(MagazineData));

    public bool Add(MagazineItem magazine) => MagazineData != null && TryAddAnywhere(Item.FromMagazine(MagazineData, magazine));

    public MagazineItem? TakeFullest()
    {
        ItemGrid? bestGrid = null;
        Item? best = null;
        foreach (ItemGrid grid in ReachableGrids())
        {
            foreach (ItemGrid.Placement p in grid.Items)
            {
                if (p.Item.Magazine is { } mag && (best == null || mag.Rounds > best.Magazine!.Rounds))
                {
                    best = p.Item;
                    bestGrid = grid;
                }
            }
        }
        if (best == null)
            return null;
        bestGrid!.Remove(best);
        NotifyChanged();
        return best.Magazine;
    }

    public int FullestRounds()
    {
        int best = -1;
        foreach (MagazineItem magazine in Magazines)
            best = Mathf.Max(best, magazine.Rounds);
        return best;
    }

    // ---------- Saving ----------

    public Godot.Collections.Dictionary SaveState()
    {
        var gear = new Godot.Collections.Dictionary();
        foreach ((GearSlot slot, Item? item) in _equipped)
        {
            if (item != null)
                gear[slot.ToString()] = item.SaveState();
        }
        var pockets = new Array<Godot.Collections.Dictionary>();
        foreach (ItemGrid pocket in Pockets)
            pockets.Add(pocket.SaveState());
        return new Godot.Collections.Dictionary { ["gear"] = gear, ["pockets"] = pockets };
    }

    public void LoadState(Godot.Collections.Dictionary state)
    {
        foreach (GearSlot slot in new List<GearSlot>(_equipped.Keys))
            _equipped[slot] = null;
        foreach ((Variant key, Variant value) in state["gear"].AsGodotDictionary())
        {
            if (Enum.TryParse(key.AsString(), out GearSlot slot) && Item.Load(value.AsGodotDictionary()) is { } item)
                _equipped[slot] = item;
        }
        Godot.Collections.Array pockets = state["pockets"].AsGodotArray();
        for (int i = 0; i < pockets.Count && i < Pockets.Count; i++)
            Pockets[i].LoadState(pockets[i].AsGodotDictionary());
        NotifyChanged();
    }
}
