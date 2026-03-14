using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

public sealed class CharacterCutoutProcessor
{
    private const float ForegroundCandidateMargin = 48f;
    private const float ForegroundCandidateFloor = 96f;
    private const float SmallAlphaBlendStart = 0.35f;

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
        ArgumentNullException.ThrowIfNull(sourceBgr);
        ArgumentNullException.ThrowIfNull(parameters);

        if (sourceBgr.Empty())
        {
            throw new ArgumentException("Source image is empty.", nameof(sourceBgr));
        }

        if (sourceBgr.Type() != MatType.CV_8UC3)
        {
            throw new ArgumentException("Source image must be BGR 8-bit.", nameof(sourceBgr));
        }

        progress?.Report(new ProcessingProgress(6d, "背景シードを取得"));
        var backgroundMode = parameters.BackgroundSpecificationMode;
        var seeds = backgroundMode == BackgroundSpecificationMode.ManualSeed
            ? CollectBackgroundSeeds(sourceBgr, manualMaps?.BackgroundSeedAddMap)
            : [];
        if (backgroundMode == BackgroundSpecificationMode.ManualSeed && seeds.Count == 0)
        {
            throw new InvalidOperationException("背景seedがありません。");
        }

        var background = ResolveBackgroundColor(parameters, seeds);

        progress?.Report(new ProcessingProgress(18d, "背景を連結推定"));
        using var backgroundMask = BuildBackgroundMask(sourceBgr, seeds, background, parameters);

        progress?.Report(new ProcessingProgress(34d, "背景を整形"));
        CleanupBackgroundMask(backgroundMask, parameters);

        progress?.Report(new ProcessingProgress(48d, "粗い前景マスクを作成"));
        using var foregroundMask = BuildForegroundMask(backgroundMask);
        using var edgeField = BuildEdgeField(foregroundMask);

        progress?.Report(new ProcessingProgress(64d, "アルファを推定"));
        using var alpha8 = BuildFinalAlpha(sourceBgr, foregroundMask, edgeField, background, parameters);

        progress?.Report(new ProcessingProgress(78d, "前景色を復元"));
        using var reconstructedForeground = ReconstructForegroundColors(sourceBgr, alpha8, background);

        progress?.Report(new ProcessingProgress(88d, "アルファを平滑化"));
        SmoothAlpha(alpha8);

        var alphaPreview = alpha8.Clone();
        using var straightBgra = ComposeStraightBgra(reconstructedForeground, alpha8);

        var targetSize = parameters.Resize.ResolveTargetSize(sourceBgr.Size());
        progress?.Report(new ProcessingProgress(96d, "リサイズと縁取りを適用"));
        using var resizedStraight = ResizePremultiplied(straightBgra, targetSize);
        var finalRgba = ApplyOutline(resizedStraight, parameters.Outline);

        progress?.Report(new ProcessingProgress(100d, "完了"));
        return new CutoutResult(alphaPreview, finalRgba, background);
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

    private static RgbColor ComputeGlobalBackgroundColor(IReadOnlyList<SeedInfo> seeds)
    {
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
        return new RgbColor(
            ClampToByte(sumR / count),
            ClampToByte(sumG / count),
            ClampToByte(sumB / count));
    }

    private static RgbColor ResolveBackgroundColor(CutoutParameters parameters, IReadOnlyList<SeedInfo> seeds)
    {
        return parameters.BackgroundSpecificationMode == BackgroundSpecificationMode.ColorRange
            ? parameters.BackgroundColor
            : ComputeGlobalBackgroundColor(seeds);
    }

    private static Mat BuildBackgroundMask(Mat sourceBgr, IReadOnlyList<SeedInfo> seeds, RgbColor background, CutoutParameters parameters)
    {
        var threshold = ComputeBackgroundDistanceThreshold(parameters.Extraction);
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
        var result = new Mat(sourceBgr.Rows, sourceBgr.Cols, MatType.CV_8UC1, Scalar.Black);
        var resultIndexer = result.GetGenericIndexer<byte>();
        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var visited = new bool[sourceBgr.Rows, sourceBgr.Cols];
        var queue = new Queue<Point>();
        queue.Enqueue(seed.Point);
        visited[seed.Point.Y, seed.Point.X] = true;

        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (ComputeColorDistance(sourceIndexer[point.Y, point.X], seed.Color) > threshold)
            {
                continue;
            }

            resultIndexer[point.Y, point.X] = 255;

            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    var nx = point.X + offsetX;
                    var ny = point.Y + offsetY;
                    if (nx < 0 || ny < 0 || nx >= sourceBgr.Cols || ny >= sourceBgr.Rows)
                    {
                        continue;
                    }

                    if (visited[ny, nx])
                    {
                        continue;
                    }

                    visited[ny, nx] = true;
                    queue.Enqueue(new Point(nx, ny));
                }
            }
        }

        return result;
    }

    private static void CleanupBackgroundMask(Mat backgroundMask, CutoutParameters parameters)
    {
        var minBackgroundArea = 1 + (int)Math.Round(Math.Clamp(parameters.NoiseRemoval, 0d, 1d) * 20d);
        RemoveSmallComponents(backgroundMask, minBackgroundArea);
        var maxHoleArea = 2 + (int)Math.Round(Math.Clamp(parameters.NoiseRemoval, 0d, 1d) * 48d);
        FillSmallHoles(backgroundMask, maxHoleArea);
    }

    private static void FillSmallHoles(Mat mask, int maxHoleArea)
    {
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

    private static Mat BuildForegroundMask(Mat backgroundMask)
    {
        var foregroundMask = new Mat();
        Cv2.BitwiseNot(backgroundMask, foregroundMask);
        return foregroundMask;
    }

    private static Mat BuildFinalAlpha(
        Mat sourceBgr,
        Mat foregroundMask,
        EdgeField edgeField,
        RgbColor background,
        CutoutParameters parameters)
    {
        var scanWidth = Math.Max(0, parameters.ScanWidth);
        using var stableInterior = ErodeMask(foregroundMask, scanWidth);
        using var innerBand = new Mat();
        Cv2.Subtract(foregroundMask, stableInterior, innerBand);

        var alpha = new Mat(sourceBgr.Rows, sourceBgr.Cols, MatType.CV_8UC1, Scalar.Black);
        stableInterior.CopyTo(alpha);

        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var bandIndexer = innerBand.GetGenericIndexer<byte>();
        var alphaIndexer = alpha.GetGenericIndexer<byte>();
        var maskIndexer = foregroundMask.GetGenericIndexer<byte>();

        var bgThreshold = ComputeBackgroundDistanceThreshold(parameters.Extraction);
        var fgThreshold = ComputeForegroundCandidateThreshold(bgThreshold);

        for (var y = 0; y < sourceBgr.Rows; y++)
        {
            for (var x = 0; x < sourceBgr.Cols; x++)
            {
                if (bandIndexer[y, x] == 0)
                {
                    continue;
                }

                var pixel = sourceIndexer[y, x];
                if (TryEstimateLocalForegroundColor(sourceBgr, foregroundMask, edgeField, x, y, scanWidth, parameters.LinePolarity, background, fgThreshold, out var foregroundColor))
                {
                    var alphaValue = EstimateForegroundMixFactor(pixel, foregroundColor, background);
                    alphaIndexer[y, x] = ClampToByte(alphaValue * 255f);
                }
                else
                {
                    var distance = ComputeColorDistance(pixel, background);
                    alphaIndexer[y, x] = ClampToByte(255f * SmoothStep(bgThreshold, fgThreshold, distance));
                }
            }
        }

        for (var y = 0; y < sourceBgr.Rows; y++)
        {
            for (var x = 0; x < sourceBgr.Cols; x++)
            {
                if (maskIndexer[y, x] == 0)
                {
                    alphaIndexer[y, x] = 0;
                }
            }
        }
        return alpha;
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

    private static bool TryEstimateLocalForegroundColor(
        Mat sourceBgr,
        Mat foregroundMask,
        EdgeField edgeField,
        int x,
        int y,
        int scanWidth,
        LinePolarity polarity,
        RgbColor background,
        float foregroundThreshold,
        out Vec3f foregroundColor)
    {
        foregroundColor = default;
        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var maskIndexer = foregroundMask.GetGenericIndexer<byte>();
        var gradXIndexer = edgeField.GradX.GetGenericIndexer<float>();
        var gradYIndexer = edgeField.GradY.GetGenericIndexer<float>();

        var gradX = gradXIndexer[y, x];
        var gradY = gradYIndexer[y, x];
        var magnitude = MathF.Sqrt((gradX * gradX) + (gradY * gradY));

        var candidates = new List<Candidate>();
        if (magnitude > 0.001f)
        {
            var dirX = gradX / magnitude;
            var dirY = gradY / magnitude;
            for (var step = 1; step <= scanWidth; step++)
            {
                var sampleX = (int)Math.Round(x + (dirX * step));
                var sampleY = (int)Math.Round(y + (dirY * step));
                if (sampleX < 0 || sampleY < 0 || sampleX >= sourceBgr.Cols || sampleY >= sourceBgr.Rows)
                {
                    continue;
                }

                if (maskIndexer[sampleY, sampleX] == 0)
                {
                    continue;
                }

                var pixel = sourceIndexer[sampleY, sampleX];
                var distance = ComputeColorDistance(pixel, background);
                if (distance < foregroundThreshold)
                {
                    continue;
                }

                candidates.Add(new Candidate(step, sampleX, sampleY, pixel, distance, ComputeLuma(pixel)));
            }
        }

        if (candidates.Count == 0)
        {
            var windowCandidates = CollectWindowCandidates(sourceBgr, foregroundMask, edgeField, x, y, scanWidth, background, foregroundThreshold);
            if (windowCandidates.Count == 0)
            {
                return false;
            }

            candidates.AddRange(windowCandidates);
        }

        var best = SelectForegroundCandidate(candidates, polarity);
        if (best is null)
        {
            return false;
        }

        foregroundColor = AverageCandidateNeighborhood(candidates, best.Value);
        return true;
    }

    private static List<Candidate> CollectWindowCandidates(
        Mat sourceBgr,
        Mat foregroundMask,
        EdgeField edgeField,
        int centerX,
        int centerY,
        int scanWidth,
        RgbColor background,
        float foregroundThreshold)
    {
        var result = new List<Candidate>();
        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var maskIndexer = foregroundMask.GetGenericIndexer<byte>();
        var insideDistanceIndexer = edgeField.InsideDistance.GetGenericIndexer<float>();

        var minX = Math.Max(0, centerX - scanWidth);
        var maxX = Math.Min(sourceBgr.Cols - 1, centerX + scanWidth);
        var minY = Math.Max(0, centerY - scanWidth);
        var maxY = Math.Min(sourceBgr.Rows - 1, centerY + scanWidth);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (maskIndexer[y, x] == 0 || insideDistanceIndexer[y, x] < 1f)
                {
                    continue;
                }

                var pixel = sourceIndexer[y, x];
                var distance = ComputeColorDistance(pixel, background);
                if (distance < foregroundThreshold)
                {
                    continue;
                }

                var step = Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
                result.Add(new Candidate(step, x, y, pixel, distance, ComputeLuma(pixel)));
            }
        }

        return result;
    }

    private static Candidate? SelectForegroundCandidate(List<Candidate> candidates, LinePolarity polarity)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        Candidate best = candidates[0];
        foreach (var candidate in candidates)
        {
            var better = polarity switch
            {
                LinePolarity.Black => candidate.Luma < best.Luma || (Math.Abs(candidate.Luma - best.Luma) < 0.001f && candidate.Distance > best.Distance),
                LinePolarity.White => candidate.Luma > best.Luma || (Math.Abs(candidate.Luma - best.Luma) < 0.001f && candidate.Distance > best.Distance),
                _ => candidate.Distance > best.Distance || (Math.Abs(candidate.Distance - best.Distance) < 0.001f && candidate.Step > best.Step),
            };

            if (better)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static Vec3f AverageCandidateNeighborhood(List<Candidate> candidates, Candidate best)
    {
        var sumB = 0f;
        var sumG = 0f;
        var sumR = 0f;
        var count = 0;

        foreach (var candidate in candidates)
        {
            if (Math.Abs(candidate.Step - best.Step) > 1)
            {
                continue;
            }

            sumB += candidate.Pixel.Item0;
            sumG += candidate.Pixel.Item1;
            sumR += candidate.Pixel.Item2;
            count++;
        }

        if (count == 0)
        {
            return new Vec3f(best.Pixel.Item0, best.Pixel.Item1, best.Pixel.Item2);
        }

        return new Vec3f(sumB / count, sumG / count, sumR / count);
    }

    private static Mat ReconstructForegroundColors(Mat sourceBgr, Mat alpha8, RgbColor background)
    {
        var reconstructed = new Mat(sourceBgr.Rows, sourceBgr.Cols, MatType.CV_8UC3, Scalar.Black);
        var sourceIndexer = sourceBgr.GetGenericIndexer<Vec3b>();
        var alphaIndexer = alpha8.GetGenericIndexer<byte>();
        var resultIndexer = reconstructed.GetGenericIndexer<Vec3b>();

        var bgB = background.B;
        var bgG = background.G;
        var bgR = background.R;

        for (var y = 0; y < sourceBgr.Rows; y++)
        {
            for (var x = 0; x < sourceBgr.Cols; x++)
            {
                var alpha = alphaIndexer[y, x];
                if (alpha == 0)
                {
                    resultIndexer[y, x] = default;
                    continue;
                }

                if (alpha == 255)
                {
                    resultIndexer[y, x] = sourceIndexer[y, x];
                    continue;
                }

                var observed = sourceIndexer[y, x];
                var a = alpha / 255f;
                var invA = 1f / MathF.Max(a, 0.0001f);

                var reconstructedB = (observed.Item0 - ((1f - a) * bgB)) * invA;
                var reconstructedG = (observed.Item1 - ((1f - a) * bgG)) * invA;
                var reconstructedR = (observed.Item2 - ((1f - a) * bgR)) * invA;

                var stabilization = SmoothStep(0f, SmallAlphaBlendStart, a);
                reconstructedB = Lerp(observed.Item0, reconstructedB, stabilization);
                reconstructedG = Lerp(observed.Item1, reconstructedG, stabilization);
                reconstructedR = Lerp(observed.Item2, reconstructedR, stabilization);

                resultIndexer[y, x] = new Vec3b(
                    ClampToByte(reconstructedB),
                    ClampToByte(reconstructedG),
                    ClampToByte(reconstructedR));
            }
        }

        return reconstructed;
    }

    private static void SmoothAlpha(Mat alpha)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(alpha, blurred, new Size(3, 3), 0.7d, 0.7d, BorderTypes.Reflect101);
        using var partialMask = new Mat();
        Cv2.InRange(alpha, new Scalar(1), new Scalar(254), partialMask);
        using var expandedMask = new Mat();
        using var kernel = CreateKernel(1);
        Cv2.Dilate(partialMask, expandedMask, kernel);
        if (!TryGetNonZeroBounds(expandedMask, out var bounds))
        {
            return;
        }

        var alphaIndexer = alpha.GetGenericIndexer<byte>();
        var blurIndexer = blurred.GetGenericIndexer<byte>();
        var maskIndexer = expandedMask.GetGenericIndexer<byte>();

        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (var x = bounds.Left; x < bounds.Right; x++)
            {
                if (maskIndexer[y, x] == 0)
                {
                    continue;
                }

                alphaIndexer[y, x] = ClampToByte(Lerp(alphaIndexer[y, x], blurIndexer[y, x], 0.35f));
            }
        }
    }

    private static Mat ComposeStraightBgra(Mat foregroundBgr, Mat alpha8)
    {
        var result = new Mat(foregroundBgr.Rows, foregroundBgr.Cols, MatType.CV_8UC4);
        var foregroundIndexer = foregroundBgr.GetGenericIndexer<Vec3b>();
        var alphaIndexer = alpha8.GetGenericIndexer<byte>();
        var resultIndexer = result.GetGenericIndexer<Vec4b>();

        for (var y = 0; y < foregroundBgr.Rows; y++)
        {
            for (var x = 0; x < foregroundBgr.Cols; x++)
            {
                var pixel = foregroundIndexer[y, x];
                resultIndexer[y, x] = new Vec4b(pixel.Item0, pixel.Item1, pixel.Item2, alphaIndexer[y, x]);
            }
        }

        return result;
    }

    private static Mat ResizePremultiplied(Mat straightBgra, Size targetSize)
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
        Cv2.Resize(premultiplied, resizedPremultiplied, targetSize, 0d, 0d, InterpolationFlags.Lanczos4);
        return ConvertPremultipliedToStraight(resizedPremultiplied);
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
        Cv2.Threshold(alpha, outlineSourceMask, 127d, 255d, ThresholdTypes.Binary);
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
        if (sourceAlpha >= 255)
        {
            return 0f;
        }

        var signedDistance = MathF.Max(0f, outsideDistance - 1f);
        var feather = MathF.Max(0.75f, MathF.Min(1.5f, 0.55f + (thickness * 0.25f)));
        var innerEdge = MathF.Max(0f, thickness - feather);
        var outerEdge = thickness + feather;
        if (signedDistance <= innerEdge)
        {
            return 1f;
        }

        if (signedDistance >= outerEdge)
        {
            return 0f;
        }

        var t = 1f - ((signedDistance - innerEdge) / MathF.Max(0.0001f, outerEdge - innerEdge));
        return t * t * (3f - (2f * t));
    }

    private static EdgeField BuildEdgeField(Mat foregroundMask)
    {
        var edgeField = new EdgeField();
        BuildSignedDistanceField(foregroundMask, edgeField.InsideDistance, edgeField.SignedDistance, edgeField.OutsideDistance);
        Cv2.Sobel(edgeField.SignedDistance, edgeField.GradX, MatType.CV_32FC1, 1, 0, 3);
        Cv2.Sobel(edgeField.SignedDistance, edgeField.GradY, MatType.CV_32FC1, 0, 1, 3);
        return edgeField;
    }

    private static void BuildSignedDistanceField(Mat foregroundMask, Mat insideDistance, Mat signedDistance, Mat? outsideDistanceTarget = null)
    {
        using var inverse = new Mat();
        Cv2.BitwiseNot(foregroundMask, inverse);
        var outsideDistance = outsideDistanceTarget ?? new Mat();
        Cv2.DistanceTransform(foregroundMask, insideDistance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        Cv2.DistanceTransform(inverse, outsideDistance, DistanceTypes.L2, DistanceTransformMasks.Mask3);
        Cv2.Subtract(insideDistance, outsideDistance, signedDistance);
        if (outsideDistanceTarget is null)
        {
            outsideDistance.Dispose();
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
        keep[0] = false;
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

    private static Mat CreateKernel(int radius)
    {
        var size = Math.Max(1, (radius * 2) + 1);
        return Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(size, size));
    }

    private static float ComputeBackgroundDistanceThreshold(double extraction)
    {
        var t = (float)Math.Clamp(extraction, 0d, 1d);
        return 12f + (108f * t);
    }

    private static float ComputeForegroundCandidateThreshold(float backgroundThreshold)
    {
        return MathF.Max(backgroundThreshold + ForegroundCandidateMargin, ForegroundCandidateFloor);
    }

    private static float EstimateForegroundMixFactor(Vec3b pixel, Vec3f foregroundColor, RgbColor background)
    {
        var vectorB = foregroundColor.Item0 - background.B;
        var vectorG = foregroundColor.Item1 - background.G;
        var vectorR = foregroundColor.Item2 - background.R;
        var denominator = (vectorB * vectorB) + (vectorG * vectorG) + (vectorR * vectorR);
        if (denominator <= 0.001f)
        {
            return 0f;
        }

        var diffB = pixel.Item0 - background.B;
        var diffG = pixel.Item1 - background.G;
        var diffR = pixel.Item2 - background.R;
        var numerator = (diffB * vectorB) + (diffG * vectorG) + (diffR * vectorR);
        return Math.Clamp(numerator / denominator, 0f, 1f);
    }

    private static float ComputeColorDistance(Vec3b pixel, Vec3f reference)
    {
        var diffB = pixel.Item0 - reference.Item0;
        var diffG = pixel.Item1 - reference.Item1;
        var diffR = pixel.Item2 - reference.Item2;
        return MathF.Sqrt((diffB * diffB) + (diffG * diffG) + (diffR * diffR));
    }

    private static float ComputeColorDistance(Vec3b pixel, RgbColor background)
    {
        var diffB = pixel.Item0 - background.B;
        var diffG = pixel.Item1 - background.G;
        var diffR = pixel.Item2 - background.R;
        return MathF.Sqrt((diffB * diffB) + (diffG * diffG) + (diffR * diffR));
    }

    private static float ComputeLuma(Vec3b pixel) => (0.114f * pixel.Item0) + (0.587f * pixel.Item1) + (0.299f * pixel.Item2);

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        if (edge1 <= edge0)
        {
            return value >= edge1 ? 1f : 0f;
        }

        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
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

    private static byte ClampToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static float Lerp(float start, float end, float amount) => start + ((end - start) * amount);

    private readonly record struct SeedInfo(Point Point, Vec3f Color);

    private readonly record struct Candidate(int Step, int X, int Y, Vec3b Pixel, float Distance, float Luma);

    private sealed class EdgeField : IDisposable
    {
        public Mat InsideDistance { get; } = new();
        public Mat OutsideDistance { get; } = new();
        public Mat SignedDistance { get; } = new();
        public Mat GradX { get; } = new();
        public Mat GradY { get; } = new();

        public void Dispose()
        {
            InsideDistance.Dispose();
            OutsideDistance.Dispose();
            SignedDistance.Dispose();
            GradX.Dispose();
            GradY.Dispose();
        }
    }
}
