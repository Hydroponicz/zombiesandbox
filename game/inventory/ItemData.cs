using Godot;
using Godot.Collections;

namespace ZombieSandbox.Inventory;

public enum ItemCategory
{
    Misc,
    Magazine,
    Ammo,
    Medical,
    Food,
    Gear,
}

/// <summary>Where a wearable item goes on the body.</summary>
public enum GearSlot
{
    None,
    Headwear,
    BodyArmor,
    ChestRig,
    Backpack,
}

/// <summary>
/// What an item is: name, grid size, weight, stacking. Wearables (rigs, backpacks, later clothing)
/// also list the storage grids they give you.
/// </summary>
[GlobalClass]
public partial class ItemData : Resource
{
    /// <summary>Stable id (save files key on the resource path; this is for display/debug).</summary>
    [Export] public string Id { get; set; } = "item";
    [Export] public string DisplayName { get; set; } = "Item";
    /// <summary>Text drawn on the item's grid box until there are icons.</summary>
    [Export] public string ShortName { get; set; } = "Item";
    /// <summary>Grid cells, width × height (unrotated).</summary>
    [Export] public Vector2I Size { get; set; } = Vector2I.One;
    /// <summary>Weight of one (pounds).</summary>
    [Export] public float WeightLb { get; set; } = 0.5f;
    [Export] public int MaxStack { get; set; } = 1;
    [Export] public ItemCategory Category { get; set; } = ItemCategory.Misc;
    /// <summary>Placeholder colour for the grid box and the world model.</summary>
    [Export] public Color Color { get; set; } = new(0.45f, 0.45f, 0.42f);

    [ExportGroup("Wearable")]
    [Export] public GearSlot Slot { get; set; } = GearSlot.None;
    /// <summary>Storage grids this gives when worn (e.g. a rig: four 1×2 mag pouches and two 1×1).</summary>
    [Export] public Array<Vector2I> Grids { get; set; } = new();

    [ExportGroup("Magazine")]
    /// <summary>Added weight per loaded round (pounds).</summary>
    [Export] public float WeightPerRoundLb { get; set; } = 0.027f;
}
