using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class PreResizeCutoutResult : IDisposable
{
    public PreResizeCutoutResult(Mat alphaMask, Mat straightBgra, RgbColor resolvedBackgroundColor)
    {
        AlphaMask = alphaMask;
        StraightBgra = straightBgra;
        ResolvedBackgroundColor = resolvedBackgroundColor;
    }

    public Mat AlphaMask { get; }

    public Mat StraightBgra { get; }

    public RgbColor ResolvedBackgroundColor { get; }

    public PreResizeCutoutResult Clone()
        => new(AlphaMask.Clone(), StraightBgra.Clone(), ResolvedBackgroundColor);

    public void Dispose()
    {
        AlphaMask.Dispose();
        StraightBgra.Dispose();
    }
}
