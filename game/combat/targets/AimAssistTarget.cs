using System.Collections.Generic;
using Godot;

namespace ZombieSandbox.Combat;

/// <summary>
/// Marks a point gamepad aim assist can lock onto (center of mass of a target, zombie or hostile NPC).
/// Place it as a child of the target's physics body.
/// </summary>
public partial class AimAssistTarget : Marker3D
{
    public static readonly List<AimAssistTarget> All = new();

    /// <summary>Rough size of the target around this point (m).</summary>
    [Export] public float Radius { get; set; } = 0.3f;
    /// <summary>Only hostile targets get aim assist.</summary>
    [Export] public bool Hostile { get; set; } = true;

    /// <summary>The body bullets and line-of-sight checks hit for this target.</summary>
    public CollisionObject3D? Body { get; private set; }

    public override void _EnterTree()
    {
        Body = GetParent() as CollisionObject3D;
        All.Add(this);
    }

    public override void _ExitTree() => All.Remove(this);
}
