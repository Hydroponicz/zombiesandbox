using Godot.Collections;

namespace ZombieSandbox.Core;

/// <summary>
/// A component that can write its state to a dictionary and restore it later.
/// Components (skills, stamina, ...) implement this; their owner aggregates them.
/// </summary>
public interface ISaveState
{
    Dictionary SaveState();
    void LoadState(Dictionary state);
}

/// <summary>
/// Anything that exists in the persistent world (player, containers, zombies, NPCs, doors).
/// The id must be stable across sessions so the save system can match state to objects.
/// </summary>
public interface IPersistent : ISaveState
{
    string PersistentId { get; }
}
