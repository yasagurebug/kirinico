using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class CharacterCutoutProcessorTests
{
    private sealed class CountingAlphaMatteEstimator : IAlphaMatteEstimator
    {
        public int CallCount { get; private set; }

        public Mat EstimateAlpha(Mat referenceBgr, Mat trimapMask, MattingMethod method, MattingSettings settings)
        {
            CallCount++;
            return trimapMask.Clone();
        }

        public void CancelCurrentRequest()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeAlphaMatteEstimator : IAlphaMatteEstimator
    {
        public Mat EstimateAlpha(Mat referenceBgr, Mat trimapMask, MattingMethod method, MattingSettings settings)
            => trimapMask.Clone();

        public void CancelCurrentRequest()
        {
        }

        public void Dispose()
        {
        }
    }

    private static ResizeOptions DefaultResize() =>
        new()
        {
            Mode = ResizeMode.Scale,
            ScalePercent = 100d,
        };

    private static CutoutParameters CreateParameters(
        BackgroundSpecificationMode backgroundSpecificationMode = BackgroundSpecificationMode.ManualSeed,
        RgbColor? backgroundColor = null,
        double backgroundTolerance = 0.5d,
        double contourTolerance = 0.4d,
        double denoiseStrength = 0.3d,
        double transparencyCut = 0.15d,
        int maxContourWidthPx = 32,
        int despillExpansionPx = 1) =>
        new()
        {
            BackgroundSpecificationMode = backgroundSpecificationMode,
            BackgroundColor = backgroundColor ?? new RgbColor(255, 255, 255),
            BackgroundTolerance = backgroundTolerance,
            ContourTolerance = contourTolerance,
            MaxContourWidthPx = maxContourWidthPx,
            DenoiseStrength = denoiseStrength,
            TransparencyCut = transparencyCut,
            DespillExpansionPx = despillExpansionPx,
            DespillMix = 0.5d,
            DespillExpand = 0d,
            DespillBrightness = 0d,
            Resize = DefaultResize(),
        };

    private static CharacterCutoutProcessor CreateProcessor(IAlphaMatteEstimator? alphaMatteEstimator = null)
        => new(alphaMatteEstimator ?? new FakeAlphaMatteEstimator());

    private static ManualEditMaps CreateSeedMaps(Mat image, params Point[] points)
    {
        var seedMap = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0d));
        foreach (var point in points)
        {
            seedMap.Set(point.Y, point.X, 255);
        }

        return new ManualEditMaps
        {
            BackgroundSeedAddMap = seedMap,
        };
    }

    [Fact]
    public void SampleBackgroundColor_ReturnsLocalAverage()
    {
        using var image = new Mat(5, 5, MatType.CV_8UC3, new Scalar(20, 30, 40));
        image.Set(2, 2, new Vec3b(100, 110, 120));

        using var processor = CreateProcessor();
        var sampled = processor.SampleBackgroundColor(image, new Point(2, 2), radius: 0);

        Assert.Equal(new RgbColor(120, 110, 100), sampled);
    }

    [Fact]
    public void Process_ColorRangeMode_ProducesForegroundAndBackground()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(18, 18, 28, 28), new Scalar(12, 24, 36), -1);

        using var processor = CreateProcessor();
        using var result = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.2d,
                contourTolerance: 0.0d));

        Assert.True(result.AlphaMask.Get<byte>(32, 32) >= 120);
        Assert.True(result.AlphaMask.Get<byte>(4, 4) <= 16);
        Assert.True(result.TrimapMask.Get<byte>(32, 32) >= 120);
        Assert.Equal(0, result.TrimapMask.Get<byte>(4, 4));
    }

    [Fact]
    public void ProcessPreResize_SkipsPyMattingWhenTrimapHasTooFewUnknownPixels()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(16, 16, 32, 32), new Scalar(0, 0, 0), -1);

        using var estimator = new CountingAlphaMatteEstimator();
        using var processor = CreateProcessor(estimator);
        using var preResize = processor.ProcessPreResize(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.2d,
                contourTolerance: 0.0d,
                denoiseStrength: 0.0d));

        Assert.Equal(0, estimator.CallCount);
        Assert.Equal(255, preResize.AlphaMask.Get<byte>(32, 32));
        Assert.Equal(0, preResize.AlphaMask.Get<byte>(4, 4));
    }

    [Fact]
    public void PrepareTrimap_DoesNotInvokeAlphaMatting()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(16, 16, 32, 32), new Scalar(0, 0, 0), -1);

        using var estimator = new CountingAlphaMatteEstimator();
        using var processor = CreateProcessor(estimator);
        using var prepared = processor.PrepareTrimap(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.2d,
                contourTolerance: 0.4d,
                denoiseStrength: 0.0d));

        Assert.Equal(0, estimator.CallCount);
        Assert.Equal(image.Size(), prepared.TrimapMask.Size());
        Assert.Equal(image.Size(), prepared.ReferenceBgr.Size());
        Assert.Equal(image.Size(), prepared.OriginalBgr.Size());
    }

    [Fact]
    public void EstimateAlphaPreviewFromTrimap_RestoresAlphaToOriginalSize()
    {
        using var original = new Mat(20, 20, MatType.CV_8UC3, new Scalar(255, 255, 255));
        using var reference = original.Clone();
        using var trimap = new Mat(20, 20, MatType.CV_8UC1, new Scalar(128));
        using var prepared = new TrimapPreparationResult(original.Clone(), reference.Clone(), trimap.Clone(), new RgbColor(255, 255, 255));
        using var estimator = new CountingAlphaMatteEstimator();
        using var processor = CreateProcessor(estimator);

        using var result = processor.EstimateAlphaPreviewFromTrimap(
            prepared,
            CreateParameters(backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange),
            0.25d);

        Assert.NotNull(result);
        Assert.Equal(trimap.Size(), result!.AlphaMask.Size());
        Assert.Equal(original.Size(), result.OriginalBgr.Size());
        Assert.Equal(1, estimator.CallCount);
    }

    [Fact]
    public void FinalizeFromPreResize_ClampsNearOpaqueAlphaToFullOpacity()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 40, 80));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, new Scalar(255));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, new Scalar(250));

        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0));
        using var processor = CreateProcessor();
        using var result = processor.FinalizeFromPreResize(preResize, CreateParameters(backgroundColor: new RgbColor(0, 255, 0)));

        Assert.Equal(255, result.FinalRgba.Get<Vec4b>(0, 0).Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_DespillExpansionChangesPixelsNearPartialAlphaBand()
    {
        using var original = new Mat(3, 3, MatType.CV_8UC3, new Scalar(10, 210, 10));
        using var trimap = new Mat(3, 3, MatType.CV_8UC1, new Scalar(255));
        using var alpha = new Mat(3, 3, MatType.CV_8UC1, new Scalar(255));
        alpha.Set(1, 1, 128);

        using var processor = CreateProcessor();
        using var baseline = processor.FinalizeFromPreResize(
            new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0)),
            CreateParameters(backgroundColor: new RgbColor(0, 255, 0), despillExpansionPx: 0));
        using var expanded = processor.FinalizeFromPreResize(
            new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0)),
            CreateParameters(backgroundColor: new RgbColor(0, 255, 0), despillExpansionPx: 1));

        Assert.NotEqual(
            baseline.FinalRgba.Get<Vec4b>(1, 0),
            expanded.FinalRgba.Get<Vec4b>(1, 0));
    }

    [Fact]
    public void FinalizeFromPreResize_DespillParametersAffectPartialAlphaPixel()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(20, 220, 30));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, new Scalar(255));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, new Scalar(160));

        var mildParameters = CreateParameters(backgroundColor: new RgbColor(0, 255, 0));
        mildParameters.DespillMix = 0.2d;
        mildParameters.DespillExpand = 0d;
        mildParameters.DespillBrightness = 0d;

        var strongParameters = CreateParameters(backgroundColor: new RgbColor(0, 255, 0));
        strongParameters.DespillMix = 1.0d;
        strongParameters.DespillExpand = 1.0d;
        strongParameters.DespillBrightness = 10d;

        using var processor = CreateProcessor();
        using var mild = processor.FinalizeFromPreResize(
            new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0)),
            mildParameters);
        using var strong = processor.FinalizeFromPreResize(
            new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0)),
            strongParameters);

        Assert.NotEqual(mild.FinalRgba.Get<Vec4b>(0, 0), strong.FinalRgba.Get<Vec4b>(0, 0));
    }

    [Fact]
    public void Process_ManualSeedMode_UsesSeedBackground()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(20, 20, 20, 20), new Scalar(40, 20, 10), -1);
        using var maps = CreateSeedMaps(image, new Point(2, 2));

        using var processor = CreateProcessor();
        using var result = processor.Process(
            image,
            CreateParameters(backgroundSpecificationMode: BackgroundSpecificationMode.ManualSeed),
            maps);

        Assert.Equal(0, result.AlphaMask.Get<byte>(2, 2));
        Assert.True(result.AlphaMask.Get<byte>(30, 30) > 0);
    }
}
