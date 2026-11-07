using NAudio.Dsp;
using NAudio.Wave;

namespace VoiceChanger.Core.Dsp;

/// <summary>
/// Real-time AI voice conversion. Turns the speaker into a different person; the effect chain
/// then runs on top of that. Runs on the CPU only.
///
/// Models work at their own sample rate (16 kHz) in fixed chunks and carry state between
/// chunks. Inference runs on its own thread so a slow chunk never stalls the audio callback:
/// the audio thread only moves samples in and out of two queues. With no model set the stage
/// passes audio through untouched.
///
/// Model names: "llvc-demo-c4" is a one-voice LLVC model (llvc-demo-c4.onnx + .json, none ship today);
/// "meanvc/man" is the shared MeanVC model in the meanvc folder with the man.spk voice.
/// </summary>
public sealed class AiVoiceStage : ISampleProvider, IDisposable
{
    /// <summary>Folder holding the models. Set once by the app.</summary>
    public static string ModelDirectory { get; set; } = "";

    /// <summary>CPU threads given to the model. Measured: 2 is as fast as 4 or 12 and leaves the rest for the game.</summary>
    public static int Threads { get; set; } = 2;

    private readonly ISampleProvider _source;
    private readonly object _gate = new();

    // Model (used by the worker while it runs)
    private IVoiceModel? _model;
    private string? _modelName;
    private float[] _chunk = [];
    private readonly List<float> _converted = new(8192);

    // Queues between the audio thread and the worker (guarded by _gate)
    private StreamResampler? _down, _up;
    private readonly List<float> _inFifo = new(8192);   // model rate, waiting for the worker
    private readonly List<float> _outFifo = new(16384); // engine rate, ready for the audio thread

    private Thread? _worker;
    private readonly AutoResetEvent _work = new(false);
    private volatile bool _stop;

    // Diagnostics
    private readonly System.Diagnostics.Stopwatch _runClock = new();
    private int _runs, _underruns, _missing;
    private int _owed; // output samples played as silence and not yet made up for
    private int _watched, _lowestQueue = int.MaxValue; // how long, and how full, the output queue has been since the last gap
    private double _maxRunMs;

    public AiVoiceStage(ISampleProvider monoSource) => _source = monoSource;

    public WaveFormat WaveFormat => _source.WaveFormat;
    public bool IsActive => _model != null;
    public string? ModelName => _modelName;
    public double AverageRunMs => _runs == 0 ? 0 : _runClock.Elapsed.TotalMilliseconds / _runs;
    public int Runs => _runs;
    /// <summary>Longest single run (ms) since the model was set.</summary>
    public double MaxRunMs => _maxRunMs;
    public double ChunkMs => _model == null ? 0 : 1000.0 * _model.ChunkLen / _model.SampleRate;
    /// <summary>Model-rate samples queued and not yet converted (for tests).</summary>
    public int PendingInput { get { lock (_gate) return _inFifo.Count; } }
    /// <summary>Audio callbacks that found too little converted audio (heard as gaps).</summary>
    public int Underruns => _underruns;
    /// <summary>Engine-rate samples that were missing in those gaps.</summary>
    public int MissingSamples => _missing;

    /// <summary>True when the files for the named model are installed.</summary>
    public static bool IsAvailable(string name)
    {
        var (engine, voice) = Split(name);
        return voice == null
            ? File.Exists(Path.Combine(ModelDirectory, engine + ".onnx"))
            : File.Exists(Path.Combine(ModelDirectory, engine, voice + ".spk"));
    }

    private static (string Engine, string? Voice) Split(string name)
    {
        var slash = name.IndexOf('/');
        return slash < 0 ? (name, null) : (name[..slash], name[(slash + 1)..]);
    }

    /// <summary>Loads the named model, or clears it with null/empty. Voices of an already loaded shared model switch without reloading.</summary>
    public void SetModel(string? name)
    {
        if (string.IsNullOrEmpty(name)) name = null;
        if (name == _modelName) return;

        StopWorker();
        lock (_gate)
        {
            _inFifo.Clear();
            _outFifo.Clear();
            var previous = _modelName;
            _modelName = name;
            if (name == null) { ReleaseModel(); return; }

            var (engine, voice) = Split(name);
            try
            {
                if (voice != null && _model is MeanVcModel shared && previous != null && Split(previous).Engine == engine)
                {
                    shared.SetSpeaker(MeanVcModel.ReadSpeaker(Path.Combine(ModelDirectory, engine, voice + ".spk")));
                }
                else
                {
                    ReleaseModel();
                    if (voice == null)
                    {
                        _model = new LlvcModel(Path.Combine(ModelDirectory, engine + ".onnx"), Threads);
                    }
                    else
                    {
                        var mv = new MeanVcModel(Path.Combine(ModelDirectory, engine));
                        _model = mv;
                        mv.SetSpeaker(MeanVcModel.ReadSpeaker(Path.Combine(ModelDirectory, engine, voice + ".spk")));
                    }
                }
            }
            catch
            {
                ReleaseModel();
                _modelName = null;
                throw;
            }

            _chunk = new float[_model.ChunkLen];
            _down = new StreamResampler(WaveFormat.SampleRate, _model.SampleRate);
            _up = new StreamResampler(_model.SampleRate, WaveFormat.SampleRate);
            // Start with the model's worst-case delay already queued as silence: output then plays at a
            // steady delay and only runs dry if a run takes longer than planned.
            _outFifo.AddRange(new float[(int)((long)_model.DelayLen * WaveFormat.SampleRate / _model.SampleRate)]);
            _runClock.Reset(); _runs = 0; _underruns = 0; _missing = 0; _maxRunMs = 0;
            _owed = 0;
            _watched = 0;
            _lowestQueue = int.MaxValue;
        }
        StartWorker();
    }

    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    public int Read(Span<float> buffer)
    {
        var n = _source.Read(buffer);
        lock (_gate)
        {
            if (_model == null) return n;

            _down!.Process(buffer[..n], _inFifo);
            if (_inFifo.Count >= _chunk.Length) _work.Set();

            var available = Math.Min(n, _outFifo.Count);
            if (available < n) { _underruns++; _missing += n - available; _owed += n - available; }
            for (var i = 0; i < available; i++) buffer[i] = _outFifo[i];
            _outFifo.RemoveRange(0, available);
            for (var i = available; i < n; i++) buffer[i] = 0; // underrun
            if (available < n)
            {
                _watched = 0;
                _lowestQueue = int.MaxValue;
            }
            else if (_owed > 0)
            {
                CatchUp(n);
            }
            return n;
        }
    }

    // Silence played during an underrun pushes everything after it later, for good. Once the model
    // has caught up, skip that much audio to get back to the planned delay. A real skip of speech,
    // but better than talking over people for the rest of the session.
    // The queue rises and falls in bursts as chunks arrive, so only its low point over half a second
    // says how much is truly spare; skipping at a peak would cause the next underrun.
    private void CatchUp(int read)
    {
        _lowestQueue = Math.Min(_lowestQueue, _outFifo.Count);
        _watched += read;
        if (_watched < WaveFormat.SampleRate / 2) return;

        var keep = WaveFormat.SampleRate * 3 / 50; // 60 ms: compared with 40 ms, fewer gaps on a busy laptop
        var skip = Math.Min(_owed, _lowestQueue - keep);
        _watched = 0;
        _lowestQueue = int.MaxValue;
        if (skip <= 0) return;

        var fade = WaveFormat.SampleRate / 200; // 5 ms, so the jump doesn't click
        for (var i = 0; i < fade && skip + i < _outFifo.Count; i++)
        {
            var mix = (i + 1f) / (fade + 1);
            _outFifo[skip + i] = _outFifo[i] * (1 - mix) + _outFifo[skip + i] * mix;
        }
        _outFifo.RemoveRange(0, skip);
        _owed -= skip;
    }

    private void StartWorker()
    {
        _stop = false;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Voxel AI voice", Priority = ThreadPriority.AboveNormal };
        _worker.Start();
    }

    private void StopWorker()
    {
        var w = _worker;
        if (w == null) return;
        _stop = true;
        _work.Set();
        w.Join();
        _worker = null;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    private void WorkerLoop()
    {
        var model = _model!;
        while (!_stop)
        {
            _work.WaitOne();
            while (!_stop)
            {
                lock (_gate)
                {
                    if (_inFifo.Count < _chunk.Length) break;
                    _inFifo.CopyTo(0, _chunk, 0, _chunk.Length);
                    _inFifo.RemoveRange(0, _chunk.Length);
                }

                _converted.Clear();
                var before = _runClock.Elapsed;
                _runClock.Start();
                model.Process(_chunk, _converted);
                _runClock.Stop();
                _runs++;
                _maxRunMs = Math.Max(_maxRunMs, (_runClock.Elapsed - before).TotalMilliseconds);

                lock (_gate) _up!.Process(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_converted), _outFifo);
            }
        }
    }

    private void ReleaseModel()
    {
        _model?.Dispose();
        _model = null;
    }

    public void Dispose()
    {
        StopWorker();
        lock (_gate) { ReleaseModel(); _modelName = null; }
        _work.Dispose();
    }
}

/// <summary>Streaming sample-rate converter: linear interpolation plus a two-stage low-pass to tame aliasing.</summary>
public sealed class StreamResampler
{
    private readonly double _step;         // input samples per output sample
    private readonly bool _filterInput;    // filter the higher-rate side
    private readonly BiQuadFilter _lp1, _lp2;
    private float _prev;
    private double _frac;
    private bool _hasPrev;

    public StreamResampler(int inRate, int outRate)
    {
        _step = (double)inRate / outRate;
        _filterInput = inRate > outRate;
        var filterRate = _filterInput ? inRate : outRate;
        var cutoff = 0.45f * Math.Min(inRate, outRate);
        _lp1 = BiQuadFilter.LowPassFilter(filterRate, cutoff, 0.54f);
        _lp2 = BiQuadFilter.LowPassFilter(filterRate, cutoff, 1.31f);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    public void Process(ReadOnlySpan<float> input, List<float> output)
    {
        foreach (var raw in input)
        {
            var s = _filterInput ? _lp2.Transform(_lp1.Transform(raw)) : raw;
            if (!_hasPrev) { _prev = s; _hasPrev = true; _frac = 0; continue; }
            while (_frac < 1)
            {
                var y = _prev + (s - _prev) * (float)_frac;
                output.Add(_filterInput ? y : _lp2.Transform(_lp1.Transform(y)));
                _frac += _step;
            }
            _frac -= 1;
            _prev = s;
        }
    }
}
