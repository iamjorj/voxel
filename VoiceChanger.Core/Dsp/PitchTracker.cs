namespace VoiceChanger.Core.Dsp;

/// <summary>
/// Cheap fundamental-frequency estimate by normalized autocorrelation over one analysis frame.
/// Good enough for autotune and for driving a vocoder carrier.
/// </summary>
public sealed class PitchTracker
{
    private readonly int _minLag, _maxLag;

    public PitchTracker(int sampleRate, float minHz = 70, float maxHz = 500)
    {
        SampleRate = sampleRate;
        _minLag = (int)(sampleRate / maxHz);
        _maxLag = (int)(sampleRate / minHz);
    }

    public int SampleRate { get; }

    /// <summary>Last detected pitch in Hz, or 0 when the frame was unvoiced / silent.</summary>
    public float F0 { get; private set; }

    public void Analyze(ReadOnlySpan<float> frame)
    {
        var n = frame.Length;
        if (n < _maxLag * 2) { F0 = 0; return; }

        double energy = 0;
        for (var i = 0; i < n; i++) energy += frame[i] * frame[i];
        if (energy / n < 1e-6) { F0 = 0; return; } // silence

        // Coarse search every 2 lags for the global best, then take the SHORTEST lag that comes
        // close to it: multiples of the true period correlate almost as well, and picking the
        // shortest avoids landing an octave (or two) too low.
        var bestLag = 0;
        double best = 0;
        for (var lag = _minLag; lag <= _maxLag; lag += 2)
        {
            var r = Correlate(frame, lag, n);
            if (r > best) { best = r; bestLag = lag; }
        }
        if (best < 0.6) { F0 = 0; return; }

        var chosen = bestLag;
        for (var lag = _minLag; lag < bestLag; lag += 2)
        {
            if (Correlate(frame, lag, n) >= best * 0.9) { chosen = lag; break; }
        }
        double chosenScore = 0;
        var refined = chosen;
        for (var lag = Math.Max(_minLag, chosen - 2); lag <= Math.Min(_maxLag, chosen + 2); lag++)
        {
            var r = Correlate(frame, lag, n);
            if (r > chosenScore) { chosenScore = r; refined = lag; }
        }

        F0 = (float)SampleRate / refined;
    }

    private static double Correlate(ReadOnlySpan<float> x, int lag, int n)
    {
        double num = 0, d1 = 0, d2 = 0;
        var len = n - lag;
        for (var i = 0; i < len; i++)
        {
            var a = x[i];
            var b = x[i + lag];
            num += a * b; d1 += a * a; d2 += b * b;
        }
        var den = Math.Sqrt(d1 * d2);
        return den > 0 ? num / den : 0;
    }
}
