using System;
using Godot;

namespace ZombieSandbox.AI;

public enum NoiseKind
{
    Gunshot,
    Footstep,
    Landing,
    Impact,
    Handling, // Reloading, dry fire, dropped magazines.
    Voice,
}

/// <summary>A sound in the world. Anything within <see cref="Radius"/> can hear it.</summary>
public readonly record struct NoiseEvent(Vector3 Position, float Radius, NoiseKind Kind, Node? Source);

/// <summary>
/// Global sound-event bus for AI hearing. Anything that makes a sound calls <see cref="Emit"/>;
/// every listener (zombie, NPC) gets the event and decides whether it heard it.
/// </summary>
public static class GameNoise
{
    public static event Action<NoiseEvent>? Emitted;

    // Typical loudness radii (m). Tunable in one place.
    public const float RifleShot = 250f;
    public const float Sprinting = 12f;
    public const float Running = 7f;
    public const float Walking = 5f;
    public const float CrouchWalking = 2f;
    public const float Prone = 1f;
    public const float JumpLanding = 6f;
    public const float BulletImpact = 12f;
    public const float Reload = 4f;
    public const float DryFire = 3f;
    public const float MagazineDrop = 5f;

    public static void Emit(Vector3 position, float radius, NoiseKind kind, Node? source = null) =>
        Emitted?.Invoke(new NoiseEvent(position, radius, kind, source));
}
