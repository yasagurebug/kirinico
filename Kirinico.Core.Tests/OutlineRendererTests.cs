using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class OutlineRendererTests
{
    [Fact]
    public void ApplyOutline_Disabled_ReturnsClone()
    {
        using var image = new Mat(1, 1, MatType.CV_8UC4, new Scalar(1, 2, 3, 255));

        using var outlined = OutlineRenderer.ApplyOutline(image, new OutlineOptions { Enabled = false, Thickness = 2d });

        Assert.Equal(image.Get<Vec4b>(0, 0), outlined.Get<Vec4b>(0, 0));
        Assert.NotSame(image, outlined);
    }
}
