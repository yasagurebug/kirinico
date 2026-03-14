using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kirinico.Core.Services;

public static class ImageLoadService
{
    public static Mat LoadColorImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var image = Cv2.ImRead(path, ImreadModes.Color);
        if (!image.Empty())
        {
            return image;
        }

        return LoadWithWic(path);
    }

    private static Mat LoadWithWic(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault() ?? throw new InvalidOperationException("画像を読み込めませんでした。");

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0d);
        var stride = converted.PixelWidth * 4;
        var buffer = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(buffer, stride, 0);

        using var bgra = new Mat(converted.PixelHeight, converted.PixelWidth, MatType.CV_8UC4);
        Marshal.Copy(buffer, 0, bgra.Data, buffer.Length);

        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }
}
