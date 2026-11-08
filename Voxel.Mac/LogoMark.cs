using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Voxel.Mac;

/// <summary>The Voxel mark: five sound bars forming a V. Size it with Width/Height; colour with Foreground.</summary>
public sealed class LogoMark : UserControl
{
    public LogoMark()
    {
        var path = new Path
        {
            Data = Geometry.Parse("M30,38 V62 M45,30 V82 M60,46 V98 M75,30 V82 M90,38 V62"),
            StrokeThickness = 9,
            StrokeLineCap = PenLineCap.Round,
            [Canvas.LeftProperty] = -24.0,
            [Canvas.TopProperty] = -24.0,
        };
        path.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = new Canvas { Width = 72, Height = 80, Children = { path } } };
    }
}
