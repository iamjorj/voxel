using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceChanger.Core;
using VoiceChanger.Core.Dsp;
using VoiceChanger.Mac;

namespace VoiceChanger.Mac.LiveTest;

/// <summary>
/// The whole path through real Mac audio devices, the way a user and another app would use it:
/// speech plays into a stand-in mic, the service runs a voice, and a separate recorder listens
/// to whatever the system's default mic is, like Discord would. `dotnet run -- [output folder]`
/// writes results.txt and the recordings there. Exit code 0 = passed.
/// </summary>
static class Program
{
    private const int Rate = 48000;
    private const int RecordSeconds = 8;
    private static readonly StringBuilder Report = new();
    private static bool _failed;

    // The stand-in mic's player: loops the speech clip, or plays silence.
    private static float[] _speech = [];
    private static long _played;
    private static volatile bool _playSilence;

    static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "results");
        Directory.CreateDirectory(output);
        AiVoiceStage.ModelDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "ai");
        Log($"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} cores");
        try
        {
            Run(output);
        }
        catch (Exception ex)
        {
            Log($"FAIL: crashed: {ex}");
            _failed = true;
        }
        Log(_failed ? "LIVE TEST FAILED" : "LIVE TEST OK");
        File.WriteAllText(Path.Combine(output, "results.txt"), Report.ToString());
        return _failed ? 1 : 0;
    }

    private static void Run(string output)
    {
        Log("Devices:");
        foreach (var d in MacAudioDevices.All())
            Log($"  {d.Name}: {d.InputChannels} in, {d.OutputChannels} out ({d.Uid})");

        var cable = MacAudioDevices.Cable();
        var fakeMic = MacAudioDevices.All().FirstOrDefault(d => d.Name.Contains("BlackHole") && d.InputChannels > 0 && d.OutputChannels > 0);
        Check(cable != null, "VB-Cable found, with both ends");
        Check(fakeMic != null, "stand-in mic (BlackHole) found");
        if (cable == null || fakeMic == null) return;

        MacAudioDevices.SetDefaultMicrophone(fakeMic);
        Check(MacAudioDevices.DefaultMicrophone()?.Uid == fakeMic.Uid, "can set the default mic");
        Check(MacAudioDevices.RealMicrophone()?.Uid == fakeMic.Uid, "picks it as the real mic");

        _speech = ReadSpeech();
        var sourcePitch = MedianPitch(_speech);
        Log($"Source speech pitch: {sourcePitch:F0} Hz");

        using var player = new MacOutputQueue(fakeMic.Uid, Rate, 1, Rate / 50, PlaySpeech, ex => { Log($"FAIL: player: {ex.Message}"); _failed = true; });
        player.Start();

        using var service = new MacVoiceChangerService();
        service.Failed += ex => { Log($"FAIL: engine: {ex.Message}"); _failed = true; };

        // A voice on: other apps' default mic must now be VB-Cable.
        service.TurnOn(Voice.Normal);
        Check(!service.IsPreviewOnly, "audio goes through VB-Cable, not the speakers");
        Check(MacAudioDevices.DefaultMicrophone()?.Uid == cable.Uid, "default mic switched to VB-Cable");

        Thread.Sleep(500);
        var normal = Measure("Normal", output);
        Check(Math.Abs(normal.Pitch - sourcePitch) / sourcePitch < 0.12, $"Normal keeps the pitch ({normal.Pitch:F0} Hz)");
        Check(normal.Rms > 0.005, "an app recording the default mic hears the voice");
        Check(service.Level > 0, "the level meter moves");

        // Switching voices while on, as a click does: instant, same session.
        foreach (var name in new[] { "Chipmunk", "Man", "E-girl", "Grandpa" })
        {
            var voice = VoiceCatalog.All.First(v => v.Name == name);
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            service.TurnOn(voice);
            Thread.Sleep(voice.AiModel != null ? 1500 : 500);
            var clock = Stopwatch.StartNew();
            var result = Measure(name, output);
            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds / (clock.Elapsed.TotalSeconds + (voice.AiModel != null ? 1.5 : 0.5));
            Log($"    whole app CPU about {cpu:P0} of one core");
            Check(result.Rms > 0.005, $"{name} is heard by an app");
            if (name == "Chipmunk") Check(Math.Abs(result.Pitch - sourcePitch * voice.Pitch) / (sourcePitch * voice.Pitch) < 0.15, $"Chipmunk raises the pitch ({result.Pitch:F0} Hz)");
        }

        // Quiet in, quiet out.
        service.TurnOn(Voice.Normal);
        _playSilence = true;
        Thread.Sleep(500);
        var silent = Measure("silence", output);
        Check(silent.Peak < 0.01, $"silent when you are (peak {silent.Peak:F4})");
        _playSilence = false;

        // Hear myself, on then off, with a voice running.
        service.HearMyself = true;
        Thread.Sleep(1000);
        service.HearMyself = false;
        Check(!_failed, "Hear myself turns on and off with a voice on");

        service.TurnOff();
        Check(MacAudioDevices.DefaultMicrophone()?.Uid == fakeMic.Uid, "real mic is back after turning off");
        Check(!service.IsOn, "service reports off");

        // Mic test: nothing on, Hear myself plays the plain mic and leaves the default mic alone.
        service.HearMyself = true;
        Thread.Sleep(1000);
        Check(MacAudioDevices.DefaultMicrophone()?.Uid == fakeMic.Uid, "mic test doesn't touch the default mic");
        service.HearMyself = false;

        // A crash while a voice is on: the next start must put the real mic back.
        var crashed = new MacDefaultMicSwitcher();
        crashed.SwitchTo(cable, fakeMic);
        Check(MacAudioDevices.DefaultMicrophone()?.Uid == cable.Uid, "simulated crash left VB-Cable as default");
        using (new MacVoiceChangerService())
            Check(MacAudioDevices.DefaultMicrophone()?.Uid == fakeMic.Uid, "next start restores the real mic");
    }

    private static void PlaySpeech(Span<float> block)
    {
        if (_playSilence) { block.Clear(); return; }
        for (var i = 0; i < block.Length; i++)
            block[i] = _speech[(int)((_played + i) % _speech.Length)];
        Interlocked.Add(ref _played, block.Length);
    }

    private readonly record struct Recording(double Rms, float Peak, float Pitch);

    // Records whatever the system's default mic is right now: no device named, like another app.
    private static Recording Measure(string name, string output)
    {
        var recorded = new List<float>(Rate * RecordSeconds);
        var startedAt = Interlocked.Read(ref _played);
        using (var recorder = new MacInputQueue(null, Rate, 1, Rate / 50, samples => { lock (recorded) recorded.AddRange(samples); },
                   ex => { Log($"FAIL: recorder: {ex.Message}"); _failed = true; }))
        {
            recorder.Start();
            Thread.Sleep(RecordSeconds * 1000);
        }

        float[] audio;
        lock (recorded) audio = recorded.ToArray();
        using (var writer = new WaveFileWriter(Path.Combine(output, $"{name.ToLowerInvariant()}.wav"), new WaveFormat(Rate, 16, 1)))
            writer.WriteSamples(audio, 0, audio.Length);

        var rms = audio.Length == 0 ? 0 : Math.Sqrt(audio.Average(v => (double)v * v));
        var peak = audio.Length == 0 ? 0 : audio.Max(Math.Abs);
        var pitch = MedianPitch(audio);
        var delay = _playSilence ? "" : $", delay from player about {Delay(audio, startedAt)} ms";
        Log($"  {name}: recorded {audio.Length / (double)Rate:F1} s, rms {rms:F3}, peak {peak:F3}, pitch {pitch:F0} Hz{delay}");
        return new Recording(rms, peak, pitch);
    }

    // The shift (10 ms steps) that best lines up the recording's loudness with the looped source's.
    private static int Delay(float[] recording, long sourceStart)
    {
        const int step = 480;
        var frames = recording.Length / step;
        var received = new double[frames];
        for (var i = 0; i < frames; i++)
        {
            double sum = 0;
            for (var j = 0; j < step; j++) sum += recording[i * step + j] * recording[i * step + j];
            received[i] = Math.Sqrt(sum / step);
        }
        var best = 0;
        var bestScore = double.MinValue;
        for (var lag = 0; lag < 200; lag++)
        {
            double score = 0;
            for (var i = 0; i < frames; i++)
            {
                double sum = 0;
                var at = sourceStart + (i - lag) * (long)step;
                for (var j = 0; j < step; j += 8)
                {
                    var s = _speech[(int)(((at + j) % _speech.Length + _speech.Length) % _speech.Length)];
                    sum += s * s;
                }
                score += received[i] * Math.Sqrt(sum / (step / 8.0));
            }
            if (score > bestScore) { bestScore = score; best = lag; }
        }
        return best * 10;
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

    private static float[] ReadSpeech()
    {
        using var reader = new WaveFileReader(Path.Combine(AppContext.BaseDirectory, "speech.wav"));
        var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider(), Rate);
        var all = new List<float>();
        var chunk = new float[Rate];
        int n;
        while ((n = resampled.Read(chunk.AsSpan())) > 0) all.AddRange(chunk.AsSpan(0, n));
        return all.ToArray();
    }

    private static void Log(string line)
    {
        Console.WriteLine(line);
        lock (Report) Report.AppendLine(line);
    }

    private static void Check(bool ok, string what)
    {
        Log($"  {(ok ? "ok" : "FAIL")}: {what}");
        _failed |= !ok;
    }
}
