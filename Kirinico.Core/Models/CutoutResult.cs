using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class CutoutResult : IDisposable
{
    public CutoutResult(Mat alphaMask, Mat finalRgba, RgbColor resolvedBackgroundColor)
    {
        AlphaMask = alphaMask;
        FinalRgba = finalRgba;
        ResolvedBackgroundColor = resolvedBackgroundColor;
    }

    public Mat AlphaMask { get; }

    public Mat FinalRgba { get; }

    public RgbColor ResolvedBackgroundColor { get; }

    public void Dispose()
    {
        AlphaMask.Dispose();
        FinalRgba.Dispose();
    }
}
