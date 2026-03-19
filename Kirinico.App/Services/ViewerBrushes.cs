using Kirinico.App.Models;
using Brush = System.Windows.Media.Brush;
using BrushMappingMode = System.Windows.Media.BrushMappingMode;
using Color = System.Windows.Media.Color;
using DrawingBrush = System.Windows.Media.DrawingBrush;
using DrawingGroup = System.Windows.Media.DrawingGroup;
using GeometryDrawing = System.Windows.Media.GeometryDrawing;
using RectangleGeometry = System.Windows.Media.RectangleGeometry;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Stretch = System.Windows.Media.Stretch;
using TileMode = System.Windows.Media.TileMode;

namespace Kirinico.App.Services;

public static class ViewerBrushes
{
    private static readonly Brush CheckerBrush = CreateCheckerBrush();
    private static readonly Brush AlphaFixedBrush = CreateFrozenBrush(Color.FromRgb(0, 0, 0));
    private static readonly Brush BlackBrush = CreateFrozenBrush(Color.FromRgb(0, 0, 0));
    private static readonly Brush WhiteBrush = CreateFrozenBrush(Color.FromRgb(255, 255, 255));
    private static readonly Brush RedBrush = CreateFrozenBrush(Color.FromRgb(255, 0, 0));
    private static readonly Brush GreenBrush = CreateFrozenBrush(Color.FromRgb(0, 255, 0));
    private static readonly Brush BlueBrush = CreateFrozenBrush(Color.FromRgb(0, 0, 255));
    private static readonly Brush OriginalBrush = CreateFrozenBrush(Color.FromRgb(32, 34, 38));

    public static Brush Original => OriginalBrush;

    public static Brush AlphaFixed => AlphaFixedBrush;

    public static Brush GetBrush(ViewBackgroundKind kind) => kind switch
    {
        ViewBackgroundKind.Black => BlackBrush,
        ViewBackgroundKind.White => WhiteBrush,
        ViewBackgroundKind.Red => RedBrush,
        ViewBackgroundKind.Green => GreenBrush,
        ViewBackgroundKind.Blue => BlueBrush,
        _ => CheckerBrush,
    };

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush CreateCheckerBrush()
    {
        var light = CreateFrozenBrush(Color.FromRgb(210, 210, 210));
        var dark = CreateFrozenBrush(Color.FromRgb(164, 164, 164));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 12, 12))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new System.Windows.Rect(12, 12, 12, 12))));
        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, 24, 24),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

}
