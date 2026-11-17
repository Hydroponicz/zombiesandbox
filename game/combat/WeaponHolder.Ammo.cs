using System;
using Godot;
using ZombieSandbox.AI;
using ZombieSandbox.Character;
using ZombieSandbox.Core.Audio;

namespace ZombieSandbox.Combat;

public enum ReloadKind
{
    None,
    /// <summary>Old magazine goes back into the pouch.</summary>
    Tactical,
    /// <summary>Old magazine is dropped on the ground. Faster.</summary>
    Fast,
    /// <summary>A magazine is in but nothing is chambered: just rack the charging handle.</summary>
    ChargeOnly,
}

/// <summary>Magazines, the chamber, reloading and magazine checks.</summary>
public partial class WeaponHolder
{
    // Reload timeline, as fractions of the magazine-swap part of the reload.
    private const float MagazineOutAt = 0.3f;
    private const float MagazineInAt = 0.7f;

    /// <summary>The magazine inserted in the weapon (null = none).</summary>
    public MagazineItem? Magazine { get; private set; }
    /// <summary>A round is in the chamber, ready to fire.</summary>
    public bool Chambered { get; private set; }
    public IMagazineStorage? Pouch { get; private set; }

    public ReloadKind Reloading { get; private set; } = ReloadKind.None;
    public float ReloadProgress => _reloadDuration > 0f ? _reloadTime / _reloadDuration : 0f;
    public bool IsCheckingMagazine { get; private set; }
    /// <summary>Hands are busy (reloading or checking the magazine): can't aim or fire.</summary>
    public bool IsBusy => Reloading != ReloadKind.None || IsCheckingMagazine || IsHandlingMalfunction;

    /// <summary>Short text for the player (magazine check result, "no magazines", ...).</summary>
    public event Action<string>? Message;

    private float _reloadTime;
    private float _reloadDuration;
    private float _swapDuration;
    private bool _magazineOutDone;
    private bool _magazineInDone;
    private MagazineItem? _incomingMagazine;
    private float _checkTime;
    private float _checkDuration;
    private float _busyBlend;
    private AudioStreamPlayer3D _handlingAudio = null!;

    private void CreateHandlingAudio()
    {
        _handlingAudio = new AudioStreamPlayer3D { MaxPolyphony = 4, UnitSize = 3f, MaxDistance = 40f };
        AddChild(_handlingAudio);
    }

    private void PlayHandling(AudioStream sound)
    {
        if (Weapon == null)
            return;
        _handlingAudio.GlobalPosition = Weapon.GlobalPosition;
        _handlingAudio.Stream = sound;
        _handlingAudio.Play();
        float loudness = sound == PlaceholderSounds.DryClick ? GameNoise.DryFire : GameNoise.Reload;
        GameNoise.Emit(Weapon.GlobalPosition, loudness, NoiseKind.Handling, GetParent());
    }

    /// <summary>Fresh weapon: a full magazine inserted and a round chambered from it.</summary>
    private void LoadStartingMagazine(WeaponData data)
    {
        if (data.Ammo == null)
            return;
        Magazine = new MagazineItem(data.MagazineCapacity, data.Ammo, data.MagazineCapacity);
        ChamberFromMagazine();
        UpdateMagazineVisual();
    }

    /// <summary>Starting loadout: spare full magazines in the pouch.</summary>
    public void GiveSpareMagazines(int count)
    {
        if (Weapon?.Data.Ammo == null || Pouch == null)
            return;
        for (int i = 0; i < count; i++)
            Pouch.Add(new MagazineItem(Weapon.Data.MagazineCapacity, Weapon.Data.Ammo, Weapon.Data.MagazineCapacity));
    }

    private bool ChamberFromMagazine()
    {
        if (Magazine == null || Magazine.IsEmpty)
            return false;
        Magazine.Rounds--;
        Chambered = true;
        return true;
    }

    /// <summary>Called after each shot: the action cycles the next round in, if there is one.</summary>
    private void CycleAfterShot()
    {
        Chambered = false;
        ChamberFromMagazine();
    }

    private void UpdateMagazineVisual()
    {
        if (Weapon?.MagazineVisual != null)
            Weapon.MagazineVisual.Visible = Magazine != null;
    }

    // ---------- Reloading ----------

    /// <summary>
    /// Start a reload. A fast request during a tactical reload (before the old magazine is out)
    /// turns it into a fast reload.
    /// </summary>
    public void RequestReload(bool fast)
    {
        if (Weapon == null || Pouch == null)
            return;
        WeaponData data = Weapon.Data;

        if (Reloading != ReloadKind.None)
        {
            if (fast && Reloading == ReloadKind.Tactical && !_magazineOutDone)
                SwitchToFastReload(data);
            return;
        }

        CancelMagazineCheck();

        if (Pouch.FullestRounds() <= 0)
        {
            // No loaded spares, but a loaded magazine with nothing chambered: just rack it.
            if (Magazine is { IsEmpty: false } && !Chambered)
                StartReload(ReloadKind.ChargeOnly, data);
            else
                Message?.Invoke(Pouch.Magazines.Count == 0 ? "No spare magazines" : "No loaded magazines");
            return;
        }

        StartReload(fast ? ReloadKind.Fast : ReloadKind.Tactical, data);
    }

    private void StartReload(ReloadKind kind, WeaponData data)
    {
        float multiplier = _stats.ReloadTimeMultiplier;
        Reloading = kind;
        _reloadTime = 0f;

        if (kind == ReloadKind.ChargeOnly)
        {
            _swapDuration = 0f;
            _reloadDuration = data.ChargingHandleTime * multiplier;
            _magazineOutDone = _magazineInDone = true;
            return;
        }

        // Reserve the fullest magazine now, so the old one going back into the pouch can't be picked again.
        _incomingMagazine = Pouch!.TakeFullest();
        _swapDuration = (kind == ReloadKind.Fast ? data.FastReloadTime : data.TacticalReloadTime) * multiplier;
        bool willNeedCharging = !Chambered;
        _reloadDuration = _swapDuration + (willNeedCharging ? data.ChargingHandleTime * multiplier : 0f);
        _magazineOutDone = false;
        _magazineInDone = false;

        // Nothing to take out: skip straight to inserting.
        if (Magazine == null)
        {
            _reloadTime = _swapDuration * MagazineOutAt;
            _magazineOutDone = true;
        }
    }

    private void SwitchToFastReload(WeaponData data)
    {
        float progress = _reloadTime / _swapDuration;
        float charging = _reloadDuration - _swapDuration;
        Reloading = ReloadKind.Fast;
        _swapDuration = data.FastReloadTime * _stats.ReloadTimeMultiplier;
        _reloadDuration = _swapDuration + charging;
        _reloadTime = progress * _swapDuration;
    }

    private void UpdateReload(float dt)
    {
        if (Reloading == ReloadKind.None)
            return;

        // Sprinting (weapon lowered) interrupts the reload wherever it got to.
        if (Lowered)
        {
            CancelReload();
            return;
        }

        _reloadTime += dt;

        if (!_magazineOutDone && _reloadTime >= _swapDuration * MagazineOutAt)
        {
            _magazineOutDone = true;
            RemoveMagazine(drop: Reloading == ReloadKind.Fast);
        }

        if (!_magazineInDone && _reloadTime >= _swapDuration * MagazineInAt)
        {
            _magazineInDone = true;
            Magazine = _incomingMagazine;
            _incomingMagazine = null;
            UpdateMagazineVisual();
            PlayHandling(PlaceholderSounds.MagazineIn);
        }

        if (_reloadTime >= _reloadDuration)
        {
            if (!Chambered && ChamberFromMagazine())
                PlayHandling(PlaceholderSounds.ChargingHandle);
            _skills.AddXp(SkillId.Reloading, Config.ReloadingXpPerReload);
            Reloading = ReloadKind.None;
        }
    }

    private void RemoveMagazine(bool drop)
    {
        MagazineItem? old = Magazine;
        Magazine = null;
        UpdateMagazineVisual();
        PlayHandling(PlaceholderSounds.MagazineOut);
        if (old == null)
            return;
        if (drop || !Pouch!.Add(old))
            DropMagazine(old);
    }

    /// <summary>Stop a reload midway. Whatever already happened stays happened.</summary>
    public void CancelReload()
    {
        if (Reloading == ReloadKind.None)
            return;
        if (_incomingMagazine != null && !Pouch!.Add(_incomingMagazine))
            DropMagazine(_incomingMagazine);
        _incomingMagazine = null;
        Reloading = ReloadKind.None;
    }

    private void DropMagazine(MagazineItem magazine)
    {
        PackedScene? scene = Weapon?.Data.DroppedMagazineScene;
        if (scene == null || Weapon == null)
            return;
        var dropped = scene.Instantiate<DroppedMagazine>();
        dropped.Magazine = magazine;
        GetTree().CurrentScene.AddChild(dropped);
        Transform3D from = Weapon.MagazineVisual?.GlobalTransform ?? Weapon.GlobalTransform;
        dropped.GlobalTransform = from;
        dropped.LinearVelocity = -from.Basis.Y.Normalized() * 1.2f + new Vector3(0f, -0.5f, 0f);
        GameNoise.Emit(from.Origin, GameNoise.MagazineDrop, NoiseKind.Handling, GetParent());
        dropped.AngularVelocity = new Vector3(_rng.RandfRange(-4f, 4f), _rng.RandfRange(-4f, 4f), _rng.RandfRange(-4f, 4f));
    }

    // ---------- Magazine check ----------

    public void RequestMagazineCheck()
    {
        if (Weapon == null || IsBusy)
            return;
        if (Magazine == null)
        {
            Message?.Invoke("No magazine in the weapon");
            return;
        }
        IsCheckingMagazine = true;
        _checkTime = 0f;
        _checkDuration = Weapon.Data.MagazineCheckTime * _stats.ReloadTimeMultiplier;
    }

    private void CancelMagazineCheck() => IsCheckingMagazine = false;

    private void UpdateMagazineCheck(float dt)
    {
        if (!IsCheckingMagazine)
            return;
        if (Lowered || Magazine == null)
        {
            CancelMagazineCheck();
            return;
        }
        _checkTime += dt;
        if (_checkTime < _checkDuration)
            return;

        IsCheckingMagazine = false;
        Message?.Invoke(DescribeMagazine(Magazine, _skills.GetLevel(SkillId.Reloading)));
        _skills.AddXp(SkillId.Reloading, Config.ReloadingXpPerMagazineCheck);
    }

    /// <summary>What the character can tell about a magazine by feel, depending on Reloading skill.</summary>
    public static string DescribeMagazine(MagazineItem magazine, int reloadingLevel)
    {
        int rounds = magazine.Rounds;
        float fill = magazine.Fill;
        if (rounds == 0)
            return "Magazine is empty";

        if (reloadingLevel <= 2)
        {
            return fill >= 0.7f ? "Magazine feels full, or close to it"
                : fill >= 0.3f ? "Magazine feels about half full"
                : "Magazine feels nearly empty";
        }

        if (reloadingLevel <= 6)
        {
            return fill >= 1f ? "Magazine is full"
                : fill >= 0.75f ? "Magazine is nearly full"
                : fill >= 0.4f ? "Magazine is about half full"
                : fill >= 0.15f ? "Magazine is less than half full"
                : "Magazine is almost empty";
        }

        if (reloadingLevel <= 9)
        {
            if (fill >= 1f)
                return "Magazine is full";
            int roughly = Mathf.Max(5, Mathf.RoundToInt(rounds / 5f) * 5);
            return rounds < 5 ? "Fewer than 5 rounds" : $"About {roughly} rounds";
        }

        return rounds == magazine.Capacity ? $"Magazine is full ({rounds} rounds)" : $"{rounds} rounds";
    }

    /// <summary>Developer tool: full magazine in the gun, chambered, and the pouch topped up.</summary>
    public void DebugRefill(int spareMagazines)
    {
        if (Weapon?.Data.Ammo == null || Pouch == null)
            return;
        CancelReload();
        CancelMagazineCheck();
        Magazine = new MagazineItem(Weapon.Data.MagazineCapacity, Weapon.Data.Ammo, Weapon.Data.MagazineCapacity);
        Chambered = false;
        ChamberFromMagazine();
        UpdateMagazineVisual();
        foreach (MagazineItem magazine in Pouch.Magazines)
            magazine.Rounds = magazine.Capacity;
        while (Pouch.Magazines.Count < spareMagazines && Pouch.HasSpace)
            Pouch.Add(new MagazineItem(Weapon.Data.MagazineCapacity, Weapon.Data.Ammo, Weapon.Data.MagazineCapacity));
    }
}
