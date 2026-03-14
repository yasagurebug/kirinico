using CommunityToolkit.Mvvm.ComponentModel;

namespace Kirinico.App.ViewModels;

public sealed class ViewportState : ObservableObject
{
    private double _zoom = 1d;
    private double _offsetX;
    private double _offsetY;

    public double Zoom => _zoom;

    public double OffsetX => _offsetX;

    public double OffsetY => _offsetY;

    public void Update(double zoom, double offsetX, double offsetY)
    {
        var nextZoom = Math.Clamp(zoom, 0.1d, 12d);
        var nextOffsetX = Math.Max(0d, offsetX);
        var nextOffsetY = Math.Max(0d, offsetY);

        if (AreClose(_zoom, nextZoom) &&
            AreClose(_offsetX, nextOffsetX) &&
            AreClose(_offsetY, nextOffsetY))
        {
            return;
        }

        _zoom = nextZoom;
        _offsetX = nextOffsetX;
        _offsetY = nextOffsetY;
        OnPropertyChanged(string.Empty);
    }

    public void Reset() => Update(1d, 0d, 0d);

    private static bool AreClose(double left, double right) => Math.Abs(left - right) < 0.0001d;
}
