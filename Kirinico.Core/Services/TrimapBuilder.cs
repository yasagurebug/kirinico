using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

internal static class TrimapBuilder
{
    public static TrimapPreparationResult Prepare(
        Mat sourceBgr,
        CutoutParameters parameters,
        ManualEditMaps? manualMaps,
        IProgress<ProcessingProgress>? progress)
    {
        ValidateSource(sourceBgr);
        ArgumentNullException.ThrowIfNull(parameters);

        progress?.Report(new ProcessingProgress(10d, "参照画像を生成"));
        var referenceBgr = BuildReferenceImage(sourceBgr, parameters);

        try
        {
            var seeds = parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ManualSeed
                ? BackgroundSampler.CollectBackgroundSeeds(referenceBgr, manualMaps?.BackgroundSeedAddMap)
                : [];

            if (parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ManualSeed && seeds.Count == 0)
            {
                throw new InvalidOperationException("背景seedがありません。");
            }

            var backgroundColor = BackgroundSampler.ResolveBackgroundColor(parameters, seeds);

            progress?.Report(new ProcessingProgress(20d, "粗マスクを生成"));
            using var backgroundMask0 = BuildBackgroundMask(referenceBgr, seeds, backgroundColor, parameters);

            progress?.Report(new ProcessingProgress(25d, "背景側ノイズを整形"));
            using var backgroundMask1 = backgroundMask0.Clone();
            CleanupBackgroundMask(backgroundMask1, parameters.Internal.BackgroundThreshold);

            using var backgroundDistance = ComputeDistanceFromBackground(backgroundMask1);
            using var foregroundMask0 = BuildForegroundMask(referenceBgr, backgroundMask1, backgroundDistance, backgroundColor, parameters);
            progress?.Report(new ProcessingProgress(28d, "trimap を生成"));
            var trimapMask = BuildTrimap(backgroundMask1, foregroundMask0);
            return new TrimapPreparationResult(sourceBgr.Clone(), referenceBgr, trimapMask, backgroundColor);
        }
        catch
        {
            referenceBgr.Dispose();
            throw;
        }
    }

    private static void ValidateSource(Mat sourceBgr)
    {
        ArgumentNullException.ThrowIfNull(sourceBgr);

        if (sourceBgr.Empty())
        {
            throw new ArgumentException("Source image is empty.", nameof(sourceBgr));
        }

        if (sourceBgr.Type() != MatType.CV_8UC3)
        {
            throw new ArgumentException("Source image must be BGR 8-bit.", nameof(sourceBgr));
        }
    }

    private static Mat BuildReferenceImage(Mat sourceBgr, CutoutParameters parameters)
    {
        var preprocess = parameters.Internal.Preprocess;
        var radius = (int)Math.Round(Lerp(preprocess.DenoiseRadiusMin, preprocess.DenoiseRadiusMax, parameters.DenoiseStrength));
        var sigma = Lerp((float)preprocess.DenoiseSigmaMin, (float)preprocess.DenoiseSigmaMax, (float)parameters.DenoiseStrength);
        if (radius <= 0 || sigma <= 0.0001f)
        {
            return sourceBgr.Clone();
        }

        var kernelSize = Math.Max(1, (radius * 2) + 1);
        if ((kernelSize & 1) == 0)
        {
            kernelSize++;
        }

        var result = new Mat();
        Cv2.GaussianBlur(sourceBgr, result, new Size(kernelSize, kernelSize), sigma, sigma, BorderTypes.Reflect101);
        return result;
    }

    private static Mat BuildBackgroundMask(Mat sourceBgr, IReadOnlyList<BackgroundSampler.SeedInfo> seeds, RgbColor background, CutoutParameters parameters)
    {
        var threshold = ComputeBackgroundThreshold(parameters);
        if (parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ColorRange)
        {
            return BuildColorRangeBackgroundMask(sourceBgr, background, threshold);
        }

        var backgroundMask = new Mat(sourceBgr.Rows, sourceBgr.Cols, MatType.CV_8UC1, Scalar.Black);
        foreach (var seed in seeds)
        {
            using var seedMask = GrowBackgroundRegion(sourceBgr, seed, threshold);
            Cv2.BitwiseOr(backgroundMask, seedMask, backgroundMask);
        }

        return backgroundMask;
    }

    private static Mat BuildForegroundMask(Mat sourceBgr, Mat backgroundMask1, Mat backgroundDistance, RgbColor background, CutoutParameters parameters)
    {
        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var result = new Mat(sourceBgr.Rows, sourceBgr.Cols, MatType.CV_8UC1, Scalar.Black);
        var distanceIndexer = backgroundDistance.GetGenericIndexer<float>();
        var backgroundIndexer = backgroundMask1.GetGenericIndexer<byte>();
        var resultIndexer = result.GetGenericIndexer<byte>();
        var maxContourWidth = ComputeMaxContourWidth(parameters);
        var distanceFromBackgroundOnly = parameters.DistanceFromBackgroundOnly;

        float threshold = 0f;
        float foregroundThreshold = 0f;

        if (!distanceFromBackgroundOnly)
        {
            threshold = ComputeBackgroundThreshold(parameters);
            foregroundThreshold = threshold + ComputeForegroundDelta(parameters);
        }

        for (var y = 0; y < sourceBgr.Rows; y++)
        {
            for (var x = 0; x < sourceBgr.Cols; x++)
            {
                if (backgroundIndexer[y, x] != 0)
                {
                    continue;
                }

                if (distanceFromBackgroundOnly)
                {
                    if (distanceIndexer[y, x] >= maxContourWidth)
                    {
                        resultIndexer[y, x] = 255;
                    }

                    continue;
                }

                var colorDistance = ComputeColorDistance(sourceIndexer[y, x], background);
                if (colorDistance >= foregroundThreshold || distanceIndexer[y, x] >= maxContourWidth)
                {
                    resultIndexer[y, x] = 255;
                }
            }
        }

        return result;
    }

    private static Mat BuildColorRangeBackgroundMask(Mat sourceBgr, RgbColor background, float threshold)
    {
        var result = new Mat(sourceBgr.Rows, sourceBgr.Cols, MatType.CV_8UC1, Scalar.Black);
        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var resultIndexer = result.GetGenericIndexer<byte>();

        for (var y = 0; y < sourceBgr.Rows; y++)
        {
            for (var x = 0; x < sourceBgr.Cols; x++)
            {
                if (ComputeColorDistance(sourceIndexer[y, x], background) <= threshold)
                {
                    resultIndexer[y, x] = 255;
                }
            }
        }

        return result;
    }

    private static Mat GrowBackgroundRegion(Mat sourceBgr, BackgroundSampler.SeedInfo seed, float threshold)
    {
        using var mask = new Mat(sourceBgr.Rows + 2, sourceBgr.Cols + 2, MatType.CV_8UC1, Scalar.Black);
        var seedColor = new Scalar(seed.Color.Item0, seed.Color.Item1, seed.Color.Item2);
        var tolerance = new Scalar(threshold, threshold, threshold);
        var flags = (FloodFillFlags)(8 | (255 << 8)) | FloodFillFlags.FixedRange | FloodFillFlags.MaskOnly;
        Cv2.FloodFill(sourceBgr, mask, seed.Point, seedColor, out _, tolerance, tolerance, flags);

        using var roi = new Mat(mask, new Rect(1, 1, sourceBgr.Cols, sourceBgr.Rows));
        var result = new Mat();
        Cv2.Compare(roi, Scalar.Black, result, CmpTypes.GT);
        return result;
    }

    private static void CleanupBackgroundMask(Mat backgroundMask, BackgroundThresholdSettings settings)
    {
        RemoveSmallComponents(backgroundMask, Math.Max(1, settings.BgNoiseMinArea));
        FillSmallHoles(backgroundMask, Math.Max(0, settings.BgNoiseMaxHoleArea));
    }

    private static Mat BuildTrimap(Mat backgroundMask1, Mat foregroundMask0)
    {
        var trimap = new Mat(backgroundMask1.Rows, backgroundMask1.Cols, MatType.CV_8UC1, Scalar.Black);
        trimap.SetTo(new Scalar(128));
        trimap.SetTo(Scalar.Black, backgroundMask1);
        trimap.SetTo(new Scalar(255), foregroundMask0);
        return trimap;
    }

    private static Mat ComputeDistanceFromBackground(Mat backgroundMask)
    {
        using var inverse = new Mat();
        Cv2.BitwiseNot(backgroundMask, inverse);
        var result = new Mat();
        Cv2.DistanceTransform(inverse, result, DistanceTypes.L2, DistanceTransformMasks.Mask5);
        return result;
    }

    private static void FillSmallHoles(Mat mask, int maxHoleArea)
    {
        if (maxHoleArea <= 0)
        {
            return;
        }

        using var inverse = new Mat();
        Cv2.BitwiseNot(mask, inverse);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(inverse, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32SC1);
        if (count <= 1)
        {
            return;
        }

        var labelsIndexer = labels.GetGenericIndexer<int>();
        var statsIndexer = stats.GetGenericIndexer<int>();
        var maskIndexer = mask.GetGenericIndexer<byte>();
        var borderConnected = new bool[count];

        for (var x = 0; x < mask.Cols; x++)
        {
            borderConnected[labelsIndexer[0, x]] = true;
            borderConnected[labelsIndexer[mask.Rows - 1, x]] = true;
        }

        for (var y = 0; y < mask.Rows; y++)
        {
            borderConnected[labelsIndexer[y, 0]] = true;
            borderConnected[labelsIndexer[y, mask.Cols - 1]] = true;
        }

        for (var label = 1; label < count; label++)
        {
            if (borderConnected[label])
            {
                continue;
            }

            var area = statsIndexer[label, (int)ConnectedComponentsTypes.Area];
            if (area <= 0 || area > maxHoleArea)
            {
                continue;
            }

            var left = statsIndexer[label, (int)ConnectedComponentsTypes.Left];
            var top = statsIndexer[label, (int)ConnectedComponentsTypes.Top];
            var width = statsIndexer[label, (int)ConnectedComponentsTypes.Width];
            var height = statsIndexer[label, (int)ConnectedComponentsTypes.Height];

            for (var y = top; y < top + height; y++)
            {
                for (var x = left; x < left + width; x++)
                {
                    if (labelsIndexer[y, x] == label)
                    {
                        maskIndexer[y, x] = 255;
                    }
                }
            }
        }
    }

    private static void RemoveSmallComponents(Mat mask, int minArea)
    {
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var componentCount = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
        if (componentCount <= 1)
        {
            return;
        }

        var keep = new bool[componentCount];
        for (var label = 1; label < componentCount; label++)
        {
            keep[label] = stats.Get<int>(label, (int)ConnectedComponentsTypes.Area) >= minArea;
        }

        var labelIndexer = labels.GetGenericIndexer<int>();
        var maskIndexer = mask.GetGenericIndexer<byte>();
        for (var y = 0; y < mask.Rows; y++)
        {
            for (var x = 0; x < mask.Cols; x++)
            {
                if (!keep[labelIndexer[y, x]])
                {
                    maskIndexer[y, x] = 0;
                }
            }
        }
    }

    private static float ComputeBackgroundThreshold(CutoutParameters parameters)
        => Lerp((float)parameters.Internal.BackgroundThreshold.TbgMin, (float)parameters.Internal.BackgroundThreshold.TbgMax, (float)parameters.BackgroundTolerance);

    private static float ComputeForegroundDelta(CutoutParameters parameters)
        => Lerp(
            (float)parameters.Internal.BackgroundThreshold.TfgDeltaMin,
            (float)parameters.Internal.BackgroundThreshold.TfgDeltaMax,
            (float)parameters.ContourTolerance);

    private static float ComputeMaxContourWidth(CutoutParameters parameters)
        => Math.Clamp(parameters.MaxContourWidthPx, 0, 128);

    private static float ComputeColorDistance(Vec3b pixel, RgbColor reference)
    {
        var diffB = pixel.Item0 - reference.B;
        var diffG = pixel.Item1 - reference.G;
        var diffR = pixel.Item2 - reference.R;
        return MathF.Sqrt((diffB * diffB) + (diffG * diffG) + (diffR * diffR));
    }

    private static float Lerp(float start, float end, float amount) => start + ((end - start) * Math.Clamp(amount, 0f, 1f));

    private static float Lerp(int start, int end, double amount) => start + ((end - start) * (float)Math.Clamp(amount, 0d, 1d));
}
