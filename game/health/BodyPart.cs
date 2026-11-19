namespace ZombieSandbox.Health;

public enum BodyPart
{
    Head,
    Chest,
    Stomach,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
}

public enum BleedLevel
{
    None,
    Light,
    Heavy,
}

/// <summary>What caused damage. Wound types (scratch, laceration, bite) arrive with zombies (step 4).</summary>
public enum DamageSource
{
    Bullet,
    Fall,
    Bleeding,
    Infection,
    ZombieAttack,
    Shove,
    Debug,
    Melee,
}

public static class BodyParts
{
    public const int Count = 7;

    public static readonly BodyPart[] All =
    {
        BodyPart.Head, BodyPart.Chest, BodyPart.Stomach,
        BodyPart.LeftArm, BodyPart.RightArm, BodyPart.LeftLeg, BodyPart.RightLeg,
    };

    public static bool IsVital(BodyPart part) => part is BodyPart.Head or BodyPart.Chest;
    public static bool IsLimb(BodyPart part) => part is BodyPart.LeftArm or BodyPart.RightArm or BodyPart.LeftLeg or BodyPart.RightLeg;
    public static bool IsLeg(BodyPart part) => part is BodyPart.LeftLeg or BodyPart.RightLeg;
    public static bool IsArm(BodyPart part) => part is BodyPart.LeftArm or BodyPart.RightArm;

    public static string Name(BodyPart part) => part switch
    {
        BodyPart.LeftArm => "Left arm",
        BodyPart.RightArm => "Right arm",
        BodyPart.LeftLeg => "Left leg",
        BodyPart.RightLeg => "Right leg",
        _ => part.ToString(),
    };
}
