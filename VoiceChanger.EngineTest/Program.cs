using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceChanger.Core;
using VoiceChanger.Core.Dsp;

namespace VoiceChanger.EngineTest;

/// <summary>
/// Runs the audio engine with no audio devices, on any OS: pitch correctness, every effect voice,
/// and the AI voices at live pace on a real speech clip. `dotnet run -- [output folder]` writes
/// results.txt and the converted audio there. Exit code 0 = passed.
/// </summary>
static class Program
{
    private const int Rate = 48000;
    private static readonly StringBuilder Report = new();
    private static bool _failed;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "results");
        Directory.CreateDirectory(output);
        AiVoiceStage.ModelDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "ai");
        if (OperatingSystem.IsWindows()) TimeBeginPeriod(1); // 1 ms sleeps, so blocks arrive evenly

        Log($"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} cores, {RuntimeInformation.FrameworkDescription}");
        try
        {
            PitchChecks();
            EffectVoices();
            foreach (var voice in VoiceCatalog.All.Where(v => v.AiModel != null))
                AiVoice(voice, output);
        }
        catch (Exception ex)
        {
            Log($"FAIL: crashed: {ex}");
            _failed = true;
        }

        Log(_failed ? "ENGINE TEST FAILED" : "ENGINE TEST OK");
        File.WriteAllText(Path.Combine(output, "results.txt"), Report.ToString());
        return _failed ? 1 : 0;
    }

    private static void Log(string line)
    {
        Console.WriteLine(line);
        Report.AppendLine(line);
    }

    private static void Check(bool ok, string what)
    {
        Log($"  {(ok ? "ok" : "FAIL")}: {what}");
        _failed |= !ok;
    }

    // A sawtooth stands in for a voice: strong harmonics, easy to measure.
    private static float[] Saw(float hz, float seconds)
    {
        var s = new float[(int)(Rate * seconds)];
        for (var i = 0; i < s.Length; i++) s[i] = (float)((i * hz / Rate) % 1.0 * 2 - 1) * 0.3f;
        return s;
    }

    private static void PitchChecks()
    {
        Log("Pitch shifter, 200 Hz in:");
        foreach (var (pitch, formant, want) in new[] { (1f, 0f, 200f), (1.5f, 0f, 300f), (0.7f, 0f, 140f), (1f, 1.3f, 200f), (1.5f, 1.1f, 300f) })
        {
            var src = new SplitSource(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1));
            src.Push(Saw(200, 2));
            var shifter = new FormantPitchShifter(src) { PitchFactor = pitch, FormantFactor = formant };
            var result = new float[Rate * 2];
            shifter.Read(result, 0, result.Length);
            var hz = MedianPitch(result.AsSpan(Rate));
            Check(Math.Abs(hz - want) / want < 0.06, $"pitch x{pitch} formant {formant}: {hz:F0} Hz, expected ~{want:F0}");
        }
    }

    private static float MedianPitch(ReadOnlySpan<float> audio)
    {
        var tracker = new PitchTracker(Rate);
        var found = new List<float>();
        for (var at = 0; at + 4096 <= audio.Length; at += 4096)
        {
            tracker.Analyze(audio.Slice(at, 4096));
            if (tracker.F0 > 0) found.Add(tracker.F0);
        }
        found.Sort();
        return found.Count == 0 ? 0 : found[found.Count / 2];
    }

    private static void EffectVoices()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);
        var signal = Saw(150, 2);
        var voices = VoiceCatalog.All.Where(v => v.AiModel == null).ToList();
        var speed = new List<(string Name, double Ratio)>();
        var broken = new List<string>();
        var noisy = new List<string>();
        var clock = new Stopwatch();
        foreach (var voice in voices)
        {
            var src = new SplitSource(format);
            src.Push(signal);
            using (var chain = new VoiceEffectChain(src))
            {
                chain.Apply(voice);
                var buffer = new float[960];
                for (var done = 0; done < signal.Length; done += buffer.Length)
                {
                    clock.Restart();
                    chain.Read(buffer, 0, buffer.Length);
                    clock.Stop();
                    speed.Add((voice.Name, clock.Elapsed.TotalSeconds / 0.02));
                    if (buffer.Any(v => !float.IsFinite(v))) { broken.Add(voice.Name); break; }
                }
            }

            // Silence in, silence out: no voice may add a sound of its own.
            var quiet = new SplitSource(format);
            quiet.Push(new float[Rate * 2]);
            using var silent = new VoiceEffectChain(quiet);
            silent.Apply(voice);
            var result = new float[Rate * 2];
            silent.Read(result, 0, result.Length);
            if (result.Max(Math.Abs) > 0.001f) noisy.Add(voice.Name);
        }

        Log($"{voices.Count} effect voices:");
        Check(broken.Count == 0, broken.Count == 0 ? "no voice produced broken numbers" : $"broken numbers: {string.Join(", ", broken.Distinct())}");
        Check(noisy.Count == 0, noisy.Count == 0 ? "every voice is silent when you are" : $"sound during silence: {string.Join(", ", noisy)}");
        var perVoice = speed.GroupBy(s => s.Name).Select(g => (Name: g.Key, Ratio: g.Average(s => s.Ratio))).OrderByDescending(v => v.Ratio).ToList();
        Check(perVoice[0].Ratio < 0.8, $"slowest is {perVoice[0].Name} at {perVoice[0].Ratio:P0} of real time");
    }

    private static float[] ReadSpeech()
    {
        using var reader = new WaveFileReader(Path.Combine(AppContext.BaseDirectory, "speech.wav"));
        var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider(), Rate);
        var all = new List<float>();
        var chunk = new float[Rate];
        int n;
        while ((n = resampled.Read(chunk.AsSpan())) > 0) all.AddRange(chunk.AsSpan(0, n));
        all.AddRange(new float[Rate]); // one second of silence so the tail comes out
        return all.ToArray();
    }

    // Blocks of 20 ms at a steady pace, exactly how the app's audio thread reads the stage.
    private static void AiVoice(Voice voice, string output)
    {
        const int block = 960;
        var speech = ReadSpeech();
        var src = new SplitSource(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1));
        src.Push(speech);
        using var ai = new AiVoiceStage(src);
        var load = Stopwatch.StartNew();
        ai.SetModel(voice.AiModel!);
        var loadMs = load.ElapsedMilliseconds;

        var result = new float[speech.Length];
        var pace = Stopwatch.StartNew();
        for (var done = 0; done + block <= speech.Length; done += block)
        {
            ai.Read(result, done, block);
            var wait = TimeSpan.FromSeconds((double)(done + block) / Rate) - pace.Elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }

        var name = voice.Name.ToLowerInvariant().Replace(' ', '-');
        using (var writer = new WaveFileWriter(Path.Combine(output, $"{name}.wav"), new WaveFormat(Rate, 16, 1)))
            writer.WriteSamples(result, 0, result.Length);

        var busy = ai.AverageRunMs / ai.ChunkMs;
        var rms = Math.Sqrt(result.Average(v => (double)v * v));
        Log($"AI {voice.Name}: loaded in {loadMs} ms, {busy:P0} of one core, slowest run {ai.MaxRunMs:F0} ms, " +
            $"{ai.Underruns} gaps ({ai.MissingSamples * 1000.0 / Rate:F0} ms missing of {speech.Length * 1000 / Rate} ms), " +
            $"delay ~{Delay(speech, result)} ms, pitch {MedianPitch(result):F0} Hz");
        Check(busy < 0.9, $"{voice.Name} keeps up with real time");
        Check(result.All(float.IsFinite) && rms > 0.005, $"{voice.Name} produced real audio");
    }

    // The shift that best lines up the loudness of input and output, in 10 ms steps.
    private static int Delay(float[] input, float[] result)
    {
        static double[] Envelope(float[] x)
        {
            var e = new double[x.Length / 480];
            for (var i = 0; i < e.Length; i++)
            {
                double sum = 0;
                for (var j = 0; j < 480; j++) sum += x[i * 480 + j] * x[i * 480 + j];
                e[i] = Math.Sqrt(sum / 480);
            }
            return e;
        }
        var a = Envelope(input);
        var b = Envelope(result);
        var best = 0;
        var bestScore = double.MinValue;
        for (var lag = 0; lag < 100; lag++)
        {
            double score = 0;
            for (var i = 0; i + lag < b.Length; i++) score += a[i] * b[i + lag];
            if (score > bestScore) { bestScore = score; best = lag; }
        }
        return best * 10;
    }
}
