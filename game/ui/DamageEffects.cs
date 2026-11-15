using Godot;
using ZombieSandbox.Core.Audio;
using ZombieSandbox.Health;
using ZombieSandbox.Player;

namespace ZombieSandbox.UI;

/// <summary>
/// Injury feedback instead of a health bar: hit flash, red vignette that grows as health drops,
/// pulsing edges while bleeding, blur and desaturation with pain, heartbeat when in danger,
/// hurt and bone-crack sounds, and the death screen.
/// </summary>
public partial class DamageEffects : ColorRect
{
    private PlayerController _player = null!;
    private ShaderMaterial _material = null!;
    private AudioStreamPlayer _hurtAudio = null!;
    private AudioStreamPlayer _crackAudio = null!;
    private AudioStreamPlayer _heartAudio = null!;
    private Label _deathTitle = null!;
    private Label _deathHint = null!;
    private bool _subscribed;
    private float _flash;
    private float _heartTimer;
    private float _deathTime;
    private float _clock;

    public override void _Ready()
    {
        _player = (PlayerController)Owner;
        _material = (ShaderMaterial)Material;
        MouseFilter = MouseFilterEnum.Ignore;

        _hurtAudio = AddAudio(PlaceholderSounds.Hurt, -2f);
        _crackAudio = AddAudio(PlaceholderSounds.BoneCrack, 0f);
        _heartAudio = AddAudio(PlaceholderSounds.Heartbeat, 0f);

        _deathTitle = AddLabel("You died", 46, 0.42f);
        _deathHint = AddLabel("Press Enter / A to start a new character", 20, 0.52f);
    }

    private AudioStreamPlayer AddAudio(AudioStream stream, float volumeDb)
    {
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = volumeDb };
        AddChild(player);
        return player;
    }

    private Label AddLabel(string text, int size, float heightFraction)
    {
        var label = new Label
        {
            Text = text,
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorRight = 1f,
            AnchorTop = heightFraction,
            AnchorBottom = heightFraction,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.15f, 0.12f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 8);
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _clock += dt;
        BodyHealth health = _player.Health;
        if (health == null)
            return;
        if (!_subscribed)
        {
            health.Damaged += OnDamaged;
            health.Fractured += OnFractured;
            _subscribed = true;
        }

        _flash = Mathf.Max(0f, _flash - 2.5f * dt);
        float danger = health.Danger;

        float bleedPulse = 0f;
        if (health.IsBleeding)
        {
            float amplitude = health.IsBleedingHeavily ? 0.28f : 0.14f;
            bleedPulse = amplitude * (0.5f + 0.5f * Mathf.Sin(_clock * Mathf.Tau * 1.1f));
        }

        float vignette = Mathf.Clamp(0.15f + danger * 0.55f + _flash * 0.7f + bleedPulse, 0f, 1f);
        if (danger < 0.05f && _flash <= 0f && bleedPulse <= 0f)
            vignette = 0f;

        _material.SetShaderParameter("vignette", vignette);
        // Fever: a slow, sickly pulse of washed-out color.
        float fever = _player.Infection.Severity * (0.6f + 0.4f * Mathf.Sin(_clock * 0.8f));
        _material.SetShaderParameter("desaturate", Mathf.Clamp(health.Pain * 0.5f + danger * 0.35f + fever * 0.3f, 0f, 0.85f));
        // Blur only once pain is significant, and never so much that you can't aim.
        _material.SetShaderParameter("blur", Mathf.Max(0f, health.Pain - 0.3f) * 1.1f);

        UpdateHeartbeat(dt, danger, health.IsDead);
        UpdateDeath(dt, health.IsDead);
    }

    private void UpdateHeartbeat(float dt, float danger, bool dead)
    {
        if (dead || danger < 0.45f)
        {
            _heartTimer = 0f;
            return;
        }
        _heartTimer -= dt;
        if (_heartTimer > 0f)
            return;
        float urgency = Mathf.InverseLerp(0.45f, 1f, danger);
        _heartTimer = Mathf.Lerp(1.0f, 0.45f, urgency);
        _heartAudio.VolumeDb = Mathf.Lerp(-12f, 0f, urgency);
        _heartAudio.Play();
    }

    private void UpdateDeath(float dt, bool dead)
    {
        if (!dead)
        {
            _deathTime = 0f;
            _material.SetShaderParameter("darken", 0f);
            _deathTitle.Visible = _deathHint.Visible = false;
            return;
        }
        _deathTime += dt;
        _material.SetShaderParameter("darken", Mathf.Clamp(_deathTime / 2f, 0f, 0.85f));
        _deathTitle.Text = _player.Health.DeathCause;
        _deathTitle.Visible = _deathTime > 1f;
        _deathHint.Visible = _deathTime > 2f;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_player.Health is { IsDead: true } && _deathTime > 2f && e.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            GetTree().ReloadCurrentScene();
        }
    }

    private void OnDamaged(BodyPart part, float amount)
    {
        _flash = Mathf.Min(1f, _flash + amount / 35f);
        _hurtAudio.VolumeDb = Mathf.Lerp(-10f, 0f, Mathf.Clamp(amount / 40f, 0f, 1f));
        _hurtAudio.Play();
    }

    private void OnFractured(BodyPart part) => _crackAudio.Play();

    public override void _ExitTree()
    {
        if (_subscribed && _player.Health != null)
        {
            _player.Health.Damaged -= OnDamaged;
            _player.Health.Fractured -= OnFractured;
        }
    }
}
