using Godot;
using ZombieSandbox.Core.Interaction;

namespace ZombieSandbox.Inventory;

/// <summary>
/// An item lying in the world. Place one in a level with <see cref="Data"/> set, or drop one with
/// <see cref="Spawn"/>. Picked up with Interact (into the first free spot) or from the inventory's
/// ground panel. Until real models: a box sized and coloured from the item.
/// </summary>
public partial class WorldItem : RigidBody3D, IInteractable
{
    public const string Group = "world_items";
    /// <summary>Metres per inventory cell for the placeholder box.</summary>
    private const float CellMeters = 0.12f;

    [Export] public ItemData? Data { get; set; }
    [Export] public int Count { get; set; } = 1;

    public Item? Item { get; set; }

    public string InteractPrompt => _wouldWear
        ? $"{(Item?.Data.Slot is GearSlot.Primary or GearSlot.Melee ? "Equip" : "Wear")} {Item?.Data.DisplayName}"
        : $"Pick up {Item?.Data.DisplayName}{(Item is { Count: > 1 } ? $" ({Item.Count})" : "")}";
    private bool _wouldWear;

    public override void _Ready()
    {
        AddToGroup(Group);
        if (Item == null && Data != null)
            Item = Item.Create(Data, Count);
        if (Item == null)
        {
            QueueFree();
            return;
        }
        CollisionLayer = 1u << 2; // Items.
        CollisionMask = 1u;
        Mass = Mathf.Max(0.2f, Item.WeightLb * 0.4536f);
        ContinuousCd = true;

        Vector2I cells = Item.Data.Size;
        var size = new Vector3(cells.X * CellMeters, Mathf.Min(cells.X, cells.Y) * CellMeters * 0.6f, cells.Y * CellMeters);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh
            {
                Size = size,
                Material = new StandardMaterial3D { AlbedoColor = Item.Data.Color, Roughness = 0.85f },
            },
        });
    }

    public bool CanInteract(Node user)
    {
        if (Item == null || user.GetNodeOrNull<CharacterInventory>("Inventory") is not { } inventory)
            return false;
        _wouldWear = inventory.CanEquip(Item);
        return true;
    }

    public void Interact(Node user)
    {
        if (Item == null || user.GetNodeOrNull<CharacterInventory>("Inventory") is not { } inventory)
            return;
        // Gear you aren't wearing yet goes straight on; anything else into the first free spot.
        if (inventory.Equip(Item) || inventory.TryAddAnywhere(Item))
        {
            Item = null;
            QueueFree();
        }
        else
        {
            NoRoom?.Invoke(user);
        }
    }

    /// <summary>Raised when someone tries to pick this up without room (for the HUD message).</summary>
    public static event System.Action<Node>? NoRoom;

    /// <summary>Takes the item out of the world (picked up from the inventory screen).</summary>
    public Item? Take()
    {
        Item? item = Item;
        Item = null;
        QueueFree();
        return item;
    }

    /// <summary>Drops an item at a spot with a little toss.</summary>
    public static WorldItem Spawn(Node parent, Item item, Vector3 position, Vector3 velocity)
    {
        var worldItem = new WorldItem { Item = item };
        parent.AddChild(worldItem);
        worldItem.GlobalPosition = position;
        worldItem.LinearVelocity = velocity;
        worldItem.AngularVelocity = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f) * 3f;
        return worldItem;
    }
}
