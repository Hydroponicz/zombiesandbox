using Godot;

namespace ZombieSandbox.Health;

/// <summary>
/// What a character can tell about their own injuries, depending on the Medical skill.
/// Low skill: rough words. Mid: finer words. High: approximate HP. Level 10: exact.
/// </summary>
public static class HealthEstimate
{
    public static (string Text, Color Color) Describe(BodyHealth health, BodyPart part, int medicalLevel)
    {
        if (health.IsDestroyed(part))
            return ("Destroyed", new Color(0.35f, 0.35f, 0.35f));

        float fraction = health.Fraction(part);
        float hp = health.Hp(part);
        float max = health.MaxHp(part);

        string text;
        if (medicalLevel <= 2)
        {
            text = fraction >= 0.95f ? "Fine" : fraction >= 0.6f ? "Hurt" : fraction >= 0.25f ? "Badly hurt" : "Critical";
        }
        else if (medicalLevel <= 5)
        {
            text = fraction >= 0.98f ? "Fine"
                : fraction >= 0.85f ? "Scratched"
                : fraction >= 0.65f ? "Hurt"
                : fraction >= 0.45f ? "Wounded"
                : fraction >= 0.25f ? "Badly wounded"
                : "Critical";
        }
        else if (medicalLevel <= 9)
        {
            float step = medicalLevel <= 8 ? 10f : 5f;
            float rounded = Mathf.Max(step / 2f, Mathf.Round(hp / step) * step);
            text = fraction >= 0.98f ? "Fine" : $"About {rounded:0} HP";
        }
        else
        {
            text = $"{Mathf.Ceil(hp):0} / {max:0} HP";
        }

        return (text, ColorFor(fraction));
    }

    public static string Bleeding(BleedLevel level, int medicalLevel) => level switch
    {
        BleedLevel.None => "",
        _ when medicalLevel <= 2 => "Bleeding",
        BleedLevel.Heavy => "Bleeding heavily",
        _ => "Bleeding lightly",
    };

    public static string Pain(float pain) =>
        pain < 0.05f ? "None" : pain < 0.3f ? "Mild" : pain < 0.6f ? "Moderate" : "Severe";

    private static Color ColorFor(float fraction) =>
        fraction >= 0.85f ? new Color(0.45f, 0.8f, 0.4f)
        : fraction >= 0.6f ? new Color(0.85f, 0.8f, 0.3f)
        : fraction >= 0.3f ? new Color(0.9f, 0.5f, 0.2f)
        : new Color(0.85f, 0.15f, 0.15f);
}
