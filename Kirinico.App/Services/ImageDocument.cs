using Kirinico.App.Models;
using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.App.Services;

internal sealed class ImageDocument : IDisposable
{
    public Mat? SourceImage { get; private set; }

    public Mat? BackgroundSeedAddMap { get; private set; }

    public Mat? LatestTrimapMask { get; private set; }

    public PreResizeCutoutResult? CachedPreResizeResult { get; private set; }

    public CutoutResult? LatestResult { get; private set; }

    public void ReplaceSourceImage(Mat sourceImage)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);

        ClearAll();
        SourceImage = sourceImage;
        BackgroundSeedAddMap = new Mat(sourceImage.Rows, sourceImage.Cols, MatType.CV_8UC1, Scalar.All(0d));
    }

    public void ClearLatestResult()
    {
        LatestResult?.Dispose();
        LatestResult = null;
    }

    public void ClearLatestTrimapMask()
    {
        LatestTrimapMask?.Dispose();
        LatestTrimapMask = null;
    }

    public void ClearCachedPreResizeResult()
    {
        CachedPreResizeResult?.Dispose();
        CachedPreResizeResult = null;
    }

    public void ReplaceLatestTrimapMask(Mat trimapMask)
    {
        ArgumentNullException.ThrowIfNull(trimapMask);
        ClearLatestTrimapMask();
        LatestTrimapMask = trimapMask;
    }

    public void ReplaceCachedPreResizeResult(PreResizeCutoutResult preResizeResult)
    {
        ArgumentNullException.ThrowIfNull(preResizeResult);
        ClearCachedPreResizeResult();
        CachedPreResizeResult = preResizeResult;
    }

    public void ReplaceLatestResult(CutoutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ClearLatestResult();
        LatestResult = result;
    }

    public Mat CloneSourceImage()
        => SourceImage?.Clone() ?? throw new InvalidOperationException("元画像が読み込まれていません。");

    public ManualEditMaps? CreateManualEditMapsClone()
    {
        if (BackgroundSeedAddMap is null)
        {
            return null;
        }

        return new ManualEditMaps
        {
            BackgroundSeedAddMap = BackgroundSeedAddMap.Clone(),
        };
    }

    public PreResizeCutoutResult? CloneCachedPreResizeResult()
        => CachedPreResizeResult?.Clone();

    public List<AppSettingsSnapshot.SeedPointSnapshot> CollectSeedSnapshots()
    {
        var result = new List<AppSettingsSnapshot.SeedPointSnapshot>();
        if (BackgroundSeedAddMap is null || BackgroundSeedAddMap.Empty())
        {
            return result;
        }

        var indexer = BackgroundSeedAddMap.GetGenericIndexer<byte>();
        for (var y = 0; y < BackgroundSeedAddMap.Rows; y++)
        {
            for (var x = 0; x < BackgroundSeedAddMap.Cols; x++)
            {
                if (indexer[y, x] == 0)
                {
                    continue;
                }

                result.Add(new AppSettingsSnapshot.SeedPointSnapshot
                {
                    X = x,
                    Y = y,
                });
            }
        }

        return result;
    }

    public void RestoreSeedSnapshots(IReadOnlyList<AppSettingsSnapshot.SeedPointSnapshot>? seeds)
    {
        if (BackgroundSeedAddMap is null || BackgroundSeedAddMap.Empty())
        {
            return;
        }

        BackgroundSeedAddMap.SetTo(Scalar.All(0d));
        if (seeds is null)
        {
            return;
        }

        foreach (var seed in seeds)
        {
            if (seed.X < 0 || seed.Y < 0 || seed.X >= BackgroundSeedAddMap.Cols || seed.Y >= BackgroundSeedAddMap.Rows)
            {
                continue;
            }

            BackgroundSeedAddMap.Set(seed.Y, seed.X, 255);
        }
    }

    public bool HasAnyBackgroundSeed()
        => BackgroundSeedAddMap is not null && !BackgroundSeedAddMap.Empty() && Cv2.CountNonZero(BackgroundSeedAddMap) > 0;

    public void ClearProcessingState()
    {
        ClearLatestTrimapMask();
        ClearCachedPreResizeResult();
        ClearLatestResult();
    }

    public void ClearAll()
    {
        ClearProcessingState();

        SourceImage?.Dispose();
        SourceImage = null;

        BackgroundSeedAddMap?.Dispose();
        BackgroundSeedAddMap = null;
    }

    public void Dispose()
    {
        ClearAll();
    }
}
