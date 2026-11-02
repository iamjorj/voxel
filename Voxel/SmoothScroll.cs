using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Voxel;

/// <summary>
/// Attached property that replaces WPF's jumpy wheel scrolling with a short eased glide.
/// One wheel notch moves about one row. Usage: app:SmoothScroll.IsEnabled="True" on a ScrollViewer.
/// </summary>
public static class SmoothScroll
{
    private const double PixelsPerNotch = 60;
    private const double Ease = 0.2; // fraction of the remaining distance covered per frame

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer && (bool)e.NewValue) _ = new Glider(viewer);
    }

    private sealed class Glider
    {
        private readonly ScrollViewer _viewer;
        private double _target;
        private bool _running;

        public Glider(ScrollViewer viewer)
        {
            _viewer = viewer;
            _viewer.PreviewMouseWheel += OnWheel;
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewer.ScrollableHeight <= 0) return;
            if (!_running) _target = _viewer.VerticalOffset;
            _target = Math.Clamp(_target - e.Delta / 120.0 * PixelsPerNotch, 0, _viewer.ScrollableHeight);
            e.Handled = true;
            if (_running) return;
            _running = true;
            CompositionTarget.Rendering += OnFrame;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var current = _viewer.VerticalOffset;
            var next = current + (_target - current) * Ease;
            if (Math.Abs(_target - next) < 0.5)
            {
                next = _target;
                _running = false;
                CompositionTarget.Rendering -= OnFrame;
            }
            _viewer.ScrollToVerticalOffset(next);
        }
    }
}
