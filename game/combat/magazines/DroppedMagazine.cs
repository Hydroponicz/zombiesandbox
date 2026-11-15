using Godot;
using ZombieSandbox.Core.Interaction;

namespace ZombieSandbox.Combat;

/// <summary>A magazine lying in the world (dropped during a fast reload). Can be picked back up.</summary>
public partial class DroppedMagazine : RigidBody3D, IInteractable
{
    public MagazineItem? Magazine { get; set; }

    public string InteractPrompt => "Pick up magazine";

    public bool CanInteract(Node user) => Magazine != null && FindPouch(user) is { HasSpace: true };

    public void Interact(Node user)
    {
        MagazinePouch? pouch = FindPouch(user);
        if (Magazine == null || pouch == null || !pouch.Add(Magazine))
            return;
        Magazine = null;
        QueueFree();
    }

    private static MagazinePouch? FindPouch(Node user) => user.GetNodeOrNull<MagazinePouch>("MagazinePouch");
}
