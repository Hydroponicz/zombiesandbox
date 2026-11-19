using Godot;

namespace ZombieSandbox.Inventory;

/// <summary>One possible item in a loot table.</summary>
[GlobalClass]
public partial class LootEntry : Resource
{
    [Export] public ItemData? Item { get; set; }
    /// <summary>How likely it is compared to the other entries.</summary>
    [Export] public float Weight { get; set; } = 1f;
    [Export] public int MinCount { get; set; } = 1;
    [Export] public int MaxCount { get; set; } = 1;
}
