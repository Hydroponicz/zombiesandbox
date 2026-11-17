using Godot;
using ZombieSandbox.Core.Input;
using ZombieSandbox.Player;

namespace ZombieSandbox.UI;

/// <summary>
/// The player's (deliberately minimal) on-screen text: the interaction prompt and short messages
/// such as magazine-check results. No ammo counter, no crosshair.
/// </summary>
public partial class PlayerHud : Control
{
    private const float MessageSeconds = 2.5f;

    private PlayerController _player = null!;
    private Label _prompt = null!;
    private Label _message = null!;
    private float _messageLeft;
    private bool _subscribed;

    public override void _Ready()
    {
        _player = (PlayerController)Owner;
        MouseFilter = MouseFilterEnum.Ignore;
        _prompt = CreateLabel(0.62f, 20);
        _message = CreateLabel(0.7f, 20);
    }

    private Label CreateLabel(float heightFraction, int fontSize)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = heightFraction,
            AnchorBottom = heightFraction,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        label.AddThemeConstantOverride("outline_size", 6);
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        // The player wires up its weapon after this HUD is ready, so subscribe on first use.
        if (!_subscribed && _player.Weapons != null)
        {
            _player.Weapons.Message += ShowMessage;
            _player.Message += ShowMessage;
            _subscribed = true;
        }

        if (_player.InteractTarget is { } target)
        {
            string key = GameInput.Instance.LastDevice == InputDevice.Gamepad ? "X" : "F";
            _prompt.Text = $"[{key}] {target.InteractPrompt}";
        }
        else
        {
            _prompt.Text = "";
        }

        if (_messageLeft > 0f)
        {
            _messageLeft -= (float)delta;
            _message.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_messageLeft / 0.5f, 0f, 1f));
            if (_messageLeft <= 0f)
                _message.Text = "";
        }
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _messageLeft = MessageSeconds;
    }

    public override void _ExitTree()
    {
        if (!_subscribed)
            return;
        _player.Weapons.Message -= ShowMessage;
        _player.Message -= ShowMessage;
    }
}
