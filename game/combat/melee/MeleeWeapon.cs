using Godot;

namespace ZombieSandbox.Combat;

/// <summary>
/// A melee weapon instance (bat, machete, ...). The model is built along +Y from the grip at the
/// origin. Condition wears with hits (melee piece B).
/// </summary>
public partial class MeleeWeapon : Node3D
{
    [Export] public MeleeData Data { get; set; } = null!;
    /// <summary>Condition in percent; the weapon breaks at 0.</summary>
    [Export(PropertyHint.Range, "0,100,0.1")] public float Condition { get; set; } = 100f;
}
