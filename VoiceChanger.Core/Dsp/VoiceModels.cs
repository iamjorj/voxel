using System.Text.Json;
using Microsoft.ML.OnnxRuntime;

namespace VoiceChanger.Core.Dsp;

/// <summary>A streaming AI voice model run by <see cref="AiVoiceStage"/> on its worker thread.</summary>
internal interface IVoiceModel : IDisposable
{
    /// <summary>Rate the model works at.</summary>
    int SampleRate { get; }
    /// <summary>Model-rate samples consumed by each <see cref="Process"/> call.</summary>
    int ChunkLen { get; }
    /// <summary>Model-rate delay to build in: output may arrive this late (model look-ahead, bursts and run time).</summary>
    int DelayLen { get; }
    /// <summary>Converts exactly <see cref="ChunkLen"/> samples; appends whatever output is ready (may be none).</summary>
    void Process(float[] chunk, List<float> output);
}

/// <summary>Pins a float array as an ONNX tensor so runs read and write it without copies.</summary>
internal sealed class Tensor : IDisposable
{
    public readonly float[] Data;
    public readonly OrtValue Value;

    public Tensor(params long[] shape)
    {
        Data = new float[shape.Aggregate(1L, (a, b) => a * b)];
        Value = OrtValue.CreateTensorValueFromMemory(Data, shape);
    }

    public static Tensor For(NodeMetadata meta) => new(meta.Dimensions.Select(d => (long)d).ToArray());

    public void Dispose() => Value.Dispose();
}

internal static class Onnx
{
    public static InferenceSession Load(string path, int threads)
    {
        var options = new SessionOptions { IntraOpNumThreads = threads, InterOpNumThreads = 1 };
        // Denormal floats in recurrent state would make x86 math crawl; flush them to zero.
        options.AddSessionConfigEntry("session.set_denormal_as_zero", "1");
        return new InferenceSession(path, options);
    }
}

/// <summary>
/// LLVC (Koe AI, MIT): converts any speaker into the one voice it was trained on.
/// Fixed chunks with a little context from the previous chunk and recurrent state in four buffers.
/// </summary>
internal sealed class LlvcModel : IVoiceModel
{
    private static readonly string[] InputNames = ["chunk", "enc_buf", "dec_buf", "out_buf", "pre_ctx"];
    private static readonly string[] OutputNames = ["out", "enc_buf_out", "dec_buf_out", "out_buf_out", "pre_ctx_out"];

    private readonly InferenceSession _session;
    private readonly RunOptions _run = new();
    private readonly int _context;
    private readonly Tensor _in, _out;
    private Tensor[] _state, _stateOut;
    private readonly float[] _prevTail;

    public LlvcModel(string onnxPath, int threads)
    {
        var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(Path.ChangeExtension(onnxPath, ".json")))
                   ?? throw new InvalidOperationException("Bad model metadata.");
        _session = Onnx.Load(onnxPath, threads);
        SampleRate = meta.sample_rate;
        ChunkLen = meta.chunk_len;
        DelayLen = 2 * meta.chunk_len; // one chunk to collect, one of slack for the run
        _context = meta.context;
        _in = new Tensor(1, 1, _context + ChunkLen);
        _out = new Tensor(1, 1, ChunkLen);
        _prevTail = new float[_context];
        int[][] dims = [meta.enc_buf, meta.dec_buf, meta.out_buf, meta.pre_ctx];
        _state = dims.Select(d => new Tensor(d.Select(x => (long)x).ToArray())).ToArray();
        _stateOut = dims.Select(d => new Tensor(d.Select(x => (long)x).ToArray())).ToArray();

        // A session's first runs are several times slower (memory planning); get them out of the way
        // here instead of as gaps in the first second of speech. Then start from clean state.
        for (var i = 0; i < 3; i++) Process(new float[ChunkLen], []);
        foreach (var t in _state) Array.Clear(t.Data);
        Array.Clear(_prevTail);
    }

    public int SampleRate { get; }
    public int ChunkLen { get; }
    public int DelayLen { get; }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    public void Process(float[] chunk, List<float> output)
    {
        // Input = last `context` samples of the previous chunk, then this chunk.
        Array.Copy(_prevTail, 0, _in.Data, 0, _context);
        Array.Copy(chunk, 0, _in.Data, _context, ChunkLen);
        Array.Copy(_in.Data, _in.Data.Length - _context, _prevTail, 0, _context);

        OrtValue[] inputs = [_in.Value, .. _state.Select(t => t.Value)];
        OrtValue[] outputs = [_out.Value, .. _stateOut.Select(t => t.Value)];
        _session.Run(_run, InputNames, inputs, OutputNames, outputs);
        (_state, _stateOut) = (_stateOut, _state);
        output.AddRange(_out.Data);
    }

    public void Dispose()
    {
        _session.Dispose();
        _run.Dispose();
        foreach (var t in _state.Concat(_stateOut).Append(_in).Append(_out)) t.Dispose();
    }

    private sealed record Meta(int sample_rate, int L, int chunk_len, int context,
                               int[] enc_buf, int[] dec_buf, int[] out_buf, int[] pre_ctx);
}

/// <summary>
/// MeanVC2 (ASLP-lab, Apache-2.0): zero-shot voice conversion. One shared model turns any
/// speaker into the voice described by a 256-number speaker embedding, so each voice is a 1 KB
/// file and switching voices needs no reload. Three ONNX parts:
///   asr   — 205 ms window of audio (hop 160 ms) → 4 content frames (what is being said)
///   vc    — 16 content frames (120 ms + 40 ms look-ahead) → 12 mel frames in the target voice
///   vocos — mel frames → waveform
/// research/meanvc/stream_ort.py is the reference for this loop; research/meanvc/export_meanvc.py
/// builds the files.
/// </summary>
internal sealed class MeanVcModel : IVoiceModel
{
    private const int BnDim = 256, Mels = 80;
    private static readonly string[] AsrIn = ["wav", "offset", "rcs", "att", "cnn"];
    private static readonly string[] AsrOut = ["bn", "att_out", "cnn_out"];
    private static readonly string[] VcIn = ["noise", "cond", "spk", "steps", "k0", "v0", "k1", "v1", "k2", "v2", "k3", "v3"];
    private static readonly string[] VcOut = ["mel", "k0_out", "v0_out", "k1_out", "v1_out", "k2_out", "v2_out", "k3_out", "v3_out"];

    private readonly InferenceSession _asr, _vc, _vocos;
    private readonly RunOptions _run = new();
    private readonly Meta _m;
    private readonly int _seq;
    private readonly Random _random = new();

    // Content encoder: sliding audio window, streaming caches, absolute frame offset
    private readonly float[] _audio;
    private int _audioFill;
    private readonly Tensor _wav, _bn;
    private Tensor _att, _attOut, _cnn, _cnnOut;
    private readonly long[] _offset = new long[1], _rcs = new long[1];
    private readonly OrtValue _offsetValue, _rcsValue;
    private readonly float[] _prevBn = new float[BnDim];
    private bool _hasPrevBn;
    private readonly float[] _frames;

    // Content frames waiting for the converter (10 ms each)
    private readonly float[] _bnQueue;
    private int _bnCount;

    // Converter: noise, condition, speaker, attention caches
    private readonly Tensor _noise, _cond, _spk, _mel;
    private Tensor[] _kv, _kvOut;
    private readonly long[] _steps = new long[1];
    private readonly OrtValue _stepsValue;
    private readonly float[] _noiseTail;
    private bool _hasNoiseTail;
    private long _stepCount;

    // Vocoder: the last mel frames are fed again for continuity; the seam is crossfaded
    private readonly Tensor _melFirst, _melNext, _wavFirst, _wavNext;
    private readonly float[] _melTail, _lastWav, _fadeIn;
    private bool _hasMelTail, _hasLastWav;

    /// <summary>One CPU thread per run. Measured: the runs are small, and with two the slowest run under load
    /// doubled (190 ms vs 90 ms) while the average barely moved.</summary>
    private const int Threads = 1;

    public MeanVcModel(string directory)
    {
        _m = JsonSerializer.Deserialize<Meta>(File.ReadAllText(Path.Combine(directory, "meanvc.json")))
             ?? throw new InvalidOperationException("Bad MeanVC metadata.");
        _asr = Onnx.Load(Path.Combine(directory, "asr.onnx"), Threads);
        _vc = Onnx.Load(Path.Combine(directory, "vc.onnx"), Threads);
        _vocos = Onnx.Load(Path.Combine(directory, "vocos.onnx"), Threads);
        _seq = _m.chunk + _m.block;

        _audio = new float[_m.asr_samples + FeedLen];
        _wav = new Tensor(1, _m.asr_samples);
        _bn = Tensor.For(_asr.OutputMetadata["bn"]);
        _frames = new float[_bn.Data.Length + BnDim];
        _att = Tensor.For(_asr.InputMetadata["att"]);
        _attOut = Tensor.For(_asr.InputMetadata["att"]);
        _cnn = Tensor.For(_asr.InputMetadata["cnn"]);
        _cnnOut = Tensor.For(_asr.InputMetadata["cnn"]);
        _offsetValue = OrtValue.CreateTensorValueFromMemory(_offset, []);
        _rcsValue = OrtValue.CreateTensorValueFromMemory(_rcs, []);
        _rcs[0] = _m.asr_cache;
        _bnQueue = new float[4 * _seq * BnDim];

        _noise = new Tensor(1, _seq, Mels);
        _cond = new Tensor(1, _seq, BnDim);
        _spk = new Tensor(1, BnDim);
        _mel = new Tensor(1, Mels, _m.chunk);
        _kv = VcIn[4..].Select(n => Tensor.For(_vc.InputMetadata[n])).ToArray();
        _kvOut = VcIn[4..].Select(n => Tensor.For(_vc.InputMetadata[n])).ToArray();
        _stepsValue = OrtValue.CreateTensorValueFromMemory(_steps, [1]);
        _noiseTail = new float[_m.block * Mels];

        var ov = _m.vocoder_overlap;
        _melFirst = new Tensor(1, Mels, _m.chunk);
        _melNext = new Tensor(1, Mels, _m.chunk + ov);
        _wavFirst = new Tensor(1, (_m.chunk - 1) * _m.hop);
        _wavNext = new Tensor(1, (_m.chunk + ov - 1) * _m.hop);
        _melTail = new float[Mels * ov];
        _lastWav = new float[_m.hop];
        _fadeIn = Enumerable.Range(0, _m.hop).Select(i => i / (float)(_m.hop - 1)).ToArray();

        // A session's first runs are several times slower (memory planning); get them out of the way
        // here instead of as gaps in the first second of speech. One second of silence runs every part.
        Reset();
        var silence = new float[FeedLen];
        for (var i = 0; i < _m.sample_rate / FeedLen; i++) Process(silence, []);
        Reset();
    }

    /// <summary>Fed in 20 ms pieces, like the mic delivers them, so the encoder starts the moment its window is full.</summary>
    private const int FeedLen = 320;

    /// <summary>Extra delay on top of the model's own (meanvc.json delay_ms) for run time: one encoder run plus two
    /// converter runs, with room for the odd slow run on a busy PC (measured: 60 left a 10 ms gap or two per minute).</summary>
    private const int RunMarginMs = 80;

    public int SampleRate => _m.sample_rate;
    public int ChunkLen => FeedLen;
    public int DelayLen => (_m.delay_ms + RunMarginMs) * _m.sample_rate / 1000;

    /// <summary>Switches to another voice (a 256-float speaker embedding) and starts a fresh stream.</summary>
    public void SetSpeaker(float[] embedding)
    {
        if (embedding.Length != BnDim) throw new ArgumentException($"Speaker embedding must have {BnDim} values.");
        Array.Copy(embedding, _spk.Data, BnDim);
        Reset();
    }

    /// <summary>Reads a speaker file: 256 little-endian float32 values.</summary>
    public static float[] ReadSpeaker(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var values = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * 4);
        return values;
    }

    private void Reset()
    {
        _audioFill = 0;
        _offset[0] = _m.asr_offset_init;
        Array.Clear(_att.Data); Array.Clear(_cnn.Data);
        _hasPrevBn = false;
        _bnCount = 0;
        foreach (var t in _kv) Array.Clear(t.Data);
        _stepCount = 0;
        _hasNoiseTail = _hasMelTail = _hasLastWav = false;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    public void Process(float[] chunk, List<float> output)
    {
        Array.Copy(chunk, 0, _audio, _audioFill, chunk.Length);
        _audioFill += chunk.Length;
        while (_audioFill >= _m.asr_samples)
        {
            EncodeContent();
            Array.Copy(_audio, _m.asr_hop, _audio, 0, _audioFill - _m.asr_hop);
            _audioFill -= _m.asr_hop;

            while (_bnCount >= _seq)
            {
                Convert(output);
                Array.Copy(_bnQueue, _m.chunk * BnDim, _bnQueue, 0, (_bnCount - _m.chunk) * BnDim);
                _bnCount -= _m.chunk;
            }
        }
    }

    /// <summary>One encoder run: content frames of 40 ms, each stretched to four of 10 ms.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    private void EncodeContent()
    {
        if (_offset[0] >= 4000) _offset[0] = Math.Max(_m.asr_cache, _offset[0] - 4000);
        Array.Copy(_audio, _wav.Data, _m.asr_samples);
        _asr.Run(_run, AsrIn, [_wav.Value, _offsetValue, _rcsValue, _att.Value, _cnn.Value],
                 AsrOut, [_bn.Value, _attOut.Value, _cnnOut.Value]);
        (_att, _attOut) = (_attOut, _att);
        (_cnn, _cnnOut) = (_cnnOut, _cnn);
        _offset[0] += _m.asr_offset_step;

        // Previous run's last frame, then this run's frames; linear interpolation with aligned corners
        // to 4 points per frame, plus the first point, which belongs to the previous run and is dropped.
        var got = _bn.Data.Length / BnDim;
        var n = 0;
        if (_hasPrevBn) Array.Copy(_prevBn, 0, _frames, n++ * BnDim, BnDim);
        Array.Copy(_bn.Data, 0, _frames, n * BnDim, got * BnDim);
        n += got;
        Array.Copy(_bn.Data, (got - 1) * BnDim, _prevBn, 0, BnDim);
        _hasPrevBn = true;

        var stretched = 4 * got;
        for (var i = 1; i <= stretched; i++)
        {
            var pos = i * (n - 1) / (double)stretched;
            var i0 = Math.Min((int)pos, n - 2);
            var w = (float)(pos - i0);
            var dst = _bnCount++ * BnDim;
            for (var d = 0; d < BnDim; d++)
                _bnQueue[dst + d] = _frames[i0 * BnDim + d] * (1 - w) + _frames[(i0 + 1) * BnDim + d] * w;
        }
    }

    /// <summary>One converter run (120 ms of mel) plus the vocoder.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] // no slow first-tier code mid-stream
    private void Convert(List<float> output)
    {
        // Fresh noise for the flow; its look-ahead block is reused as the start of the next run.
        var noise = _noise.Data;
        for (var i = 0; i < noise.Length; i += 2)
        {
            // Box-Muller: two standard normal values per pair of uniforms
            var r = Math.Sqrt(-2 * Math.Log(1 - _random.NextDouble()));
            var a = 2 * Math.PI * _random.NextDouble();
            noise[i] = (float)(r * Math.Cos(a));
            if (i + 1 < noise.Length) noise[i + 1] = (float)(r * Math.Sin(a));
        }
        if (_hasNoiseTail) Array.Copy(_noiseTail, noise, _noiseTail.Length);
        Array.Copy(noise, noise.Length - _noiseTail.Length, _noiseTail, 0, _noiseTail.Length);
        _hasNoiseTail = true;

        Array.Copy(_bnQueue, _cond.Data, _seq * BnDim);
        _steps[0] = Math.Min(_stepCount++, 1000);
        _vc.Run(_run, VcIn, [_noise.Value, _cond.Value, _spk.Value, _stepsValue, .. _kv.Select(t => t.Value)],
                VcOut, [_mel.Value, .. _kvOut.Select(t => t.Value)]);
        (_kv, _kvOut) = (_kvOut, _kv);

        // Vocoder input: [previous tail, new mel] per mel channel (layout [1, 80, T]).
        var ov = _m.vocoder_overlap;
        var (melIn, wavOut) = _hasMelTail ? (_melNext, _wavNext) : (_melFirst, _wavFirst);
        var t = _hasMelTail ? _m.chunk + ov : _m.chunk;
        var lead = _hasMelTail ? ov : 0;
        for (var c = 0; c < Mels; c++)
        {
            if (_hasMelTail) Array.Copy(_melTail, c * ov, melIn.Data, c * t, ov);
            Array.Copy(_mel.Data, c * _m.chunk, melIn.Data, c * t + lead, _m.chunk);
            Array.Copy(melIn.Data, c * t + t - ov, _melTail, c * ov, ov);
        }
        _hasMelTail = true;
        _vocos.Run(_run, ["mel"], [melIn.Value], ["wav"], [wavOut.Value]);

        var wav = wavOut.Data;
        var hop = _m.hop;
        if (_hasLastWav)
        {
            for (var i = 0; i < hop; i++) output.Add(_lastWav[i] * _fadeIn[hop - 1 - i] + wav[i] * _fadeIn[i]);
            for (var i = hop; i < wav.Length - hop; i++) output.Add(wav[i]);
        }
        else
        {
            for (var i = 0; i < wav.Length - hop; i++) output.Add(wav[i]);
        }
        Array.Copy(wav, wav.Length - hop, _lastWav, 0, hop);
        _hasLastWav = true;
    }

    public void Dispose()
    {
        _asr.Dispose(); _vc.Dispose(); _vocos.Dispose(); _run.Dispose();
        _offsetValue.Dispose(); _rcsValue.Dispose(); _stepsValue.Dispose();
        foreach (var t in new[] { _wav, _bn, _att, _attOut, _cnn, _cnnOut, _noise, _cond, _spk, _mel,
                                  _melFirst, _melNext, _wavFirst, _wavNext }.Concat(_kv).Concat(_kvOut))
            t.Dispose();
    }

    private sealed record Meta(string engine, int sample_rate, int asr_samples, int asr_hop, int asr_cache,
                               int asr_offset_init, int asr_offset_step, int chunk, int block, int[] cache_lens,
                               int heads, int head_dim, int vocoder_overlap, int hop, int delay_ms);
}
