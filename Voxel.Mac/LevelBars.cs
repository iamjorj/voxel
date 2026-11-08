using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Voxel.Mac;

/// <summary>Five bars that dance with the voice level, echoing the logo.</summary>
public sealed class LevelBars : UserControl
{
    private const double Lowest = 4, Highest = 24;
    // Each bar reacts a little differently so the meter looks alive rather than mechanical.
    private static readonly double[] Weights = [0.55, 0.85, 1.0, 0.85, 0.55];
    private readonly Rectangle[] _bars;
    private readonly double[] _current = new double[5];
    private readonly Random _rng = new();

    public static readonly StyledProperty<double> LevelProperty = AvaloniaProperty.Register<LevelBars, double>(nameof(Level));

    /// <summary>Voice level 0..1.</summary>
    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public LevelBars()
    {
        Height = 24;
        var accent = new SolidColorBrush(Color.Parse("#7C6CF6"));
        _bars = [.. new[] { 6.0, 10, 14, 10, 6 }.Select((h, i) => new Rectangle
        {
            Width = 4, Height = h, RadiusX = 2, RadiusY = 2, Fill = accent,
            Margin = new Thickness(0, 0, i < 4 ? 3 : 0, 0), VerticalAlignment = VerticalAlignment.Center,
        })];
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.AddRange(_bars);
        Content = row;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LevelProperty) Render();
    }

    private void Render()
    {
        var level = Math.Clamp(Level, 0, 1);
        for (var i = 0; i < _bars.Length; i++)
        {
            var jitter = level > 0.05 ? 0.85 + _rng.NextDouble() * 0.3 : 1;
            var target = Lowest + (Highest - Lowest) * level * Weights[i] * jitter;
            // Rise fast, fall slow.
            _current[i] = target > _current[i] ? target : _current[i] + (target - _current[i]) * 0.35;
            _bars[i].Height = Math.Clamp(_current[i], Lowest, Highest);
        }
    }
}
