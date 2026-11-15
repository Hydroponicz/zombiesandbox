using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace ZombieSandbox.Health;

/// <summary>
/// The set of body-part hitboxes for a character. Follows stance height (crouch/prone) and lean.
/// Hitboxes are laid out for a standing 1.8 m body with feet at the origin, facing -Z.
/// </summary>
public partial class HitboxRig : Node3D
{
    public const uint HitboxLayer = 1u << 3; // Physics layer 4: "hitboxes".

    private readonly List<(Hitbox Box, Vector3 BasePosition)> _boxes = new();

    public override void _Ready()
    {
        foreach (Node child in GetChildren())
        {
            if (child is Hitbox box)
                _boxes.Add((box, box.Position));
        }
    }

    /// <summary>Squash the layout to the current body height (1 = standing).</summary>
    public void SetHeightFraction(float fraction)
    {
        foreach ((Hitbox box, Vector3 basePosition) in _boxes)
            box.Position = new Vector3(basePosition.X, basePosition.Y * fraction, basePosition.Z);
    }

    /// <summary>All hitbox bodies, so a character's own bullets and rays can ignore them.</summary>
    public void AddRidsTo(Array<Rid> list)
    {
        foreach ((Hitbox box, _) in _boxes)
            list.Add(box.GetRid());
    }
}
