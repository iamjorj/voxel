using NAudio.Wave;
using VoiceChanger.Core;

namespace VoiceChanger.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Use the AI models from the Voxel project so the harness tests the same files the app ships.
        var aiDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Voxel", "Assets", "ai"));
        if (Directory.Exists(aiDir)) VoiceChanger.Core.Dsp.AiVoiceStage.ModelDirectory = aiDir;

        // `--selftest`: run the clown pipeline for a few seconds without a window, so the
        // audio chain can be checked from a terminal. Exit code 0 = no errors.
        if (args.Contains("--selftest"))
            return SelfTest();
        // `--dsptest`: offline checks of the effect engine, no audio devices needed.
        if (args.Contains("--dsptest"))
            return DspTest();
        // `--record <seconds> <file.wav>`: record the real mic to a 16 kHz mono wav (for AI voice tests).
        if (args.Length >= 3 && args[0] == "--record")
            return Record(int.Parse(args[1]), args[2]);
        // `--aifile <in.wav> <model> <out.wav>`: play a recording through an AI voice at live pace, as the mic would.
        if (args.Length >= 4 && args[0] == "--aifile")
        {
            if (args.Length >= 5) VoiceChanger.Core.Dsp.AiVoiceStage.Threads = int.Parse(args[4]);
            return AiFile(args[1], args[2], args[3]);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int Record(int seconds, string path)
    {
        using var mic = AudioDevices.RealMicrophone() ?? throw new InvalidOperationException("No microphone found.");
        Console.WriteLine($"Recording {seconds} s from: {mic.FriendlyName}  — speak now");
#pragma warning disable CS0618
        using var capture = new NAudio.CoreAudioApi.WasapiCapture(mic, true, 20);
#pragma warning restore CS0618
        var buffered = new NAudio.Wave.BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(seconds + 2)) { ReadFully = false };
        capture.DataAvailable += (_, e) => buffered.AddSamples(e.Buffer, 0, e.BytesRecorded);
        capture.StartRecording();
        for (var i = seconds; i > 0; i--) { Console.Write($"{i} "); Thread.Sleep(1000); }
        capture.StopRecording();
        Console.WriteLine();

        // Mono float at the mic's rate -> 16 kHz 16-bit mono, which is what LLVC expects.
        NAudio.Wave.ISampleProvider mono = buffered.ToSampleProvider();
        if (capture.WaveFormat.Channels == 2) mono = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(mono) { LeftVolume = 0.5f, RightVolume = 0.5f };
        var resampled = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(mono, 16000);
        NAudio.Wave.WaveFileWriter.CreateWaveFile16(path, resampled);
        Console.WriteLine($"Saved {path} ({new FileInfo(path).Length / 1024} KB)");
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    private static int AiFile(string input, string model, string output)
    {
        const int rate = 48000, block = 960; // 20 ms, like the capture buffer
        float[] voice;
        using (var reader = new AudioFileReader(input))
        {
            ISampleProvider mono = reader.WaveFormat.Channels == 2 ? new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(reader) : reader;
            var resampled = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(mono, rate);
            var all = new List<float>();
            var tmp = new float[rate];
            int n;
            while ((n = resampled.Read(tmp.AsSpan())) > 0) all.AddRange(tmp.AsSpan(0, n));
            all.AddRange(new float[rate]); // one second of silence so the tail comes out
            voice = all.ToArray();
        }

        var src = new VoiceChanger.Core.Dsp.SplitSource(WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        src.Push(voice);
        using var ai = new VoiceChanger.Core.Dsp.AiVoiceStage(src);
        var load = System.Diagnostics.Stopwatch.StartNew();
        ai.SetModel(model);
        Console.WriteLine($"Loaded {model} in {load.ElapsedMilliseconds} ms");

        var result = new float[voice.Length];
        TimeBeginPeriod(1); // 1 ms sleeps, so blocks arrive evenly like a real 20 ms capture buffer
        var pace = System.Diagnostics.Stopwatch.StartNew();
        var gapTimes = new List<string>();
        for (var done = 0; done + block <= voice.Length; done += block)
        {
            var gaps = ai.Underruns;
            ai.Read(result, done, block);
            if (ai.Underruns > gaps) gapTimes.Add($"{done / (double)rate:F2}s");
            var wait = TimeSpan.FromSeconds((double)(done + block) / rate) - pace.Elapsed;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        }

        // Delay = shift that best lines up the loudness envelopes (10 ms steps) of input and output.
        static double[] Envelope(float[] x) =>
            Enumerable.Range(0, x.Length / 480).Select(i => Math.Sqrt(x.Skip(i * 480).Take(480).Average(v => (double)v * v))).ToArray();
        var ein = Envelope(voice);
        var eout = Envelope(result);
        var best = 0; var bestScore = double.MinValue;
        for (var lag = 0; lag < 100; lag++)
        {
            double score = 0;
            for (var i = 0; i + lag < eout.Length; i++) score += ein[i] * eout[i + lag];
            if (score > bestScore) { bestScore = score; best = lag; }
        }

        using (var writer = new WaveFileWriter(output, new WaveFormat(rate, 16, 1)))
            writer.WriteSamples(result, 0, result.Length);
        var chunkLoad = ai.AverageRunMs / ai.ChunkMs;
        Console.WriteLine($"Model {ai.AverageRunMs:F1} ms per {ai.ChunkMs:F0} ms of audio = {chunkLoad:P0} of real time ({ai.Runs} runs)");
        Console.WriteLine($"Slowest run {ai.MaxRunMs:F0} ms. Gaps: {ai.Underruns} ({ai.MissingSamples * 1000.0 / rate:F0} ms missing) {string.Join(" ", gapTimes)}; delay about {best * 10} ms; saved {output}");
        return 0;
    }

    private static int DspTest()
    {
        const int rate = 48000;
        var format = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        var failed = false;

        // A 200 Hz sawtooth stands in for a voice: strong harmonics, easy to track.
        static float[] Saw(int rate, float hz, float seconds)
        {
            var s = new float[(int)(rate * seconds)];
            for (var i = 0; i < s.Length; i++) s[i] = (float)((i * hz / rate) % 1.0 * 2 - 1) * 0.3f;
            return s;
        }
        static float Rms(ReadOnlySpan<float> x) { double a = 0; foreach (var v in x) a += v * v; return (float)Math.Sqrt(a / x.Length); }

        float MeasuredPitch(float pitch, float formant)
        {
            var src = new VoiceChanger.Core.Dsp.SplitSource(format);
            src.Push(Saw(rate, 200, 2));
            var shifter = new VoiceChanger.Core.Dsp.FormantPitchShifter(src) { PitchFactor = pitch, FormantFactor = formant };
            var output = new float[rate * 2];
            shifter.Read(output, 0, output.Length);
            // Strongest spectral peak one second in (a sawtooth's loudest harmonic is its fundamental).
            var fft = new NAudio.Dsp.Complex[8192];
            for (var i = 0; i < fft.Length; i++) fft[i].X = output[rate + i] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / fft.Length));
            NAudio.Dsp.FastFourierTransform.FFT(true, 13, fft);
            var peak = 1; float peakMag = 0;
            for (var k = 1; k < fft.Length / 2; k++) { var m = fft[k].X * fft[k].X + fft[k].Y * fft[k].Y; if (m > peakMag) { peakMag = m; peak = k; } }
            var hz = peak * (float)rate / fft.Length;
            var tracker = new VoiceChanger.Core.Dsp.PitchTracker(rate);
            tracker.Analyze(output.AsSpan(rate, 4096));
            var outRms = Rms(output.AsSpan(rate, rate / 2));
            Console.WriteLine($"pitch x{pitch} formant {formant}: in 200 Hz -> out {hz:F0} Hz (tracker says {tracker.F0:F0}), level {outRms / 0.17f:P0} of input");
            return hz;
        }
        void Expect(string what, float got, float want)
        {
            var ok = Math.Abs(got - want) / want < 0.06;
            Console.WriteLine($"  {(ok ? "ok" : "FAIL")}: {what} expected ~{want:F0} Hz");
            failed |= !ok;
        }

        Expect("unchanged", MeasuredPitch(1f, 0f), 200);
        Expect("pitch up", MeasuredPitch(1.5f, 0f), 300);
        Expect("pitch down", MeasuredPitch(0.7f, 0f), 140);
        Expect("formant only", MeasuredPitch(1f, 1.3f), 200);
        Expect("pitch up, formant independent", MeasuredPitch(1.5f, 1.1f), 300);

        // Speed: every voice must process audio much faster than real time.
        var sw = new System.Diagnostics.Stopwatch();
        var results = new List<(string Name, double Ratio)>();
        var signal = Saw(rate, 150, 2);
        foreach (var v in VoiceCatalog.All)
        {
            var src = new VoiceChanger.Core.Dsp.SplitSource(format);
            src.Push(signal);
            using var chain = new VoiceChanger.Core.Dsp.VoiceEffectChain(src);
            chain.Apply(v);
            var buf = new float[960];
            sw.Restart();
            for (var done = 0; done < signal.Length; done += buf.Length) chain.Read(buf, 0, buf.Length);
            sw.Stop();
            results.Add((v.Name, sw.Elapsed.TotalSeconds / 2.0));
        }
        // AI stage on its own, per model variant and thread count: where does the time go?
        // Three passes in a row: the first includes thread-pool and CPU-clock warm-up, the rest are steady state.
        foreach (var model in new[] { "llvc-demo-c4", "meanvc/man" })
        {
            if (!VoiceChanger.Core.Dsp.AiVoiceStage.IsAvailable(model)) continue;
            for (var pass = 1; pass <= 2; pass++)
            {
                var src = new VoiceChanger.Core.Dsp.SplitSource(format);
                src.Push(signal);
                using var ai = new VoiceChanger.Core.Dsp.AiVoiceStage(src);
                try { ai.SetModel(model); } catch (Exception ex) { Console.WriteLine($"AI model not available: {ex.Message}"); break; }
                var buf = new float[960];
                // Feed at real-time pace so the worker thread is measured the way the app uses it.
                var pace = new System.Diagnostics.Stopwatch();
                pace.Start();
                for (var done = 0; done < signal.Length; done += buf.Length)
                {
                    ai.Read(buf, 0, buf.Length);
                    var due = TimeSpan.FromSeconds((double)(done + buf.Length) / rate);
                    var wait = due - pace.Elapsed;
                    if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                }
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (ai.PendingInput >= ai.ChunkMs * 16 && DateTime.UtcNow < deadline) Thread.Sleep(5);
                var load = ai.AverageRunMs / ai.ChunkMs;
                Console.WriteLine($"AI {model} pass {pass}, {VoiceChanger.Core.Dsp.AiVoiceStage.Threads} threads: " +
                                  $"model {ai.AverageRunMs:F2} ms per {ai.ChunkMs:F1} ms chunk = {load:P0} of real time ({ai.Runs} runs, {ai.Underruns} gaps)");
                if (load > 0.9) { Console.WriteLine("FAIL: AI voice can't keep up with real time"); failed = true; }
            }
        }
        // Silence in, silence out: no voice may add a sound of its own (people in a call would hear it while you're quiet).
        var noisy = new List<string>();
        foreach (var v in VoiceCatalog.All.Where(v => v.AiModel == null))
        {
            var src = new VoiceChanger.Core.Dsp.SplitSource(format);
            src.Push(new float[rate * 2]);
            using var chain = new VoiceChanger.Core.Dsp.VoiceEffectChain(src);
            chain.Apply(v);
            var output = new float[rate * 2];
            chain.Read(output, 0, output.Length);
            var peak = output.Max(Math.Abs);
            if (peak > 0.001f) noisy.Add($"{v.Name} ({peak:F3})");
        }
        Console.WriteLine(noisy.Count == 0 ? "  ok: every voice is silent when you are" : $"  FAIL: sound during silence: {string.Join(", ", noisy)}");
        failed |= noisy.Count > 0;

        Console.WriteLine("Slowest voices (processing time / audio time):");
        foreach (var r in results.OrderByDescending(r => r.Ratio).Take(5))
            Console.WriteLine($"  {r.Name}: {r.Ratio:P1}");
        // Laptops on battery run everything ~2x slower than plugged in; 80% is the line where glitches start.
        if (results.Max(r => r.Ratio) > 0.8) { Console.WriteLine("FAIL: a voice uses more than 80% of real time"); failed = true; }

        Console.WriteLine(failed ? "DSP TEST FAILED" : "DSP TEST OK");
        return failed ? 1 : 0;
    }

    private static string VisibleMics()
    {
        using var e = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        return string.Join(" | ", e.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active)
            .Select(d => d.FriendlyName));
    }

    private static int SelfTest()
    {
        Exception? failure = null;
        using var service = new VoiceChangerService();
        service.Failed += ex => failure = ex;
        try
        {
            Console.WriteLine($"VB-Cable installed: {AudioDevices.IsVbCableInstalled()}");
            Console.WriteLine($"Default speakers: {AudioDevices.DefaultSpeakers().FriendlyName}");
            Console.WriteLine($"Real mic: {AudioDevices.RealMicrophone()?.FriendlyName ?? "(none)"}");
            Console.WriteLine($"Default mic before: {AudioDevices.DefaultMicrophone().FriendlyName}");
            service.TurnOn(Voice.Clown);
            Console.WriteLine(service.IsPreviewOnly ? "Running: preview through speakers" : "Running: through VB-Cable");
            Console.WriteLine($"Default mic while on: {AudioDevices.DefaultMicrophone().FriendlyName}");
            Console.WriteLine($"Mics visible to other apps while on: {VisibleMics()}");
            // Switch through every preset while running: catches bad DSP parameters.
            foreach (var v in VoiceCatalog.All)
            {
                service.TurnOn(v);
                Thread.Sleep(150);
                if (failure != null) throw new Exception($"Voice '{v.Name}' failed", failure);
            }
            Console.WriteLine($"Switched through {VoiceCatalog.All.Count} voices without errors");
            Console.WriteLine($"Live input level right now: {service.Level:F2} (0 = silence, speak to see it rise)");
            service.HearMyself = true;
            Thread.Sleep(800);
            Console.WriteLine("Hear-myself on while a voice is on: OK");
            service.HearMyself = false;
            Thread.Sleep(1000);
            service.TurnOff();
            Console.WriteLine($"Default mic after: {AudioDevices.DefaultMicrophone().FriendlyName}");
            Console.WriteLine($"Mics visible after: {VisibleMics()}");
            Console.WriteLine($"Default speakers after: {AudioDevices.DefaultSpeakers().FriendlyName}");
            service.HearMyself = true;
            Thread.Sleep(800);
            Console.WriteLine($"Mic test with no voice on: level {service.Level:F2}, default mic still {AudioDevices.DefaultMicrophone().FriendlyName}");
            service.HearMyself = false;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        if (failure != null)
        {
            Console.Error.WriteLine($"FAILED: {failure}");
            return 1;
        }
        Console.WriteLine("OK: pipeline ran for 3 s with no errors");
        return 0;
    }
}
