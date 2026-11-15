using Godot;

namespace ZombieSandbox.Combat;

/// <summary>
/// A weapon instance in the world. The scene provides the model plus two markers:
/// <c>Muzzle</c> (where bullets leave, pointing along -Z) and <c>SightEye</c> (where the eye
/// sits when aiming through the sights).
/// </summary>
public partial class Weapon : Node3D
{
    [Export] public WeaponData Data { get; set; } = null!;
    /// <summary>This weapon's condition in percent. Wears with use; low condition jams more.</summary>
    [Export(PropertyHint.Range, "0,100,0.1")] public float Condition { get; set; } = 100f;

    public Marker3D Muzzle { get; private set; } = null!;
    public Marker3D SightEye { get; private set; } = null!;
    /// <summary>The magazine mesh on the weapon (hidden while no magazine is inserted). Optional.</summary>
    public Node3D? MagazineVisual { get; private set; }

    public override void _Ready()
    {
        Muzzle = GetNode<Marker3D>("Muzzle");
        SightEye = GetNode<Marker3D>("SightEye");
        MagazineVisual = GetNodeOrNull<Node3D>("Model/Magazine");
    }
}
