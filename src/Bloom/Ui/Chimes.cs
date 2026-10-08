using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using Bloom.Core;

namespace Bloom.Ui;

/// The notices' sounds, made here (no sound files to ship): short sine notes with a soft attack and a ringing tail, one
/// figure per kind — 진행 중 one note, 작업 완료 two rising, 승인 대기 the same note twice, 한도 two falling, 복구 three
/// rising. Volume from 설정 > 알림 > 효과음; each figure is made once per volume.
internal static class Chimes
{
    private const int Rate = 44100;
    private static readonly Dictionary<(string kind, string volume), byte[]> made = new();
    private static SoundPlayer? player; // kept: a sound stops when its player is collected

    public static void Play(string kind)
    {
        string volume = Settings.Current.NoticeSound;
        if (volume == "off") return;
        if (!made.TryGetValue((kind, volume), out var wav))
            made[(kind, volume)] = wav = Wav(Notes(kind), volume switch { "s" => 0.18, "l" => 0.62, _ => 0.36 });
        try
        {
            player?.Stop();
            player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            // no sound device: the card still shows
        }
    }

    /// (frequency, start in seconds) of each note.
    private static (double hz, double at)[] Notes(string kind) => kind switch
    {
        "start" => new[] { (659.25, 0.0) }, // E5
        "done" => new[] { (783.99, 0.0), (1174.66, 0.11) }, // G5 → D6
        "waiting" => new[] { (880.0, 0.0), (880.0, 0.17) }, // A5 A5
        "limit" => new[] { (783.99, 0.0), (587.33, 0.15) }, // G5 → D5
        _ => new[] { (659.25, 0.0), (783.99, 0.09), (1046.5, 0.18) }, // E5 G5 C6
    };

    /// 16-bit mono PCM in a WAV, peak at `volume` of full scale.
    private static byte[] Wav((double hz, double at)[] notes, double volume)
    {
        int n = (int)(Rate * (notes[^1].at + 0.55));
        var mix = new double[n];
        foreach (var (hz, at) in notes)
            for (int i = (int)(at * Rate); i < n; i++)
            {
                double t = (i - (at * Rate)) / Rate;
                double envelope = Math.Min(1, t / 0.006) * Math.Exp(-t * 7.5);
                mix[i] += envelope * (Math.Sin(2 * Math.PI * hz * t) + (0.22 * Math.Sin(4 * Math.PI * hz * t)));
            }
        double peak = Math.Max(1e-6, mix.Max(Math.Abs));
        using var ms = new MemoryStream(44 + (n * 2));
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + (n * 2));
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(n * 2);
        foreach (double v in mix) w.Write((short)Math.Round(v / peak * volume * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
