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

    [ExportGroup("Over-the-shoulder aiming")]
    [Export] public float ShoulderAimDistance { get; set; } = 1.0f;
    [Export] public float ShoulderAimOffset { get; set; } = 0.55f;
    [Export] public float ShoulderAimFieldOfView { get; set; } = 55f;

    [ExportGroup("Camera shake (per shot, at 100% setting)")]
    [Export] public float ShakePitchDegrees { get; set; } = 0.4f;
    [Export] public float ShakeYawDegrees { get; set; } = 0.25f;
    [Export] public float ShakeRollDegrees { get; set; } = 1.0f;

    [ExportGroup("Aim assist (gamepad)")]
    [Export] public float AimAssistRangeHip { get; set; } = 40f;
    [Export] public float AimAssistRangeAimed { get; set; } = 150f;
    /// <summary>Extra radius around a target (m) inside which assist starts working.</summary>
    [Export] public float AimAssistPadding { get; set; } = 0.6f;
    /// <summary>Never smaller than this cone (degrees), so far targets still get some help.</summary>
    [Export] public float AimAssistMinConeDegrees { get; set; } = 2f;
    /// <summary>How much look speed is removed when aiming right at a target (0..1), hip / aimed.</summary>
    [Export] public float AimAssistSlowdownHip { get; set; } = 0.35f;
    [Export] public float AimAssistSlowdownAimed { get; set; } = 0.5f;
    /// <summary>Fraction of a target's movement (relative to you) the camera follows, hip / aimed.</summary>
    [Export] public float AimAssistTrackingHip { get; set; } = 0.4f;
    [Export] public float AimAssistTrackingAimed { get; set; } = 0.65f;

    [ExportGroup("First person")]
    /// <summary>Camera sits this far in front of the body's center, at eye height.</summary>
    [Export] public float FirstPersonForwardOffset { get; set; } = 0.1f;
    /// <summary>Camera roll at full lean, first person only (degrees).</summary>
    [Export] public float FirstPersonLeanRoll { get; set; } = 12f;
    /// <summary>The first-person camera is kept this far from walls so the near plane never cuts into them.</summary>
    [Export] public float FirstPersonCollisionRadius { get; set; } = 0.12f;
}
