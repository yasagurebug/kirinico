using Kirinico.Core.Services;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class AlphaPostProcessorTests
{
    [Fact]
    public void CreateBinaryAlphaMask_MapsForegroundToOpaque()
    {
        using var trimap = new Mat(1, 3, MatType.CV_8UC1, new Scalar(0));
        trimap.Set(0, 1, 128);
        trimap.Set(0, 2, 255);

        using var alpha = AlphaPostProcessor.CreateBinaryAlphaMask(trimap);

        Assert.Equal(0, alpha.Get<byte>(0, 0));
        Assert.Equal(0, alpha.Get<byte>(0, 1));
        Assert.Equal(255, alpha.Get<byte>(0, 2));
    }
}
