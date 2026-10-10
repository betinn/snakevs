using Microsoft.Xna.Framework.Audio;

namespace SnakeVs.Game.Audio;

public enum Sfx
{
    Click,
    Eat,
    Golden,
    PowerUp,
    Shield,
    Die,
    LevelUp,
    Win,
    Lose,
    Spawn,
}

/// <summary>
/// Retro sound effects synthesized at start-up (square, triangle and noise voices), so no audio files ship with the game.
/// Audio failures are swallowed: a device without audio must still be able to play.
/// </summary>
public sealed class SoundBank : IDisposable
{
    private const int SampleRate = 22050;
    private readonly Dictionary<Sfx, SoundEffect> _effects = [];
    private readonly Random _noise = new(1234);

    public SoundBank()
    {
        try
        {
            Add(Sfx.Click, Tone(Wave.Square, 900, 900, 0.035, 0.25));
            Add(Sfx.Eat, Concat(Tone(Wave.Square, 520, 700, 0.045, 0.3), Tone(Wave.Square, 780, 1040, 0.06, 0.3)));
            Add(Sfx.Golden, Concat(Tone(Wave.Triangle, 880, 880, 0.06, 0.45), Tone(Wave.Triangle, 1175, 1175, 0.06, 0.45), Tone(Wave.Triangle, 1568, 1568, 0.12, 0.45)));
            Add(Sfx.PowerUp, Tone(Wave.Square, 300, 1300, 0.22, 0.25));
            Add(Sfx.Shield, Concat(Tone(Wave.Triangle, 1400, 700, 0.12, 0.45), Tone(Wave.Noise, 0, 0, 0.08, 0.15)));
            Add(Sfx.Die, Concat(Tone(Wave.Square, 600, 90, 0.42, 0.3), Tone(Wave.Noise, 0, 0, 0.2, 0.25)));
            Add(Sfx.LevelUp, Concat(Tone(Wave.Square, 523, 523, 0.08, 0.25), Tone(Wave.Square, 659, 659, 0.08, 0.25), Tone(Wave.Square, 784, 784, 0.08, 0.25), Tone(Wave.Square, 1047, 1047, 0.2, 0.25)));
            Add(Sfx.Win, Concat(Tone(Wave.Triangle, 523, 523, 0.1, 0.5), Tone(Wave.Triangle, 659, 659, 0.1, 0.5), Tone(Wave.Triangle, 784, 784, 0.1, 0.5), Tone(Wave.Triangle, 1047, 1047, 0.35, 0.5)));
            Add(Sfx.Lose, Concat(Tone(Wave.Triangle, 392, 392, 0.14, 0.5), Tone(Wave.Triangle, 330, 330, 0.14, 0.5), Tone(Wave.Triangle, 262, 200, 0.4, 0.5)));
            Add(Sfx.Spawn, Tone(Wave.Triangle, 1200, 1600, 0.05, 0.2));
        }
        catch (Exception)
        {
            // No audio hardware or OpenAL failure: the game runs silently.
            _effects.Clear();
        }
    }

    public bool Enabled { get; set; } = true;

    public void Play(Sfx sfx, float volume = 1f, float pitch = 0f)
    {
        if (!Enabled || !_effects.TryGetValue(sfx, out var effect)) return;
        try
        {
            effect.Play(Math.Clamp(volume, 0f, 1f), Math.Clamp(pitch, -1f, 1f), 0f);
        }
        catch (Exception)
        {
            // Too many voices or audio device lost; skip this sound.
        }
    }

    public void Dispose()
    {
        foreach (var e in _effects.Values) e.Dispose();
        _effects.Clear();
    }

    private void Add(Sfx sfx, float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }

        _effects[sfx] = new SoundEffect(bytes, SampleRate, AudioChannels.Mono);
    }

    private enum Wave
    {
        Square,
        Triangle,
        Noise,
    }

    private float[] Tone(Wave wave, double startHz, double endHz, double seconds, double volume)
    {
        var count = (int)(seconds * SampleRate);
        var data = new float[count];
        var phase = 0.0;
        var attack = Math.Min(count / 10, (int)(0.004 * SampleRate));
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)count;
            var hz = startHz + (endHz - startHz) * t;
            phase += hz / SampleRate;
            phase -= Math.Floor(phase);

            var v = wave switch
            {
                Wave.Square => phase < 0.5 ? 1.0 : -1.0,
                Wave.Triangle => 1.0 - 4.0 * Math.Abs(phase - 0.5),
                _ => _noise.NextDouble() * 2.0 - 1.0,
            };

            var envelope = i < attack ? i / (double)Math.Max(1, attack) : Math.Pow(1.0 - t, 1.6);
            data[i] = (float)(v * envelope * volume);
        }

        return data;
    }

    private static float[] Concat(params float[][] parts)
    {
        var result = new float[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }

        return result;
    }
}
