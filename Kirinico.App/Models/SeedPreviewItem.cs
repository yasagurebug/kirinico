using System.Windows.Media.Imaging;

namespace Kirinico.App.Models;

public sealed class SeedPreviewItem
{
    public required OpenCvSharp.Point SeedPoint { get; init; }

    public required BitmapSource PreviewImage { get; init; }
}
