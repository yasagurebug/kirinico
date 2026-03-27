using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class BackgroundSamplerTests
{
    [Fact]
    public void SampleBackgroundColor_ReturnsAverageAroundPoint()
    {
        using var image = new Mat(3, 3, MatType.CV_8UC3, new Scalar(10, 20, 30));
        image.Set(1, 1, new Vec3b(40, 50, 60));

        var sampled = BackgroundSampler.SampleBackgroundColor(image, new Point(1, 1), radius: 0);

        Assert.Equal(new RgbColor(60, 50, 40), sampled);
    }
}
