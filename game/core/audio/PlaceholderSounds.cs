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
