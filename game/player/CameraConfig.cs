using Godot;

namespace ZombieSandbox.Player;

[GlobalClass]
public partial class CameraConfig : Resource
{
    [Export] public float FieldOfView { get; set; } = 75f;
    [Export] public float PitchMin { get; set; } = -80f;
    [Export] public float PitchMax { get; set; } = 80f;

    [ExportGroup("Third person")]
    [Export] public float ThirdPersonDistance { get; set; } = 2.2f;
    [Export] public float ShoulderOffset { get; set; } = 0.45f;
    [Export] public float ThirdPersonHeightOffset { get; set; } = 0.15f;
    /// <summary>How fast the camera slides to the other shoulder (shoulder widths per second).</summary>
    [Export] public float ShoulderSwapSpeed { get; set; } = 7f;

    [ExportGroup("First person")]
    /// <summary>Camera sits this far in front of the body's center, at eye height.</summary>
    [Export] public float FirstPersonForwardOffset { get; set; } = 0.1f;
    /// <summary>Camera roll at full lean, first person only (degrees).</summary>
    [Export] public float FirstPersonLeanRoll { get; set; } = 12f;
}
