using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class TrimapPreparationResult : IDisposable
{
    public TrimapPreparationResult(Mat originalBgr, Mat referenceBgr, Mat trimapMask, RgbColor resolvedBackgroundColor)
    {
        OriginalBgr = originalBgr;
        ReferenceBgr = referenceBgr;
        TrimapMask = trimapMask;
        ResolvedBackgroundColor = resolvedBackgroundColor;
    }

    public Mat OriginalBgr { get; }

    public Mat ReferenceBgr { get; }

    public Mat TrimapMask { get; }

    public RgbColor ResolvedBackgroundColor { get; }

    public TrimapPreparationResult Clone()
        => new(OriginalBgr.Clone(), ReferenceBgr.Clone(), TrimapMask.Clone(), ResolvedBackgroundColor);

    public void Dispose()
    {
        OriginalBgr.Dispose();
        ReferenceBgr.Dispose();
        TrimapMask.Dispose();
    }
}
