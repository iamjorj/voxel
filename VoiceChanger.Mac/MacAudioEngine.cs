using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceChanger.Core;
using VoiceChanger.Core.Dsp;

namespace VoiceChanger.Mac;

public sealed record MacEngineDiagnostics(double WorstInputGapMs, double WorstOutputGapMs, double MicBacklogMs, int AiGaps, double AiMissingMs, double AiSlowestRunMs)
{
    public override string ToString() =>
        $"worst gap between blocks: input {WorstInputGapMs:F0} ms, output {WorstOutputGapMs:F0} ms; mic backlog {MicBacklogMs:F0} ms; " +
        $"AI so far: {AiGaps} gaps, {AiMissingMs:F0} ms missing, slowest run {AiSlowestRunMs:F0} ms";
}

/// <summary>
/// Real mic -> voice effect -> output device, the Mac twin of the Windows AudioEngine.
/// The output device is normally VB-Cable, so every other app hears the changed voice
/// by recording from it. It can also be the speakers, for previewing.
/// </summary>
public sealed class MacAudioEngine : IDisposable
{
    private const int Rate = 48000;
    // Smaller = less delay, larger = safer against glitches.
    public static int BlockMs { get; set; } = 20;
    private static int BlockFrames => Rate * BlockMs / 1000;

    private MacInputQueue? _capture;
    private MacOutputQueue? _output;
    private BufferedWaveProvider? _buffer;
    private VoiceEffectChain? _chain;
    private byte[] _bytes = new byte[4096];

    // "Hear myself": a copy of the processed signal played on the speakers/headphones.
    private MonitorTap? _tap;
    private MacOutputQueue? _monitorOut;
    private MacAudioDevice? _outputDevice;
    private float _inputLevel;

    private readonly Stopwatch _inputClock = new(), _outputClock = new();
    private double _worstInputGapMs, _worstOutputGapMs;

    public bool IsMonitoring => _monitorOut != null;
    public bool IsRunning => _capture != null;

    /// <summary>How loud the user is right now, 0..1, with a short decay so meters look smooth.</summary>
    public float InputLevel => _inputLevel;

    /// <summary>Raised on the audio thread if capture or playback stops because of an error.</summary>
    public event Action<Exception>? Failed;

    /// <summary>What the audio path has been doing since the last call, for finding where a dropout came from.</summary>
    public MacEngineDiagnostics TakeDiagnostics()
    {
        var chain = _chain;
        var result = new MacEngineDiagnostics(_worstInputGapMs, _worstOutputGapMs, _buffer?.BufferedDuration.TotalMilliseconds ?? 0,
            chain?.AiUnderruns ?? 0, (chain?.AiMissingSamples ?? 0) * 1000.0 / Rate, chain?.AiMaxRunMs ?? 0);
        _worstInputGapMs = _worstOutputGapMs = 0;
        return result;
    }

    private static void Note(Stopwatch clock, ref double worstMs)
    {
        if (clock.IsRunning) worstMs = Math.Max(worstMs, clock.Elapsed.TotalMilliseconds);
        clock.Restart();
    }

    public void Start(Voice voice, MacAudioDevice microphone, MacAudioDevice outputDevice)
    {
        if (IsRunning) Stop();
        _inputLevel = 0;

        _buffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1), TimeSpan.FromMilliseconds(500))
        {
            DiscardOnBufferOverflow = true,
        };
        _chain = new VoiceEffectChain(_buffer.ToSampleProvider());
        _chain.Apply(voice);
        var tap = new MonitorTap(new MonoToStereoSampleProvider(_chain));
        _tap = tap;
        _outputDevice = outputDevice;

        _output = new MacOutputQueue(outputDevice.Uid, Rate, 2, BlockFrames, block =>
        {
            Note(_outputClock, ref _worstOutputGapMs);
            tap.Read(block);
        }, Fail);
        _capture = new MacInputQueue(microphone.Uid, Rate, 1, BlockFrames, OnData, Fail);
        _output.Start();
        _capture.Start();
    }

    /// <summary>Starts or stops playing a copy of the output on the default speakers.</summary>
    public void SetMonitor(bool on)
    {
        if (!IsRunning || _tap == null || on == IsMonitoring) return;
        if (on)
        {
            var speakers = MacAudioDevices.DefaultSpeakers();
            // Already playing on the speakers (preview mode): a second copy would just double it.
            if (speakers == null || speakers.Uid == _outputDevice?.Uid) return;
            _tap.Enabled = true;
            var copy = _tap.Copy;
            _monitorOut = new MacOutputQueue(speakers.Uid, Rate, 2, BlockFrames, block => copy.Read(block), Fail);
            _monitorOut.Start();
        }
        else
        {
            _tap.Enabled = false;
            var m = _monitorOut;
            _monitorOut = null;
            m?.Dispose();
        }
    }

    public void SetVoice(Voice voice) => _chain?.Apply(voice);

    private void Fail(Exception ex) => Failed?.Invoke(ex);

    private void OnData(ReadOnlySpan<float> samples)
    {
        Note(_inputClock, ref _worstInputGapMs);
        var buffer = _buffer;
        if (buffer == null) return;

        // If playback falls behind, drop the backlog instead of letting the delay grow.
        if (buffer.BufferedDuration.TotalMilliseconds > 150)
            buffer.ClearBuffer();

        var byteCount = samples.Length * sizeof(float);
        if (_bytes.Length < byteCount) _bytes = new byte[byteCount];
        MemoryMarshal.AsBytes(samples).CopyTo(_bytes);
        buffer.AddSamples(_bytes, 0, byteCount);

        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        // Speech peaks are small numbers; lift them so a normal voice fills most of the meter.
        var level = Math.Clamp(MathF.Sqrt(peak) * 1.2f, 0f, 1f);
        _inputLevel = Math.Max(level, _inputLevel * 0.8f);
    }

    public void Stop()
    {
        SetMonitor(false);
        var capture = _capture;
        var output = _output;
        var chain = _chain;
        _capture = null;
        _output = null;
        // The queues go first: their threads read the chain and buffer until they are stopped.
        capture?.Dispose();
        output?.Dispose();
        chain?.Dispose();
        _buffer = null;
        _chain = null;
        _tap = null;
        _outputDevice = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// Passes audio through unchanged and, when enabled, keeps a copy in a buffer that a second
    /// player (the speakers) reads. The copy is bounded so it can never delay the main output.
    /// </summary>
    private sealed class MonitorTap(ISampleProvider source) : ISampleProvider
    {
        private readonly BufferedWaveProvider _copyBuffer = new(source.WaveFormat, TimeSpan.FromMilliseconds(300)) { DiscardOnBufferOverflow = true };
        private byte[] _bytes = new byte[8192];

        public bool Enabled { get; set; }
        public ISampleProvider Copy => _copyBuffer.ToSampleProvider();
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public int Read(Span<float> buffer)
        {
            var n = source.Read(buffer);
            if (!Enabled || n == 0) return n;
            var byteCount = n * sizeof(float);
            if (_bytes.Length < byteCount) _bytes = new byte[byteCount * 2];
            MemoryMarshal.AsBytes(buffer[..n]).CopyTo(_bytes);
            _copyBuffer.AddSamples(_bytes, 0, byteCount);
            return n;
        }
    }
}
