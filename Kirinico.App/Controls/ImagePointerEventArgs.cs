using Point = System.Windows.Point;

namespace Kirinico.App.Controls;

public sealed class ImagePointerEventArgs : EventArgs
{
    public ImagePointerEventArgs(Point sourcePoint, Point displayPoint, bool leftButtonPressed)
    {
        SourcePoint = sourcePoint;
        DisplayPoint = displayPoint;
        LeftButtonPressed = leftButtonPressed;
    }

    public Point SourcePoint { get; }

    public Point DisplayPoint { get; }

    public bool LeftButtonPressed { get; }
}
