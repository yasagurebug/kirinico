using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

internal static class BackgroundSampler
{
    public static RgbColor SampleBackgroundColor(Mat sourceBgr, Point center, int radius = 6)
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

    public static List<SeedInfo> CollectBackgroundSeeds(Mat sourceBgr, Mat? backgroundSeedAddMap)
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

    public static RgbColor ResolveBackgroundColor(CutoutParameters parameters, IReadOnlyList<SeedInfo> seeds)
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

    private static byte ClampToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    internal readonly record struct SeedInfo(Point Point, Vec3f Color);
}
