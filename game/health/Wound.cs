namespace ZombieSandbox.Health;

public enum WoundType
{
    Scratch,
    Laceration,
    Bite,
    Bullet,
}

/// <summary>An injury on a body part (treated with medical items later). Game hour = when it happened.</summary>
public readonly record struct Wound(BodyPart Part, WoundType Type, double GameHour);
