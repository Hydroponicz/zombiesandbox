using System;
using Godot;

namespace ZombieSandbox.Core.Audio;

/// <summary>
/// Sounds synthesized in code so the game isn't silent before real audio exists.
/// Replace each with an imported sound file later; nothing else needs to change.
/// </summary>
public static class PlaceholderSounds
{
    private const int MixRate = 44100;
    private static AudioStreamWav? _gunshot;
    private static AudioStreamWav? _impact;
    private static AudioStreamWav? _dryClick;
    private static AudioStreamWav? _magOut;
    private static AudioStreamWav? _magIn;
    private static AudioStreamWav? _charge;
    private static AudioStreamWav? _heartbeat;
    private static AudioStreamWav? _hurt;
    private static AudioStreamWav? _boneCrack;
    private static AudioStreamWav? _zombieGroan;
    private static AudioStreamWav? _zombieAttack;
    private static AudioStreamWav? _shoveThud;

    /// <summary>Sharp crack plus a low thump with a short noisy tail.</summary>
    public static AudioStreamWav Gunshot => _gunshot ??= Generate(0.35f, seed: 1, (t, noise) =>
    {
        float crack = noise * MathF.Exp(-t * 60f);
        float tail = noise * 0.35f * MathF.Exp(-t * 9f);
        float thump = MathF.Sin(2f * MathF.PI * 70f * t) * MathF.Exp(-t * 18f);
        return 0.9f * crack + tail + 0.8f * thump;
    });

    /// <summary>Short dull smack.</summary>
    public static AudioStreamWav Impact => _impact ??= Generate(0.08f, seed: 2, (t, noise) =>
        noise * MathF.Exp(-t * 70f) * 0.8f + MathF.Sin(2f * MathF.PI * 180f * t) * MathF.Exp(-t * 50f) * 0.5f);

    /// <summary>Hammer falling on an empty chamber.</summary>
    public static AudioStreamWav DryClick => _dryClick ??= Generate(0.05f, seed: 3, (t, noise) =>
        noise * MathF.Exp(-t * 160f) * 0.6f + MathF.Sin(2f * MathF.PI * 2400f * t) * MathF.Exp(-t * 120f) * 0.3f);

    public static AudioStreamWav MagazineOut => _magOut ??= Generate(0.12f, seed: 4, (t, noise) =>
        noise * MathF.Exp(-t * 60f) * 0.5f + MathF.Sin(2f * MathF.PI * 900f * t) * MathF.Exp(-t * 40f) * 0.35f);

    public static AudioStreamWav MagazineIn => _magIn ??= Generate(0.12f, seed: 5, (t, noise) =>
        noise * MathF.Exp(-t * 80f) * 0.6f + MathF.Sin(2f * MathF.PI * 600f * t) * MathF.Exp(-t * 45f) * 0.45f);

    /// <summary>Charging handle back and forward: two clacks.</summary>
    public static AudioStreamWav ChargingHandle => _charge ??= Generate(0.3f, seed: 6, (t, noise) =>
    {
        float second = t > 0.16f ? t - 0.16f : 10f;
        return noise * (MathF.Exp(-t * 70f) * 0.5f + MathF.Exp(-second * 70f) * 0.7f)
            + MathF.Sin(2f * MathF.PI * 700f * t) * MathF.Exp(-second * 50f) * 0.4f;
    });

    /// <summary>Lub-dub: two low thumps.</summary>
    public static AudioStreamWav Heartbeat => _heartbeat ??= Generate(0.45f, seed: 7, (t, noise) =>
    {
        float second = t > 0.2f ? t - 0.2f : 10f;
        float thump = MathF.Sin(2f * MathF.PI * 50f * t) * MathF.Exp(-t * 25f)
            + 0.7f * MathF.Sin(2f * MathF.PI * 45f * t) * MathF.Exp(-second * 25f);
        return thump * 0.9f;
    });

    /// <summary>Dull body hit.</summary>
    public static AudioStreamWav Hurt => _hurt ??= Generate(0.18f, seed: 8, (t, noise) =>
        noise * MathF.Exp(-t * 35f) * 0.4f + MathF.Sin(2f * MathF.PI * 90f * t) * MathF.Exp(-t * 20f) * 0.8f);

    /// <summary>Sharp crack for a broken bone.</summary>
    public static AudioStreamWav BoneCrack => _boneCrack ??= Generate(0.15f, seed: 9, (t, noise) =>
        noise * MathF.Exp(-t * 90f) * 0.9f + MathF.Sin(2f * MathF.PI * 1300f * t) * MathF.Exp(-t * 70f) * 0.3f);

    /// <summary>Low, wavering groan with a breathy rasp.</summary>
    public static AudioStreamWav ZombieGroan => _zombieGroan ??= Generate(1.4f, seed: 10, (t, noise) =>
    {
        float envelope = MathF.Sin(MathF.PI * MathF.Min(t / 1.4f, 1f)) * MathF.Exp(-t * 0.6f);
        float pitch = 82f + 10f * MathF.Sin(2f * MathF.PI * 3.3f * t) - 14f * t;
        float phase = 2f * MathF.PI * pitch * t;
        float voice = MathF.Sin(phase) * 0.6f + MathF.Sin(phase * 2f) * 0.3f + MathF.Sin(phase * 3.1f) * 0.15f;
        return (voice + noise * 0.25f) * envelope * 0.9f;
    });

    /// <summary>Short, harsh snarl as a zombie lunges.</summary>
    public static AudioStreamWav ZombieAttack => _zombieAttack ??= Generate(0.5f, seed: 11, (t, noise) =>
    {
        float envelope = MathF.Min(t * 20f, 1f) * MathF.Exp(-t * 5f);
        float phase = 2f * MathF.PI * (150f + 40f * MathF.Sin(2f * MathF.PI * 9f * t)) * t;
        return (MathF.Sin(phase) * 0.5f + MathF.Sin(phase * 2.3f) * 0.25f + noise * 0.45f) * envelope;
    });

    /// <summary>Rifle butt hitting a body.</summary>
    public static AudioStreamWav ShoveThud => _shoveThud ??= Generate(0.2f, seed: 12, (t, noise) =>
        MathF.Sin(2f * MathF.PI * 70f * t) * MathF.Exp(-t * 25f) + noise * MathF.Exp(-t * 60f) * 0.5f);

    /// <summary>A melee swing cutting the air.</summary>
    public static AudioStreamWav MeleeWhoosh => _meleeWhoosh ??= Generate(0.22f, seed: 13, (t, noise) =>
        noise * MathF.Sin(MathF.PI * MathF.Min(t / 0.22f, 1f)) * 0.35f);

    /// <summary>Something hard hitting a body (bat, axe back, stomp).</summary>
    public static AudioStreamWav MeleeBlunt => _meleeBlunt ??= Generate(0.25f, seed: 14, (t, noise) =>
        MathF.Sin(2f * MathF.PI * 55f * t) * MathF.Exp(-t * 18f) + noise * MathF.Exp(-t * 45f) * 0.7f);

    /// <summary>A blade biting into a body.</summary>
    public static AudioStreamWav MeleeBlade => _meleeBlade ??= Generate(0.22f, seed: 15, (t, noise) =>
        MathF.Sin(2f * MathF.PI * 80f * t) * MathF.Exp(-t * 30f) * 0.7f + noise * MathF.Exp(-t * 14f) * 0.45f);

    /// <summary>A weapon hitting a wall or floor.</summary>
    public static AudioStreamWav MeleeWall => _meleeWall ??= Generate(0.3f, seed: 16, (t, noise) =>
        (MathF.Sin(2f * MathF.PI * 620f * t) * 0.4f + MathF.Sin(2f * MathF.PI * 1310f * t) * 0.25f) * MathF.Exp(-t * 16f)
        + noise * MathF.Exp(-t * 60f) * 0.6f);

    private static AudioStreamWav? _meleeWhoosh, _meleeBlunt, _meleeBlade, _meleeWall;

    private static AudioStreamWav Generate(float seconds, int seed, Func<float, float, float> sample)
    {
        int count = (int)(seconds * MixRate);
        byte[] data = new byte[count * 2];
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / MixRate;
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            short value = (short)(Math.Clamp(sample(t, noise), -1f, 1f) * short.MaxValue);
            data[i * 2] = (byte)(value & 0xFF);
            data[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }
}
