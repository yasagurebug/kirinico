using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class TrimapBuilderTests
{
    [Fact]
    public void Prepare_ManualSeedModeWithoutSeed_Throws()
    {
        using var image = new Mat(8, 8, MatType.CV_8UC3, new Scalar(255, 255, 255));
        var parameters = new CutoutParameters
        {
            BackgroundSpecificationMode = BackgroundSpecificationMode.ManualSeed,
            Resize = new ResizeOptions { Mode = ResizeMode.Scale, ScalePercent = 100d },
        };

        Assert.Throws<InvalidOperationException>(() => TrimapBuilder.Prepare(image, parameters, null, null));
    }
}
