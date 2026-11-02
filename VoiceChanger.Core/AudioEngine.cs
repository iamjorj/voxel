using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceChanger.Core.Dsp;

// NAudio 3.3 marks WasapiCapture/WasapiOut obsolete in favour of its new builder API.
// They still work fine; moving to the new API is a later, separate step.
#pragma warning disable CS0618

namespace VoiceChanger.Core;

/// <summary>
/// Real mic -> voice effect -> output device.
/// The output device is normally VB-Cable ("CABLE Input"), so every other app hears
/// the changed voice through "CABLE Output". It can also be the speakers, for previewing.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    // Smaller = less delay, larger = safer against glitches. 20 ms is a good start.
    private const int BufferMs = 20;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private VoiceEffectChain? _chain;

    // "Hear myself": a copy of the processed signal played on the speakers/headphones.
    private MonitorTap? _tap;
    private WasapiOut? _monitorOut;
    private MMDevice? _outputDevice;

    /// <summary>True while a copy of the output is also playing on the default speakers.</summary>
    public bool IsMonitoring => _monitorOut != null;

    public bool IsRunning => _capture != null;

    private float _inputLevel;
    private bool _captureIsFloat;

    /// <summary>How loud the user is right now, 0..1, with a short decay so meters look smooth.</summary>
    public float InputLevel => _inputLevel;

    /// <summary>Raised on the audio thread if capture or playback stops because of an error.</summary>
    public event Action<Exception>? Failed;

    public void Start(Voice voice, MMDevice microphone, MMDevice outputDevice)
    {
        if (IsRunning) Stop();

        _capture = new WasapiCapture(microphone, useEventSync: true, audioBufferMillisecondsLength: BufferMs);
        var format = _capture.WaveFormat;
        // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: what shared-mode WASAPI almost always hands out.
        _captureIsFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
                          || (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        _inputLevel = 0;

        _buffer = new BufferedWaveProvider(format, TimeSpan.FromMilliseconds(500))
        {
            DiscardOnBufferOverflow = true,
        };

        ISampleProvider chain = _buffer.ToSampleProvider();
        if (format.Channels == 2)
            chain = new StereoToMonoSampleProvider(chain) { LeftVolume = 0.5f, RightVolume = 0.5f };

        _chain = new VoiceEffectChain(chain);
        _chain.Apply(voice);
        chain = new MonoToStereoSampleProvider(_chain);
        _tap = new MonitorTap(chain);
        _outputDevice = outputDevice;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += (_, e) => { if (e.Exception != null) Failed?.Invoke(e.Exception); };

        _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: BufferMs);
        _output.Init(_tap);
        _output.PlaybackStopped += (_, e) => { if (e.Exception != null) Failed?.Invoke(e.Exception); };

        _output.Play();
        _capture.StartRecording();
    }

    /// <summary>Starts or stops playing a copy of the output on the default speakers.</summary>
    public void SetMonitor(bool on)
    {
        if (!IsRunning || _tap == null) return;
        if (on == IsMonitoring) return;
        if (on)
        {
            using var speakers = AudioDevices.DefaultSpeakers();
            // Already playing on the speakers (preview mode): a second copy would just double it.
            if (_outputDevice != null && speakers.ID == _outputDevice.ID) return;
            _tap.Enabled = true;
            _monitorOut = new WasapiOut(speakers, AudioClientShareMode.Shared, useEventSync: true, latency: BufferMs);
            _monitorOut.Init(_tap.Copy);
            _monitorOut.Play();
        }
        else
        {
            _tap.Enabled = false;
            var m = _monitorOut;
            _monitorOut = null;
            m?.Stop();
            m?.Dispose();
        }
    }

    public void SetVoice(Voice voice) => _chain?.Apply(voice);

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var buffer = _buffer;
        if (buffer == null) return;

        // If playback falls behind, drop the backlog instead of letting the delay grow.
        if (buffer.BufferedDuration.TotalMilliseconds > 150)
            buffer.ClearBuffer();

        buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        MeasureLevel(e.Buffer, e.BytesRecorded);
    }

    private void MeasureLevel(byte[] data, int bytes)
    {
        var peak = 0f;
        if (_captureIsFloat)
        {
            for (var i = 0; i + 3 < bytes; i += 4)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(data, i)));
        }
        else
        {
            for (var i = 0; i + 1 < bytes; i += 2)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(data, i)) / 32768f);
        }
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
        _buffer = null;
        _chain = null;
        _tap = null;
        _outputDevice = null;
        chain?.Dispose();

        if (capture != null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.StopRecording();
            capture.Dispose();
        }
        if (output != null)
        {
            output.Stop();
            output.Dispose();
        }
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
            var byteCount = n * 4;
            if (_bytes.Length < byteCount) _bytes = new byte[byteCount * 2];
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer[..n]).CopyTo(_bytes);
            _copyBuffer.AddSamples(_bytes, 0, byteCount);
            return n;
        }
    }
}
