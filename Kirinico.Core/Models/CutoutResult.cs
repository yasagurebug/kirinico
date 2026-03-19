using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class CutoutResult : IDisposable
{
    public CutoutResult(Mat trimapMask, Mat alphaMask, Mat finalRgba, RgbColor resolvedBackgroundColor)
    {
        TrimapMask = trimapMask;
        AlphaMask = alphaMask;
        FinalRgba = finalRgba;
        ResolvedBackgroundColor = resolvedBackgroundColor;
    }

    public Mat TrimapMask { get; }

    public Mat AlphaMask { get; }

    public Mat FinalRgba { get; }

    public RgbColor ResolvedBackgroundColor { get; }

    public void Dispose()
    {
        TrimapMask.Dispose();
        AlphaMask.Dispose();
        FinalRgba.Dispose();
    }
}
