using Godot;
using ZombieSandbox.Character;
using ZombieSandbox.Health;
using ZombieSandbox.Player;

namespace ZombieSandbox.UI;

/// <summary>
/// Body diagram (toggle with H / gamepad View). The game keeps running while it's open.
/// What it says depends on the Medical skill (see <see cref="HealthEstimate"/>).
/// </summary>
public partial class HealthScreen : Control
{
    private const float PanelWidth = 560f;
    private const float PanelHeight = 470f;

    private PlayerController _player = null!;
    private Font _font = null!;

    public override void _Ready()
    {
        _player = (PlayerController)Owner;
        MouseFilter = MouseFilterEnum.Ignore;
        _font = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta)
    {
        Visible = _player.HealthScreenOpen && !_player.IsDead;
        if (Visible)
            QueueRedraw();
    }

    public override void _Draw()
    {
        BodyHealth health = _player.Health;
        int medical = _player.Skills.GetLevel(SkillId.Medical);
        Vector2 size = GetViewportRect().Size;
        var panel = new Rect2(size.X - PanelWidth - 40f, size.Y * 0.14f, PanelWidth, PanelHeight);
        DrawRect(panel, new Color(0f, 0f, 0f, 0.72f));
        DrawRect(panel, new Color(1f, 1f, 1f, 0.25f), filled: false, width: 1.5f);

        DrawString(_font, panel.Position + new Vector2(20f, 34f), "HEALTH", HorizontalAlignment.Left, -1, 22, Colors.White);
        DrawString(_font, panel.Position + new Vector2(120f, 34f), $"(Medical {medical})", HorizontalAlignment.Left, -1, 15, new Color(1f, 1f, 1f, 0.6f));

        // Diagram, seen from behind (left is on the left).
        Vector2 o = panel.Position + new Vector2(110f, 60f);
        DrawPart(health, BodyPart.Head, medical, new Rect2(o + new Vector2(-24f, 0f), new Vector2(48f, 52f)));
        DrawPart(health, BodyPart.Chest, medical, new Rect2(o + new Vector2(-45f, 58f), new Vector2(90f, 80f)));
        DrawPart(health, BodyPart.Stomach, medical, new Rect2(o + new Vector2(-40f, 142f), new Vector2(80f, 56f)));
        DrawPart(health, BodyPart.LeftArm, medical, new Rect2(o + new Vector2(-76f, 58f), new Vector2(27f, 135f)));
        DrawPart(health, BodyPart.RightArm, medical, new Rect2(o + new Vector2(49f, 58f), new Vector2(27f, 135f)));
        DrawPart(health, BodyPart.LeftLeg, medical, new Rect2(o + new Vector2(-40f, 202f), new Vector2(37f, 160f)));
        DrawPart(health, BodyPart.RightLeg, medical, new Rect2(o + new Vector2(3f, 202f), new Vector2(37f, 160f)));

        // List: name, estimate, bleeding / fracture.
        Vector2 line = panel.Position + new Vector2(230f, 70f);
        foreach (BodyPart part in BodyParts.All)
        {
            (string estimate, Color color) = HealthEstimate.Describe(health, part, medical);
            DrawString(_font, line, BodyParts.Name(part), HorizontalAlignment.Left, -1, 16, Colors.White);
            DrawString(_font, line + new Vector2(100f, 0f), estimate, HorizontalAlignment.Left, -1, 16, color);

            string bleeding = HealthEstimate.Bleeding(health.Bleeding(part), medical);
            string status = (bleeding + (health.IsFractured(part) ? (bleeding != "" ? ", " : "") + "Broken" : "")).Trim();
            string wounds = WoundSummary(health, part);
            if (wounds != "")
                status = status == "" ? wounds : status + ", " + wounds;
            if (status != "")
                DrawString(_font, line + new Vector2(100f, 19f), status, HorizontalAlignment.Left, -1, 14, new Color(1f, 0.45f, 0.4f));
            line += new Vector2(0f, 50f);
        }

        DrawString(_font, panel.Position + new Vector2(20f, PanelHeight - 18f), $"Pain: {HealthEstimate.Pain(health.Pain)}",
            HorizontalAlignment.Left, -1, 16, Colors.White);
        string symptoms = _player.Infection.SymptomsText;
        if (symptoms != "")
            DrawString(_font, panel.Position + new Vector2(230f, PanelHeight - 18f), symptoms, HorizontalAlignment.Left, -1, 16, new Color(0.95f, 0.75f, 0.35f));
    }

    /// <summary>Wounds on a part, e.g. "Bite, 2 Scratches".</summary>
    private static string WoundSummary(BodyHealth health, BodyPart part)
    {
        int scratches = 0, cuts = 0, bites = 0, bullets = 0;
        foreach (Wound wound in health.Wounds)
        {
            if (wound.Part != part)
                continue;
            switch (wound.Type)
            {
                case WoundType.Scratch: scratches++; break;
                case WoundType.Laceration: cuts++; break;
                case WoundType.Bite: bites++; break;
                case WoundType.Bullet: bullets++; break;
            }
        }
        var parts = new System.Collections.Generic.List<string>();
        if (bites > 0) parts.Add(bites == 1 ? "Bite" : $"{bites} Bites");
        if (cuts > 0) parts.Add(cuts == 1 ? "Laceration" : $"{cuts} Lacerations");
        if (scratches > 0) parts.Add(scratches == 1 ? "Scratch" : $"{scratches} Scratches");
        if (bullets > 0) parts.Add(bullets == 1 ? "Gunshot wound" : $"{bullets} Gunshot wounds");
        return string.Join(", ", parts);
    }

    private void DrawPart(BodyHealth health, BodyPart part, int medical, Rect2 rect)
    {
        (_, Color color) = HealthEstimate.Describe(health, part, medical);
        DrawRect(rect, new Color(color, 0.85f));
        if (health.Bleeding(part) != BleedLevel.None)
            DrawRect(rect, new Color(0.9f, 0f, 0f), filled: false, width: 3f);
        if (health.IsFractured(part))
            DrawLine(rect.Position + new Vector2(rect.Size.X, 0f), rect.Position + new Vector2(0f, rect.Size.Y), Colors.White, 2f);
    }
}
