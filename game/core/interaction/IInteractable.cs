using Godot;

namespace ZombieSandbox.Core.Interaction;

/// <summary>
/// Something a character can use by looking at it and pressing interact
/// (pick up an item now; doors, containers and NPCs later).
/// </summary>
public interface IInteractable
{
    /// <summary>Short action text for the on-screen prompt, e.g. "Pick up magazine".</summary>
    string InteractPrompt { get; }

    bool CanInteract(Node user);

    void Interact(Node user);
}
