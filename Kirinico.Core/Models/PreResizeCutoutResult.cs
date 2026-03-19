using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class PreResizeCutoutResult : IDisposable
{
    public PreResizeCutoutResult(Mat trimapMask, Mat alphaMask, Mat originalBgr, RgbColor resolvedBackgroundColor)
    {
        TrimapMask = trimapMask;
        AlphaMask = alphaMask;
        OriginalBgr = originalBgr;
        ResolvedBackgroundColor = resolvedBackgroundColor;
    }

    public Mat TrimapMask { get; }

    public Mat AlphaMask { get; }

    public Mat OriginalBgr { get; }

    public RgbColor ResolvedBackgroundColor { get; }

    public PreResizeCutoutResult Clone()
        => new(TrimapMask.Clone(), AlphaMask.Clone(), OriginalBgr.Clone(), ResolvedBackgroundColor);

    public void Dispose()
    {
        TrimapMask.Dispose();
        AlphaMask.Dispose();
        OriginalBgr.Dispose();
    }
}
