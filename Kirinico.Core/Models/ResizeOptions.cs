using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class ResizeOptions
{
    public ResizeMode Mode { get; set; } = ResizeMode.Scale;

    public double ScalePercent { get; set; } = 100d;

    public int OutputWidth { get; set; }

    public int OutputHeight { get; set; }

    public Size ResolveTargetSize(Size sourceSize)
    {
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
        {
            return sourceSize;
        }

        if (Mode == ResizeMode.Scale)
        {
            var scale = Math.Max(1d, ScalePercent) / 100d;
            return new Size(
                Math.Max(1, (int)Math.Round(sourceSize.Width * scale)),
                Math.Max(1, (int)Math.Round(sourceSize.Height * scale)));
        }

        if (OutputWidth > 0)
        {
            var width = OutputWidth;
            var height = Math.Max(1, (int)Math.Round(width * (sourceSize.Height / (double)sourceSize.Width)));
            return new Size(width, height);
        }

        if (OutputHeight > 0)
        {
            var height = OutputHeight;
            var width = Math.Max(1, (int)Math.Round(height * (sourceSize.Width / (double)sourceSize.Height)));
            return new Size(width, height);
        }

        return sourceSize;
    }
}
