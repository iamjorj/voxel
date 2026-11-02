using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Voxel;

public partial class LevelBars : UserControl
{
    private const double MinHeight = 4, MaxHeight = 24;
    // Each bar reacts a little differently so the meter looks alive rather than mechanical.
    private static readonly double[] Weights = [0.55, 0.85, 1.0, 0.85, 0.55];
    private readonly Rectangle[] _bars;
    private readonly double[] _current = new double[5];
    private readonly Random _rng = new();

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelBars), new PropertyMetadata(0.0, (d, _) => ((LevelBars)d).Render()));

    public LevelBars()
    {
        InitializeComponent();
        _bars = [B1, B2, B3, B4, B5];
    }

    /// <summary>Voice level 0..1.</summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    private void Render()
    {
        var level = Math.Clamp(Level, 0, 1);
        for (var i = 0; i < _bars.Length; i++)
        {
            var jitter = level > 0.05 ? 0.85 + _rng.NextDouble() * 0.3 : 1;
            var target = MinHeight + (MaxHeight - MinHeight) * level * Weights[i] * jitter;
            // Rise fast, fall slow.
            _current[i] = target > _current[i] ? target : _current[i] + (target - _current[i]) * 0.35;
            _bars[i].Height = Math.Clamp(_current[i], MinHeight, MaxHeight);
        }
    }
}
