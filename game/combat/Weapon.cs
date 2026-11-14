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

    public Marker3D Muzzle { get; private set; } = null!;
    public Marker3D SightEye { get; private set; } = null!;

    public override void _Ready()
    {
        Muzzle = GetNode<Marker3D>("Muzzle");
        SightEye = GetNode<Marker3D>("SightEye");
    }
}
