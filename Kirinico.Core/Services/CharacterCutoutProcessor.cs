using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

public sealed class CharacterCutoutProcessor : IDisposable
{
    private const float OutlineAntialiasWidth = 1.0f;
    private const int MinUnknownPixelsForMatting = 16;

    private readonly IAlphaMatteEstimator _alphaEstimator;

    public CharacterCutoutProcessor(IAlphaMatteEstimator alphaEstimator)
    {
        _alphaEstimator = alphaEstimator ?? throw new ArgumentNullException(nameof(alphaEstimator));
    }

    public RgbColor SampleBackgroundColor(Mat sourceBgr, Point center, int radius = 6)
    {
        ArgumentNullException.ThrowIfNull(sourceBgr);

        if (sourceBgr.Empty())
        {
            return new RgbColor(255, 255, 255);
        }

        var x = Math.Clamp(center.X - radius, 0, sourceBgr.Width - 1);
        var y = Math.Clamp(center.Y - radius, 0, sourceBgr.Height - 1);
        var width = Math.Clamp((radius * 2) + 1, 1, sourceBgr.Width - x);
        var height = Math.Clamp((radius * 2) + 1, 1, sourceBgr.Height - y);

        using var roi = new Mat(sourceBgr, new Rect(x, y, width, height));
        var mean = Cv2.Mean(roi);
        return new RgbColor((byte)Math.Round(mean.Val2), (byte)Math.Round(mean.Val1), (byte)Math.Round(mean.Val0));
    }

    public CutoutResult Process(
        Mat sourceBgr,
        CutoutParameters parameters,
        ManualEditMaps? manualMaps = null,
        IProgress<ProcessingProgress>? progress = null)
    {
        using var prepared = PrepareTrimap(sourceBgr, parameters, manualMaps, progress);
        using var preResize = EstimateAlphaFromTrimap(prepared, parameters, progress);
        if (preResize is null)
        {
            throw new InvalidOperationException("alpha 推定が中断されました。");
        }

        return FinalizeFromPreResize(preResize, parameters, progress);
    }

    public TrimapPreparationResult PrepareTrimap(
        Mat sourceBgr,
        CutoutParameters parameters,
        ManualEditMaps? manualMaps = null,
        IProgress<ProcessingProgress>? progress = null)
    {
        return PrepareTrimapCore(sourceBgr, parameters, manualMaps, progress);
    }

    public Mat BuildTrimapPreview(
        Mat sourceBgr,
        CutoutParameters parameters,
        ManualEditMaps? manualMaps = null,
        IProgress<ProcessingProgress>? progress = null)
    {
        using var prepared = PrepareTrimap(sourceBgr, parameters, manualMaps, progress);
        return prepared.TrimapMask.Clone();
    }

    public PreResizeCutoutResult ProcessPreResize(
        Mat sourceBgr,
        CutoutParameters parameters,
        ManualEditMaps? manualMaps = null,
        IProgress<ProcessingProgress>? progress = null)
    {
        using var prepared = PrepareTrimap(sourceBgr, parameters, manualMaps, progress);
        return EstimateAlphaFromTrimap(prepared, parameters, progress)
            ?? throw new InvalidOperationException("alpha 推定が中断されました。");
    }

    public PreResizeCutoutResult? EstimateAlphaFromTrimap(
        TrimapPreparationResult prepared,
        CutoutParameters parameters,
        IProgress<ProcessingProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(parameters);

        using var alphaMask = CountUnknownPixels(prepared.TrimapMask) < MinUnknownPixelsForMatting
            ? CreateBinaryAlphaMask(prepared.TrimapMask)
            : EstimateAlphaWithMatting(prepared.ReferenceBgr, prepared.TrimapMask, parameters.MattingMethod, parameters.Internal.Matting, progress);

        if (alphaMask is null)
        {
            return null;
        }

        progress?.Report(new ProcessingProgress(80d, "A_raw をキャッシュ"));
        return new PreResizeCutoutResult(
            prepared.TrimapMask.Clone(),
            alphaMask.Clone(),
            prepared.OriginalBgr.Clone(),
            prepared.ResolvedBackgroundColor);
    }

    public void CancelPendingMatting()
    {
        _alphaEstimator.CancelCurrentRequest();
    }

    public CutoutResult FinalizeFromPreResize(
        PreResizeCutoutResult preResizeResult,
        CutoutParameters parameters,
        IProgress<ProcessingProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(preResizeResult);
        ArgumentNullException.ThrowIfNull(parameters);

        progress?.Report(new ProcessingProgress(90d, "前景色を復元"));
        using var restoredStraightBgra = RestoreStraightBgra(
            preResizeResult.OriginalBgr,
            preResizeResult.TrimapMask,
            preResizeResult.AlphaMask,
            preResizeResult.ResolvedBackgroundColor,
            parameters);

        var targetSize = parameters.Resize.ResolveTargetSize(restoredStraightBgra.Size());
        progress?.Report(new ProcessingProgress(95d, "リサイズと縁取りを適用"));
        using var resizedStraight = ResizePremultiplied(restoredStraightBgra, targetSize, parameters.Resize.Interpolation);
        var finalRgba = ApplyOutline(resizedStraight, parameters.Outline);

        progress?.Report(new ProcessingProgress(100d, "完了"));
        return new CutoutResult(
            preResizeResult.TrimapMask.Clone(),
            preResizeResult.AlphaMask.Clone(),
            finalRgba,
            preResizeResult.ResolvedBackgroundColor);
    }

    public void Dispose()
    {
        _alphaEstimator.Dispose();
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

    private static TrimapPreparationResult PrepareTrimapCore(
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
                ? CollectBackgroundSeeds(referenceBgr, manualMaps?.BackgroundSeedAddMap)
                : [];

            if (parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ManualSeed && seeds.Count == 0)
            {
                throw new InvalidOperationException("背景seedがありません。");
            }

            var backgroundColor = ResolveBackgroundColor(parameters, seeds);

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

    private static List<SeedInfo> CollectBackgroundSeeds(Mat sourceBgr, Mat? backgroundSeedAddMap)
    {
        var result = new List<SeedInfo>();
        if (backgroundSeedAddMap is null || backgroundSeedAddMap.Empty())
        {
            return result;
        }

        var addIndexer = backgroundSeedAddMap.GetGenericIndexer<byte>();
        for (var y = 0; y < sourceBgr.Rows; y++)
        {
            for (var x = 0; x < sourceBgr.Cols; x++)
            {
                if (addIndexer[y, x] == 0)
                {
                    continue;
                }

                result.Add(new SeedInfo(new Point(x, y), ComputeRepresentativeBackgroundColor(sourceBgr, x, y)));
            }
        }

        return result;
    }

    private static Vec3f ComputeRepresentativeBackgroundColor(Mat sourceBgr, int centerX, int centerY)
    {
        var x0 = Math.Max(0, centerX - 1);
        var y0 = Math.Max(0, centerY - 1);
        var x1 = Math.Min(sourceBgr.Cols - 1, centerX + 1);
        var y1 = Math.Min(sourceBgr.Rows - 1, centerY + 1);
        var indexer = sourceBgr.GetGenericIndexer<Vec3b>();

        var sumB = 0f;
        var sumG = 0f;
        var sumR = 0f;
        var count = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var pixel = indexer[y, x];
                sumB += pixel.Item0;
                sumG += pixel.Item1;
                sumR += pixel.Item2;
                count++;
            }
        }

        return new Vec3f(sumB / count, sumG / count, sumR / count);
    }

    private static RgbColor ResolveBackgroundColor(CutoutParameters parameters, IReadOnlyList<SeedInfo> seeds)
    {
        if (parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ColorRange)
        {
            return parameters.BackgroundColor;
        }

        var sumB = 0f;
        var sumG = 0f;
        var sumR = 0f;
        foreach (var seed in seeds)
        {
            sumB += seed.Color.Item0;
            sumG += seed.Color.Item1;
            sumR += seed.Color.Item2;
        }

        var count = Math.Max(1, seeds.Count);
        return new RgbColor(ClampToByte(sumR / count), ClampToByte(sumG / count), ClampToByte(sumB / count));
    }

    private static Mat BuildBackgroundMask(Mat sourceBgr, IReadOnlyList<SeedInfo> seeds, RgbColor background, CutoutParameters parameters)
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

    private static Mat GrowBackgroundRegion(Mat sourceBgr, SeedInfo seed, float threshold)
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

    private static Mat RestoreStraightBgra(Mat originalBgr, Mat trimapMask, Mat alphaMask, RgbColor background, CutoutParameters parameters)
    {
        var restore = parameters.Internal.AlphaColorRestore;
        var a0 = Lerp((float)restore.AlphaCutMin, (float)restore.AlphaCutMax, (float)parameters.TransparencyCut);
        var result = new Mat(originalBgr.Rows, originalBgr.Cols, MatType.CV_8UC4, Scalar.All(0d));
        var sourceIndexer = originalBgr.GetGenericIndexer<Vec3b>();
        using var stabilizedAlphaMask = CreateStabilizedAlphaMask(alphaMask, a0, (float)parameters.OpaqueAlphaThreshold);
        using var despillRangeMask = BuildDespillRangeMask(stabilizedAlphaMask, parameters.DespillExpansionPx);
        var alphaIndexer = stabilizedAlphaMask.GetGenericIndexer<byte>();
        var despillRangeIndexer = despillRangeMask.GetGenericIndexer<byte>();
        var resultIndexer = result.GetGenericIndexer<Vec4b>();
        var despillMix = (float)Math.Clamp(parameters.DespillMix, 0d, 1d);
        var despillExpand = (float)Math.Clamp(parameters.DespillExpand, 0d, 1d);
        var despillBrightness = (float)parameters.DespillBrightness;
        var despillFactor = (1f - despillMix) * (1f - despillExpand);
        var useDespill = TryBuildBackgroundBasis(background, out var backgroundAxis, out var basis1, out var basis2);

        for (var y = 0; y < originalBgr.Rows; y++)
        {
            for (var x = 0; x < originalBgr.Cols; x++)
            {
                var alpha = alphaIndexer[y, x] / 255f;
                if (alpha < a0)
                {
                    resultIndexer[y, x] = default;
                    continue;
                }

                var observed = sourceIndexer[y, x];
                var restoredB = (float)observed.Item0;
                var restoredG = (float)observed.Item1;
                var restoredR = (float)observed.Item2;

                if (useDespill && despillRangeIndexer[y, x] != 0)
                {
                    var colorB = observed.Item0 / 255f;
                    var colorG = observed.Item1 / 255f;
                    var colorR = observed.Item2 / 255f;
                    var backgroundComponent =
                        (colorB * backgroundAxis.Item0) +
                        (colorG * backgroundAxis.Item1) +
                        (colorR * backgroundAxis.Item2);
                    var orthogonal1 = MathF.Abs(
                        (colorB * basis1.Item0) +
                        (colorG * basis1.Item1) +
                        (colorR * basis1.Item2));
                    var orthogonal2 = MathF.Abs(
                        (colorB * basis2.Item0) +
                        (colorG * basis2.Item1) +
                        (colorR * basis2.Item2));
                    var spillmap = MathF.Max(backgroundComponent - ((orthogonal1 * despillMix) + (orthogonal2 * despillFactor)), 0f);

                    if (spillmap > 0f)
                    {
                        colorB = MathF.Max(colorB - (spillmap * backgroundAxis.Item0) + (despillBrightness * spillmap), 0f);
                        colorG = MathF.Max(colorG - (spillmap * backgroundAxis.Item1) + (despillBrightness * spillmap), 0f);
                        colorR = MathF.Max(colorR - (spillmap * backgroundAxis.Item2) + (despillBrightness * spillmap), 0f);
                        restoredB = colorB * 255f;
                        restoredG = colorG * 255f;
                        restoredR = colorR * 255f;
                    }
                }

                resultIndexer[y, x] = new Vec4b(
                    ClampToByte(restoredB),
                    ClampToByte(restoredG),
                    ClampToByte(restoredR),
                    ClampToByte(alpha * 255f));
            }
        }

        return result;
    }

    private Mat? EstimateAlphaWithMatting(Mat referenceBgr, Mat trimapMask, MattingMethod method, MattingSettings settings, IProgress<ProcessingProgress>? progress)
    {
        progress?.Report(new ProcessingProgress(30d, "PyMatting で alpha を推定"));
        return _alphaEstimator.EstimateAlpha(referenceBgr, trimapMask, method, settings);
    }

    private static int CountUnknownPixels(Mat trimapMask)
    {
        using var unknownMask = new Mat();
        Cv2.Compare(trimapMask, new Scalar(128), unknownMask, CmpTypes.EQ);
        return Cv2.CountNonZero(unknownMask);
    }

    private static Mat CreateBinaryAlphaMask(Mat trimapMask)
    {
        var alphaMask = new Mat(trimapMask.Rows, trimapMask.Cols, MatType.CV_8UC1, Scalar.Black);
        using var foregroundMask = new Mat();
        Cv2.Compare(trimapMask, new Scalar(255), foregroundMask, CmpTypes.EQ);
        alphaMask.SetTo(new Scalar(255), foregroundMask);
        return alphaMask;
    }

    private static Mat CreateStabilizedAlphaMask(Mat alphaMask, float alphaCut, float opaqueAlphaThreshold)
    {
        var result = alphaMask.Clone();
        var indexer = result.GetGenericIndexer<byte>();
        var cutThreshold = Math.Clamp(alphaCut * 255f, 0f, 255f);
        var opaqueThreshold = Math.Clamp(opaqueAlphaThreshold * 255f, 0f, 255f);

        for (var y = 0; y < result.Rows; y++)
        {
            for (var x = 0; x < result.Cols; x++)
            {
                var value = indexer[y, x];
                if (value < cutThreshold)
                {
                    indexer[y, x] = 0;
                }
                else if (value >= opaqueThreshold)
                {
                    indexer[y, x] = 255;
                }
            }
        }

        return result;
    }

    private static Mat BuildDespillRangeMask(Mat stabilizedAlphaMask, int expansionPx)
    {
        var partialMask = new Mat(stabilizedAlphaMask.Rows, stabilizedAlphaMask.Cols, MatType.CV_8UC1, Scalar.Black);
        var alphaIndexer = stabilizedAlphaMask.GetGenericIndexer<byte>();
        var partialIndexer = partialMask.GetGenericIndexer<byte>();

        for (var y = 0; y < stabilizedAlphaMask.Rows; y++)
        {
            for (var x = 0; x < stabilizedAlphaMask.Cols; x++)
            {
                var alpha = alphaIndexer[y, x];
                if (alpha > 0 && alpha < 255)
                {
                    partialIndexer[y, x] = 255;
                }
            }
        }

        if (expansionPx <= 0)
        {
            return partialMask;
        }

        using var dilated = DilateMask(partialMask, expansionPx);
        partialMask.Dispose();
        return dilated.Clone();
    }

    private static Mat DilateMask(Mat mask, int radius)
    {
        if (radius <= 0)
        {
            return mask.Clone();
        }

        using var kernel = CreateKernel(radius);
        var result = new Mat();
        Cv2.Dilate(mask, result, kernel);
        return result;
    }

    private static Mat ErodeMask(Mat mask, int radius)
    {
        if (radius <= 0)
        {
            return mask.Clone();
        }

        using var kernel = CreateKernel(radius);
        var result = new Mat();
        Cv2.Erode(mask, result, kernel);
        return result;
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

    private static Mat ResizePremultiplied(Mat straightBgra, Size targetSize, ResizeInterpolationMode interpolation)
    {
        if (straightBgra.Size() == targetSize)
        {
            return straightBgra.Clone();
        }

        using var premultiplied = new Mat(straightBgra.Rows, straightBgra.Cols, MatType.CV_8UC4);
        var sourceIndexer = straightBgra.GetGenericIndexer<Vec4b>();
        var premulIndexer = premultiplied.GetGenericIndexer<Vec4b>();

        for (var y = 0; y < straightBgra.Rows; y++)
        {
            for (var x = 0; x < straightBgra.Cols; x++)
            {
                var pixel = sourceIndexer[y, x];
                var alpha = pixel.Item3 / 255f;
                premulIndexer[y, x] = new Vec4b(
                    ClampToByte(pixel.Item0 * alpha),
                    ClampToByte(pixel.Item1 * alpha),
                    ClampToByte(pixel.Item2 * alpha),
                    pixel.Item3);
            }
        }

        using var resizedPremultiplied = new Mat();
        Cv2.Resize(premultiplied, resizedPremultiplied, targetSize, 0d, 0d, ToInterpolationFlags(interpolation));
        return ConvertPremultipliedToStraight(resizedPremultiplied);
    }

    private static InterpolationFlags ToInterpolationFlags(ResizeInterpolationMode interpolation)
    {
        return interpolation switch
        {
            ResizeInterpolationMode.Nearest => InterpolationFlags.Nearest,
            ResizeInterpolationMode.Linear => InterpolationFlags.Linear,
            ResizeInterpolationMode.Cubic => InterpolationFlags.Cubic,
            ResizeInterpolationMode.Area => InterpolationFlags.Area,
            _ => InterpolationFlags.Lanczos4,
        };
    }

    private static Mat ConvertPremultipliedToStraight(Mat premultipliedBgra)
    {
        var result = new Mat(premultipliedBgra.Rows, premultipliedBgra.Cols, MatType.CV_8UC4);
        var sourceIndexer = premultipliedBgra.GetGenericIndexer<Vec4b>();
        var resultIndexer = result.GetGenericIndexer<Vec4b>();

        for (var y = 0; y < premultipliedBgra.Rows; y++)
        {
            for (var x = 0; x < premultipliedBgra.Cols; x++)
            {
                var pixel = sourceIndexer[y, x];
                if (pixel.Item3 == 0)
                {
                    resultIndexer[y, x] = default;
                    continue;
                }

                var alpha = pixel.Item3 / 255f;
                resultIndexer[y, x] = new Vec4b(
                    ClampToByte(pixel.Item0 / alpha),
                    ClampToByte(pixel.Item1 / alpha),
                    ClampToByte(pixel.Item2 / alpha),
                    pixel.Item3);
            }
        }

        return result;
    }

    private static Mat ExtractAlpha(Mat straightBgra)
    {
        var alpha = new Mat();
        Cv2.ExtractChannel(straightBgra, alpha, 3);
        return alpha;
    }

    private static Mat ApplyOutline(Mat straightBgra, OutlineOptions outline)
    {
        if (!outline.Enabled || outline.Thickness <= 0d)
        {
            return straightBgra.Clone();
        }

        using var alpha = ExtractAlpha(straightBgra);
        using var outlineSourceMask = new Mat();
        Cv2.Threshold(alpha, outlineSourceMask, 0d, 255d, ThresholdTypes.Binary);
        if (!TryGetNonZeroBounds(outlineSourceMask, out _))
        {
            return straightBgra.Clone();
        }

        using var inverted = new Mat();
        Cv2.Threshold(outlineSourceMask, inverted, 0d, 255d, ThresholdTypes.BinaryInv);
        using var outsideDistance = new Mat();
        Cv2.DistanceTransform(inverted, outsideDistance, DistanceTypes.L2, DistanceTransformMasks.Mask5);

        var result = straightBgra.Clone();
        using var outlineDistanceMask = new Mat();
        Cv2.Compare(outsideDistance, new Scalar(outline.Thickness + 1.5d), outlineDistanceMask, CmpTypes.LE);
        using var nonOpaqueMask = new Mat();
        Cv2.Compare(alpha, new Scalar(255), nonOpaqueMask, CmpTypes.LT);
        Cv2.BitwiseAnd(outlineDistanceMask, nonOpaqueMask, outlineDistanceMask);
        if (!TryGetNonZeroBounds(outlineDistanceMask, out var bounds))
        {
            return result;
        }

        var baseIndexer = straightBgra.GetGenericIndexer<Vec4b>();
        var alphaIndexer = alpha.GetGenericIndexer<byte>();
        var distanceIndexer = outsideDistance.GetGenericIndexer<float>();
        var maskIndexer = outlineDistanceMask.GetGenericIndexer<byte>();
        var resultIndexer = result.GetGenericIndexer<Vec4b>();

        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                if (maskIndexer[y, x] == 0)
                {
                    continue;
                }

                var foreground = baseIndexer[y, x];
                var foregroundAlpha = foreground.Item3 / 255f;
                var outlineCoverage = ComputeOutlineCoverage(alphaIndexer[y, x], distanceIndexer[y, x], (float)outline.Thickness);
                var outlineAlpha = outlineCoverage * (1f - foregroundAlpha);
                var outAlpha = foregroundAlpha + outlineAlpha;
                if (outAlpha <= 0.001f)
                {
                    resultIndexer[y, x] = default;
                    continue;
                }

                var bluePremul = (foreground.Item0 * foregroundAlpha) + (outline.Color.B * outlineAlpha);
                var greenPremul = (foreground.Item1 * foregroundAlpha) + (outline.Color.G * outlineAlpha);
                var redPremul = (foreground.Item2 * foregroundAlpha) + (outline.Color.R * outlineAlpha);

                resultIndexer[y, x] = new Vec4b(
                    ClampToByte(bluePremul / outAlpha),
                    ClampToByte(greenPremul / outAlpha),
                    ClampToByte(redPremul / outAlpha),
                    ClampToByte(outAlpha * 255f));
            }
        }

        return result;
    }

    private static float ComputeOutlineCoverage(byte sourceAlpha, float outsideDistance, float thickness)
    {
        if (sourceAlpha >= 255 || thickness <= 0f)
        {
            return 0f;
        }

        var distanceFromBoundary = MathF.Max(0f, outsideDistance - 0.5f);
        if (distanceFromBoundary <= thickness)
        {
            return 1f;
        }

        var aaEnd = thickness + OutlineAntialiasWidth;
        if (distanceFromBoundary >= aaEnd)
        {
            return 0f;
        }

        return 1f - ((distanceFromBoundary - thickness) / OutlineAntialiasWidth);
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

    private static bool TryGetNonZeroBounds(Mat mask, out Rect bounds)
    {
        using var points = new Mat();
        Cv2.FindNonZero(mask, points);
        if (points.Empty())
        {
            bounds = default;
            return false;
        }

        bounds = Cv2.BoundingRect(points);
        return true;
    }

    private static Mat CreateKernel(int radius)
    {
        var size = Math.Max(1, (radius * 2) + 1);
        return Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(size, size));
    }

    private static byte ClampToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static byte ClampToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static bool TryBuildBackgroundBasis(RgbColor background, out Vec3f axis, out Vec3f basis1, out Vec3f basis2)
    {
        axis = new Vec3f(background.B / 255f, background.G / 255f, background.R / 255f);
        var axisLength = MathF.Sqrt((axis.Item0 * axis.Item0) + (axis.Item1 * axis.Item1) + (axis.Item2 * axis.Item2));
        if (axisLength <= 1e-6f)
        {
            basis1 = default;
            basis2 = default;
            return false;
        }

        axis = new Vec3f(axis.Item0 / axisLength, axis.Item1 / axisLength, axis.Item2 / axisLength);
        var reference = MathF.Abs(axis.Item2) < 0.9f
            ? new Vec3f(0f, 0f, 1f)
            : new Vec3f(0f, 1f, 0f);
        basis1 = Cross(reference, axis);
        var basis1Length = MathF.Sqrt((basis1.Item0 * basis1.Item0) + (basis1.Item1 * basis1.Item1) + (basis1.Item2 * basis1.Item2));
        if (basis1Length <= 1e-6f)
        {
            basis1 = default;
            basis2 = default;
            return false;
        }

        basis1 = new Vec3f(basis1.Item0 / basis1Length, basis1.Item1 / basis1Length, basis1.Item2 / basis1Length);
        basis2 = Cross(axis, basis1);
        var basis2Length = MathF.Sqrt((basis2.Item0 * basis2.Item0) + (basis2.Item1 * basis2.Item1) + (basis2.Item2 * basis2.Item2));
        if (basis2Length <= 1e-6f)
        {
            basis2 = default;
            return false;
        }

        basis2 = new Vec3f(basis2.Item0 / basis2Length, basis2.Item1 / basis2Length, basis2.Item2 / basis2Length);
        return true;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / Math.Max(edge1 - edge0, 1e-6f), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float ShapeUiStrength(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return 1f - ((1f - t) * (1f - t));
    }

    private static float Lerp(float start, float end, float amount) => start + ((end - start) * Math.Clamp(amount, 0f, 1f));

    private static float Lerp(int start, int end, double amount) => start + ((end - start) * (float)Math.Clamp(amount, 0d, 1d));

    private static Vec3f Cross(Vec3f left, Vec3f right)
        => new(
            (left.Item1 * right.Item2) - (left.Item2 * right.Item1),
            (left.Item2 * right.Item0) - (left.Item0 * right.Item2),
            (left.Item0 * right.Item1) - (left.Item1 * right.Item0));

    private readonly record struct SeedInfo(Point Point, Vec3f Color);
}
