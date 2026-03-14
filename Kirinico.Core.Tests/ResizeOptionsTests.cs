using Kirinico.Core.Models;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class ResizeOptionsTests
{
    [Fact]
    public void ResolveTargetSize_UsesScalePercent()
    {
        var options = new ResizeOptions
        {
            Mode = ResizeMode.Scale,
            ScalePercent = 50d,
        };

        var size = options.ResolveTargetSize(new Size(400, 200));

        Assert.Equal(200, size.Width);
        Assert.Equal(100, size.Height);
    }

    [Fact]
    public void ResolveTargetSize_UsesWidthAndKeepsAspect()
    {
        var options = new ResizeOptions
        {
            Mode = ResizeMode.Dimensions,
            OutputWidth = 320,
        };

        var size = options.ResolveTargetSize(new Size(800, 600));

        Assert.Equal(320, size.Width);
        Assert.Equal(240, size.Height);
    }
}
