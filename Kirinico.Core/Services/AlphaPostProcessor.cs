using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Services;

internal static class AlphaPostProcessor
{
    public static Mat RestoreStraightBgra(Mat originalBgr, Mat alphaMask, RgbColor background, CutoutParameters parameters)
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

    public static Mat ResizePremultiplied(Mat straightBgra, Size targetSize, ResizeInterpolationMode interpolation)
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

    public static Mat CreateBinaryAlphaMask(Mat trimapMask)
    {
        var alphaMask = new Mat(trimapMask.Rows, trimapMask.Cols, MatType.CV_8UC1, Scalar.Black);
        using var foregroundMask = new Mat();
        Cv2.Compare(trimapMask, new Scalar(255), foregroundMask, CmpTypes.EQ);
        alphaMask.SetTo(new Scalar(255), foregroundMask);
        return alphaMask;
    }

    public static int CountUnknownPixels(Mat trimapMask)
    {
        using var unknownMask = new Mat();
        Cv2.Compare(trimapMask, new Scalar(128), unknownMask, CmpTypes.EQ);
        return Cv2.CountNonZero(unknownMask);
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

    private static Mat CreateKernel(int radius)
    {
        var size = Math.Max(1, (radius * 2) + 1);
        return Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(size, size));
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

    private static float Lerp(float start, float end, float amount) => start + ((end - start) * Math.Clamp(amount, 0f, 1f));

    private static Vec3f Cross(Vec3f left, Vec3f right)
        => new(
            (left.Item1 * right.Item2) - (left.Item2 * right.Item1),
            (left.Item2 * right.Item0) - (left.Item0 * right.Item2),
            (left.Item0 * right.Item1) - (left.Item1 * right.Item0));

    private static byte ClampToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
