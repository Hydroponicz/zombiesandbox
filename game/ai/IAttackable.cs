using Godot;
using ZombieSandbox.Health;

namespace ZombieSandbox.AI;

/// <summary>Something zombies can attack and grab (the player now; NPCs later).</summary>
public interface IAttackable
{
    BodyHealth Health { get; }
    /// <summary>Null if this body can't be infected.</summary>
    Infection? Infection { get; }
    void OnGrabbed(Node3D grabber);
    void OnReleased(Node3D grabber);
}
