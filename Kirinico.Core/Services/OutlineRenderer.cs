using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

internal static class OutlineRenderer
{
    private const float OutlineAntialiasWidth = 1.0f;

    public static Mat ApplyOutline(Mat straightBgra, OutlineOptions outline)
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

    private static Mat ExtractAlpha(Mat straightBgra)
    {
        var alpha = new Mat();
        Cv2.ExtractChannel(straightBgra, alpha, 3);
        return alpha;
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
}
