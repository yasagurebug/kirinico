using OpenCvSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kirinico.App.Services;

public static class BitmapSourceFactory
{
    public static BitmapSource FromBgr(Mat source) => CreateBitmapSource(source, PixelFormats.Bgr24);

    public static BitmapSource FromGray(Mat source) => CreateBitmapSource(source, PixelFormats.Gray8);

    public static BitmapSource FromBgra(Mat source) => CreateBitmapSource(source, PixelFormats.Bgra32);

    public static BitmapSource FromAlphaPreview(Mat alphaMask)
    {
        ArgumentNullException.ThrowIfNull(alphaMask);

        if (alphaMask.Empty())
        {
            throw new ArgumentException("Source image is empty.", nameof(alphaMask));
        }

        if (alphaMask.Type() != MatType.CV_8UC1)
        {
            throw new ArgumentException("Alpha preview source must be CV_8UC1.", nameof(alphaMask));
        }

        using var bgra = new Mat(alphaMask.Rows, alphaMask.Cols, MatType.CV_8UC4, new Scalar(255, 255, 255, 0));
        Cv2.InsertChannel(alphaMask, bgra, 3);
        return CreateBitmapSource(bgra, PixelFormats.Bgra32);
    }

    public static BitmapSource FromTrimapPreview(Mat originalBgr, Mat trimapMask)
    {
        ArgumentNullException.ThrowIfNull(originalBgr);
        ArgumentNullException.ThrowIfNull(trimapMask);

        if (originalBgr.Empty() || trimapMask.Empty())
        {
            throw new ArgumentException("Trimap preview source is empty.");
        }

        using var preview = new Mat(originalBgr.Rows, originalBgr.Cols, MatType.CV_8UC3, Scalar.Black);
        var originalIndexer = originalBgr.GetGenericIndexer<Vec3b>();
        var trimapIndexer = trimapMask.GetGenericIndexer<byte>();
        var previewIndexer = preview.GetGenericIndexer<Vec3b>();

        for (var y = 0; y < originalBgr.Rows; y++)
        {
            for (var x = 0; x < originalBgr.Cols; x++)
            {
                var trimap = trimapIndexer[y, x];
                if (trimap <= 0)
                {
                    previewIndexer[y, x] = new Vec3b(0, 0, 0);
                    continue;
                }

                var source = originalIndexer[y, x];
                if (trimap >= 255)
                {
                    previewIndexer[y, x] = source;
                    continue;
                }

                previewIndexer[y, x] = new Vec3b(
                    (byte)Math.Clamp((int)Math.Round(source.Item0 * 0.5d), 0, 255),
                    (byte)Math.Clamp((int)Math.Round(source.Item1 * 0.5d), 0, 255),
                    (byte)Math.Clamp((int)Math.Round((source.Item2 * 0.5d) + 127.5d), 0, 255));
            }
        }

        return CreateBitmapSource(preview, PixelFormats.Bgr24);
    }

    private static BitmapSource CreateBitmapSource(Mat source, PixelFormat pixelFormat)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Empty())
        {
            throw new ArgumentException("Source image is empty.", nameof(source));
        }

        var stride = (int)source.Step();
        var buffer = new byte[stride * source.Rows];
        System.Runtime.InteropServices.Marshal.Copy(source.Data, buffer, 0, buffer.Length);

        var bitmap = BitmapSource.Create(source.Width, source.Height, 96d, 96d, pixelFormat, null, buffer, stride);
        bitmap.Freeze();
        return bitmap;
    }
}
