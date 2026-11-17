using Godot;
using ZombieSandbox.Core.Interaction;

namespace ZombieSandbox.Combat;

/// <summary>A magazine lying in the world (dropped during a fast reload). Can be picked back up.</summary>
public partial class DroppedMagazine : RigidBody3D, IInteractable
{
    public const string Group = "dropped_magazines";

    public override void _Ready() => AddToGroup(Group);

    public MagazineItem? Magazine { get; set; }

    public string InteractPrompt => "Pick up magazine";

    public bool CanInteract(Node user) => Magazine != null && FindStorage(user) is { HasSpace: true };

    public void Interact(Node user)
    {
        IMagazineStorage? storage = FindStorage(user);
        if (Magazine == null || storage == null || !storage.Add(Magazine))
            return;
        Magazine = null;
        QueueFree();
    }

    /// <summary>Takes the magazine out of the world (picked up from the inventory screen).</summary>
    public MagazineItem? Take()
    {
        MagazineItem? magazine = Magazine;
        Magazine = null;
        QueueFree();
        return magazine;
    }

    /// <summary>The player's inventory, or an NPC's simple pouch.</summary>
    private static IMagazineStorage? FindStorage(Node user) =>
        (IMagazineStorage?)user.GetNodeOrNull<ZombieSandbox.Inventory.CharacterInventory>("Inventory")
        ?? user.GetNodeOrNull<MagazinePouch>("MagazinePouch");
}
