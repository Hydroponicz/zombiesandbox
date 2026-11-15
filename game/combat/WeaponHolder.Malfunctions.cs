using System;
using Godot;
using ZombieSandbox.Character;
using ZombieSandbox.Core.Audio;

namespace ZombieSandbox.Combat;

public enum Malfunction
{
    None,
    /// <summary>Dud round: the trigger clicks, nothing fires. Rack to eject it (the round is lost).</summary>
    Misfire,
    /// <summary>The spent casing didn't leave the action. Rack to clear.</summary>
    FailureToEject,
    /// <summary>The next round didn't make it into the chamber. Rack to feed it.</summary>
    FailureToFeed,
    /// <summary>Two rounds jammed together. Magazine out, rack several times, magazine back in.</summary>
    DoubleFeed,
}

/// <summary>Weapon condition, heat, jams, inspecting and clearing.</summary>
public partial class WeaponHolder
{
    private enum MalfunctionAction
    {
        None,
        /// <summary>Look the weapon over: condition and (if jammed) the jam type.</summary>
        Inspect,
        /// <summary>Blind rack: clears the simple jams, not a double feed.</summary>
        TapRack,
        /// <summary>Inspect, then do the right fix (hold reload while jammed).</summary>
        InspectAndClear,
        /// <summary>The jam type is already known: do the right fix.</summary>
        CorrectFix,
    }

    public Malfunction Malfunction { get; private set; } = Malfunction.None;
    /// <summary>The player has inspected the weapon and knows what kind of jam it is.</summary>
    public bool MalfunctionKnown { get; private set; }
    public bool IsJammed => Malfunction != Malfunction.None;
    /// <summary>Builds up with sustained fire and cools off over time; raises the jam chance.</summary>
    public float Heat { get; private set; }
    public bool IsHandlingMalfunction => _malfunctionAction != MalfunctionAction.None;
    public string MalfunctionActionName => _malfunctionAction.ToString();
    public float MalfunctionActionProgress => _actionDuration > 0f ? _actionTime / _actionDuration : 0f;

    private MalfunctionAction _malfunctionAction = MalfunctionAction.None;
    private float _actionTime;
    private float _actionDuration;
    private float _inspectPart;
    private bool _inspectReported;
    private bool _forceJamNextShot;
    private Malfunction? _forcedJamType;

    /// <summary>
    /// Deal with a jam via the reload button. Returns false if the weapon isn't jammed (so the
    /// caller reloads instead). <paramref name="inspectFirst"/> = the hold-reload combo.
    /// </summary>
    public bool RequestClearMalfunction(bool inspectFirst)
    {
        if (!IsJammed || Weapon == null)
            return false;
        if (IsBusy)
            return true;

        WeaponData data = Weapon.Data;
        float multiplier = _stats.MaintenanceTimeMultiplier;
        if (inspectFirst && !MalfunctionKnown)
            StartMalfunctionAction(MalfunctionAction.InspectAndClear, (data.InspectTime + FixTime(data)) * multiplier, data.InspectTime * multiplier);
        else if (MalfunctionKnown)
            StartMalfunctionAction(MalfunctionAction.CorrectFix, FixTime(data) * multiplier, 0f);
        else
            StartMalfunctionAction(MalfunctionAction.TapRack, data.TapRackTime * multiplier, 0f);
        return true;
    }

    /// <summary>Look the weapon over (hold the check-magazine button).</summary>
    public void RequestInspect()
    {
        if (Weapon == null || IsBusy)
            return;
        float duration = Weapon.Data.InspectTime * _stats.MaintenanceTimeMultiplier;
        StartMalfunctionAction(MalfunctionAction.Inspect, duration, duration);
    }

    private float FixTime(WeaponData data) =>
        Malfunction == Malfunction.DoubleFeed ? data.DoubleFeedFixTime : data.TapRackTime;

    private void StartMalfunctionAction(MalfunctionAction action, float duration, float inspectPart)
    {
        CancelMagazineCheck();
        _malfunctionAction = action;
        _actionTime = 0f;
        _actionDuration = duration;
        _inspectPart = inspectPart;
        _inspectReported = false;
    }

    private void UpdateMalfunctionHandling(float dt)
    {
        Heat = Mathf.Max(0f, Heat - Config.HeatCoolingPerSecond * dt);
        if (_malfunctionAction == MalfunctionAction.None)
            return;

        if (Lowered)
        {
            _malfunctionAction = MalfunctionAction.None; // Sprinting interrupts; nothing changed.
            UpdateMagazineVisual();
            return;
        }

        _actionTime += dt;

        bool inspects = _malfunctionAction is MalfunctionAction.Inspect or MalfunctionAction.InspectAndClear;
        if (inspects && !_inspectReported && _actionTime >= _inspectPart)
        {
            _inspectReported = true;
            MalfunctionKnown = IsJammed;
            Message?.Invoke(InspectionReport());
            _skills.AddXp(SkillId.Maintenance, Config.MaintenanceXpPerInspect);
        }

        // Clearing a double feed takes the magazine out for the middle of the procedure.
        bool fixingDoubleFeed = Malfunction == Malfunction.DoubleFeed
            && _malfunctionAction is MalfunctionAction.CorrectFix or MalfunctionAction.InspectAndClear;
        if (fixingDoubleFeed && Weapon?.MagazineVisual != null)
        {
            float fixProgress = Mathf.InverseLerp(_inspectPart, _actionDuration, _actionTime);
            Weapon.MagazineVisual.Visible = Magazine != null && (fixProgress < 0.2f || fixProgress > 0.8f);
        }

        if (_actionTime < _actionDuration)
            return;

        MalfunctionAction finished = _malfunctionAction;
        _malfunctionAction = MalfunctionAction.None;
        switch (finished)
        {
            case MalfunctionAction.TapRack:
                if (Malfunction == Malfunction.DoubleFeed)
                {
                    PlayHandling(PlaceholderSounds.ChargingHandle);
                    Message?.Invoke("Still jammed. Something's badly stuck: inspect it.");
                }
                else
                {
                    FixMalfunction();
                }
                break;
            case MalfunctionAction.InspectAndClear:
            case MalfunctionAction.CorrectFix:
                FixMalfunction();
                break;
        }
        UpdateMagazineVisual();
    }

    private void FixMalfunction()
    {
        if (Malfunction == Malfunction.Misfire)
            Chambered = false; // The dud is ejected and lost.
        Malfunction = Malfunction.None;
        MalfunctionKnown = false;
        if (!Chambered)
            ChamberFromMagazine();
        PlayHandling(PlaceholderSounds.ChargingHandle);
        _skills.AddXp(SkillId.Maintenance, Config.MaintenanceXpPerClear);
    }

    private string InspectionReport()
    {
        string condition = ConditionText(Weapon!.Condition);
        string jam = Malfunction switch
        {
            Malfunction.Misfire => "Misfire: a dud round in the chamber.",
            Malfunction.FailureToEject => "Failure to eject: a spent casing is stuck in the action.",
            Malfunction.FailureToFeed => "Failure to feed: the next round didn't chamber.",
            Malfunction.DoubleFeed => "Double feed: two rounds jammed together.",
            _ => "No malfunction.",
        };
        return $"Weapon is {condition}. {jam}";
    }

    public static string ConditionText(float condition) =>
        condition >= 90f ? "like new"
        : condition >= 70f ? "in good condition"
        : condition >= 50f ? "well used"
        : condition >= 30f ? "worn"
        : condition >= 10f ? "badly worn"
        : "falling apart";

    // ---------- Jams ----------

    /// <summary>Average rounds between jams at this condition (log-linear between the weapon's points).</summary>
    public static float RoundsPerJam(WeaponData data, float condition)
    {
        Span<(float Condition, float Rounds)> points = stackalloc (float, float)[]
        {
            (0f, data.RoundsPerJamAt0),
            (30f, data.RoundsPerJamAt30),
            (70f, data.RoundsPerJamAt70),
            (80f, data.RoundsPerJamAt80),
            (100f, data.RoundsPerJamAt100),
        };
        condition = Mathf.Clamp(condition, 0f, 100f);
        for (int i = 1; i < points.Length; i++)
        {
            if (condition <= points[i].Condition)
            {
                float t = Mathf.InverseLerp(points[i - 1].Condition, points[i].Condition, condition);
                return Mathf.Exp(Mathf.Lerp(Mathf.Log(points[i - 1].Rounds), Mathf.Log(points[i].Rounds), t));
            }
        }
        return data.RoundsPerJamAt100;
    }

    /// <summary>Decide whether this shot goes wrong, and how.</summary>
    internal Malfunction RollMalfunction(WeaponData data)
    {
        bool jam;
        if (_forceJamNextShot)
        {
            _forceJamNextShot = false;
            if (_forcedJamType is { } forced)
            {
                _forcedJamType = null;
                return forced;
            }
            jam = true;
        }
        else
        {
            float heatMultiplier = Mathf.Min(1f + Heat / Config.JamHeatScale, Config.JamHeatMaxMultiplier);
            jam = _rng.Randf() < heatMultiplier / RoundsPerJam(data, Weapon!.Condition);
        }
        if (!jam)
            return Malfunction.None;

        // Double feeds get more common as the weapon wears out.
        float doubleFeed = 0.1f + 0.1f * (1f - Weapon!.Condition / 100f);
        float roll = _rng.Randf() * (0.9f + doubleFeed);
        return roll < 0.35f ? Malfunction.Misfire
            : roll < 0.65f ? Malfunction.FailureToEject
            : roll < 0.9f ? Malfunction.FailureToFeed
            : Malfunction.DoubleFeed;
    }

    /// <summary>Wear and heat from one shot.</summary>
    private void ApplyWear(WeaponData data)
    {
        float heatWear = Mathf.Lerp(1f, Config.WearAtMaxHeatMultiplier, Heat / Config.MaxHeat);
        Weapon!.Condition = Mathf.Max(0f, Weapon.Condition - data.WearPerShot * _stats.WearMultiplier * heatWear);
        Heat = Mathf.Min(Heat + Config.HeatPerShot, Config.MaxHeat);
    }

    // ---------- Developer tools ----------

    /// <summary>Developer tool: the next shot jams (a random type, or the given one).</summary>
    public void DebugForceJam(Malfunction? type = null)
    {
        _forceJamNextShot = true;
        _forcedJamType = type;
    }

    public void DebugCycleCondition()
    {
        if (Weapon == null)
            return;
        float[] steps = { 100f, 70f, 40f, 15f, 0f };
        int next = 0;
        for (int i = 0; i < steps.Length; i++)
        {
            if (Weapon.Condition > steps[i] - 0.5f)
            {
                next = (i + 1) % steps.Length;
                break;
            }
        }
        Weapon.Condition = steps[next];
    }
}
