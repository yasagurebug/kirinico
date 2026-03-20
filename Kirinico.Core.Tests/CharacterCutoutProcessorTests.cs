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
        {
            ArgumentNullException.ThrowIfNull(trimapMask);
            return trimapMask.Clone();
        }

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
        double edgeCorrectionStrength = 0.5d,
        int maxContourWidthPx = 32,
        int despillDetectionWidthPx = 3,
        RgbColor? edgeColor = null) =>
        new()
        {
            BackgroundSpecificationMode = backgroundSpecificationMode,
            BackgroundColor = backgroundColor ?? new RgbColor(255, 255, 255),
            BackgroundTolerance = backgroundTolerance,
            ContourTolerance = contourTolerance,
            MaxContourWidthPx = maxContourWidthPx,
            DespillDetectionWidthPx = despillDetectionWidthPx,
            DenoiseStrength = denoiseStrength,
            TransparencyCut = transparencyCut,
            EdgeCorrectionStrength = edgeCorrectionStrength,
            EdgeRepresentativeColor = edgeColor,
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
    public void Process_ColorRangeMode_StrongColorDifferenceNearBackgroundCanRemainUnknownAtMaxContourTolerance()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(1, 1, 10, 10), new Scalar(0, 0, 0), -1);

        using var processor = CreateProcessor();
        using var result = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.2d,
                contourTolerance: 1.0d,
                denoiseStrength: 0.0d));

        Assert.Equal(128, result.TrimapMask.Get<byte>(5, 5));
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
    public void Process_ColorRangeMode_MinBackgroundRemovalDoesNotImmediatelyClassifyNearBackgroundPixelAsBackground()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(31, 31, 3, 3), new Scalar(255, 255, 252), -1);

        using var processor = CreateProcessor();
        using var weakResult = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.0d,
                denoiseStrength: 0.0d,
                contourTolerance: 0.0d));
        using var strongResult = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 1.0d,
                denoiseStrength: 0.0d,
                contourTolerance: 0.0d));

        Assert.NotEqual(0, weakResult.TrimapMask.Get<byte>(32, 32));
        Assert.Equal(0, strongResult.TrimapMask.Get<byte>(32, 32));
    }

    [Fact]
    public void Process_ManualSeedMode_CanMarkEnclosedHoleAsBackground()
    {
        using var image = new Mat(96, 96, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Circle(image, new Point(48, 48), 28, new Scalar(20, 20, 20), 8);

        using var seeds = CreateSeedMaps(image, new Point(0, 0), new Point(48, 48));
        using var processor = CreateProcessor();
        using var result = processor.Process(
            image,
            CreateParameters(backgroundTolerance: 0.2d),
            seeds);

        Assert.True(result.AlphaMask.Get<byte>(48, 48) <= 16);
        Assert.True(result.AlphaMask.Get<byte>(48, 20) >= 120);
    }

    [Fact]
    public void Process_ContourToleranceControlsUnknownBandThickness()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(31, 31, 3, 3), new Scalar(250, 250, 250), -1);

        using var processor = CreateProcessor();
        using var minWidthResult = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.0d,
                denoiseStrength: 0.0d,
                contourTolerance: 0.0d));
        using var maxWidthResult = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                backgroundTolerance: 0.0d,
                denoiseStrength: 0.0d,
                contourTolerance: 1.0d));

        Assert.Equal(255, minWidthResult.TrimapMask.Get<byte>(32, 32));
        Assert.Equal(128, maxWidthResult.TrimapMask.Get<byte>(32, 32));
    }

    [Fact]
    public void Process_DistanceFromBackgroundOnlyMode_LeavesStrongColorDifferenceNearBackgroundAsUnknown()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(1, 1, 10, 10), new Scalar(0, 0, 0), -1);

        var defaultParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(255, 255, 255),
            backgroundTolerance: 0.0d,
            denoiseStrength: 0.0d,
            contourTolerance: 0.0d,
            maxContourWidthPx: 32);
        var experimentParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(255, 255, 255),
            backgroundTolerance: 0.0d,
            denoiseStrength: 0.0d,
            contourTolerance: 1.0d,
            maxContourWidthPx: 32);
        experimentParameters.Internal.BackgroundThreshold.DistanceFromBackgroundOnly = true;

        using var processor = CreateProcessor();
        using var defaultResult = processor.Process(image, defaultParameters);
        using var experimentResult = processor.Process(image, experimentParameters);

        Assert.Equal(255, defaultResult.TrimapMask.Get<byte>(5, 5));
        Assert.Equal(128, experimentResult.TrimapMask.Get<byte>(5, 5));
    }

    [Fact]
    public void Process_ThrowsWhenNoBackgroundSeedExistsInManualSeedMode()
    {
        using var image = new Mat(32, 32, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(8, 8, 16, 16), new Scalar(16, 16, 16), -1);
        using var emptySeed = new Mat(32, 32, MatType.CV_8UC1, Scalar.All(0d));
        using var manualMaps = new ManualEditMaps { BackgroundSeedAddMap = emptySeed.Clone() };
        using var processor = CreateProcessor();

        Assert.Throws<InvalidOperationException>(() => processor.Process(image, CreateParameters(), manualMaps));
    }

    [Fact]
    public void FinalizeFromPreResize_MatchesDirectProcess()
    {
        using var image = new Mat(80, 80, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(18, 18, 44, 44), new Scalar(32, 48, 64), 2);
        Cv2.Rectangle(image, new Rect(21, 21, 38, 38), new Scalar(100, 150, 210), -1);

        var parameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(255, 255, 255),
            backgroundTolerance: 0.2d,
            contourTolerance: 0.4d,
            edgeColor: new RgbColor(64, 48, 32));
        parameters.Resize.ScalePercent = 150d;
        parameters.Outline = new OutlineOptions
        {
            Enabled = true,
            Color = new RgbColor(0, 0, 0),
            Thickness = 1.5d,
        };

        using var processor = CreateProcessor();
        using var direct = processor.Process(image, parameters);
        using var preResize = processor.ProcessPreResize(image, parameters);
        using var finalized = processor.FinalizeFromPreResize(preResize, parameters);

        Assert.Equal(direct.ResolvedBackgroundColor, finalized.ResolvedBackgroundColor);
        Assert.Equal(direct.AlphaMask.Size(), finalized.AlphaMask.Size());
        Assert.Equal(direct.TrimapMask.Size(), finalized.TrimapMask.Size());
        Assert.Equal(direct.FinalRgba.Size(), finalized.FinalRgba.Size());

        using var alphaDiff = new Mat();
        Cv2.Absdiff(direct.AlphaMask, finalized.AlphaMask, alphaDiff);
        Assert.Equal(0, Cv2.CountNonZero(alphaDiff));

        using var trimapDiff = new Mat();
        Cv2.Absdiff(direct.TrimapMask, finalized.TrimapMask, trimapDiff);
        Assert.Equal(0, Cv2.CountNonZero(trimapDiff));

        using var rgbaDiff = new Mat();
        Cv2.Absdiff(direct.FinalRgba, finalized.FinalRgba, rgbaDiff);
        var rgbaChannels = rgbaDiff.Split();
        Assert.All(rgbaChannels, static channel =>
        {
            using (channel)
            {
                Assert.Equal(0, Cv2.CountNonZero(channel));
            }
        });
    }

    [Fact]
    public void PrepareTrimap_MatchesProcessPreResizeTrimap()
    {
        using var image = new Mat(80, 80, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(18, 18, 44, 44), new Scalar(32, 48, 64), 2);
        Cv2.Rectangle(image, new Rect(21, 21, 38, 38), new Scalar(100, 150, 210), -1);

        var parameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(255, 255, 255),
            backgroundTolerance: 0.2d,
            contourTolerance: 0.4d,
            edgeColor: new RgbColor(64, 48, 32));

        using var processor = CreateProcessor();
        using var prepared = processor.PrepareTrimap(image, parameters);
        using var preResize = processor.ProcessPreResize(image, parameters);
        using var trimapDiff = new Mat();
        Cv2.Absdiff(prepared.TrimapMask, preResize.TrimapMask, trimapDiff);
        Assert.Equal(0, Cv2.CountNonZero(trimapDiff));
    }

    [Fact]
    public void FinalizeFromPreResize_EdgeCorrectionPullsSemiTransparentPixelTowardRepresentativeColor()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(232, 232, 232));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(128));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(128));
        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(255, 255, 255));

        using var processor = CreateProcessor();

        var plainParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(255, 255, 255),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            edgeColor: null);
        plainParameters.EnableEdgeColorCorrection = true;

        using var plain = processor.FinalizeFromPreResize(
            preResize,
            plainParameters);

        var correctedParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(255, 255, 255),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 1.0d,
            edgeColor: new RgbColor(0, 0, 0));
        correctedParameters.EnableEdgeColorCorrection = true;

        using var corrected = processor.FinalizeFromPreResize(
            preResize,
            correctedParameters);

        var plainPixel = plain.FinalRgba.At<Vec4b>(0, 0);
        var correctedPixel = corrected.FinalRgba.At<Vec4b>(0, 0);

        Assert.NotEqual(plainPixel, correctedPixel);
        Assert.Equal(plainPixel.Item3, correctedPixel.Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_LowAlphaRepresentativeColorCorrectionIsStrongBelowHalfAlpha()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(8, 248, 8));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(128));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(77));
        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0));

        using var processor = CreateProcessor();

        var plainParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            edgeColor: null);
        plainParameters.EnableEdgeColorCorrection = true;

        using var plain = processor.FinalizeFromPreResize(
            preResize,
            plainParameters);

        var mediumParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.5d,
            edgeColor: new RgbColor(0, 0, 0));
        mediumParameters.EnableEdgeColorCorrection = true;

        using var medium = processor.FinalizeFromPreResize(
            preResize,
            mediumParameters);

        var strongParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 1.0d,
            edgeColor: new RgbColor(0, 0, 0));
        strongParameters.EnableEdgeColorCorrection = true;

        using var strong = processor.FinalizeFromPreResize(
            preResize,
            strongParameters);

        var plainPixel = plain.FinalRgba.At<Vec4b>(0, 0);
        var mediumPixel = medium.FinalRgba.At<Vec4b>(0, 0);
        var strongPixel = strong.FinalRgba.At<Vec4b>(0, 0);

        Assert.NotEqual(plainPixel, mediumPixel);
        Assert.NotEqual(plainPixel, strongPixel);
        Assert.NotEqual(mediumPixel, strongPixel);
        Assert.Equal(plainPixel.Item3, mediumPixel.Item3);
        Assert.Equal(plainPixel.Item3, strongPixel.Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_DespillIsFixedAndIndependentOfEdgeCorrectionStrength()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(48, 220, 140));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(230));
        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0));

        using var processor = CreateProcessor();

        using var weak = processor.FinalizeFromPreResize(
            preResize,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(0, 255, 0),
                transparencyCut: 0.0d,
                edgeCorrectionStrength: 0.0d,
                edgeColor: null));

        using var strong = processor.FinalizeFromPreResize(
            preResize,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(0, 255, 0),
                transparencyCut: 0.0d,
                edgeCorrectionStrength: 1.0d,
                edgeColor: null));

        var weakPixel = weak.FinalRgba.At<Vec4b>(0, 0);
        var strongPixel = strong.FinalRgba.At<Vec4b>(0, 0);

        Assert.Equal(weakPixel, strongPixel);
        Assert.Equal(230, weakPixel.Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_RestoreComplementProjectionMaxChangesSimpleRestoreResult()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(40, 220, 80));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(230));
        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0));

        var unrestrictedParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            edgeColor: null);
        unrestrictedParameters.Internal.AlphaColorRestore.DespillStrength = 0.0d;
        unrestrictedParameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        unrestrictedParameters.Internal.AlphaColorRestore.RestoreComplementProjectionMax = 0.0d;
        unrestrictedParameters.DespillDetectionStrength = 1.0d;

        var limitedParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            edgeColor: null);
        limitedParameters.Internal.AlphaColorRestore.DespillStrength = 0.0d;
        limitedParameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        limitedParameters.Internal.AlphaColorRestore.RestoreComplementProjectionMax = 0.01d;
        limitedParameters.DespillDetectionStrength = 1.0d;

        using var processor = CreateProcessor();
        using var unrestricted = processor.FinalizeFromPreResize(preResize, unrestrictedParameters);
        using var limited = processor.FinalizeFromPreResize(preResize, limitedParameters);

        var unrestrictedPixel = unrestricted.FinalRgba.At<Vec4b>(0, 0);
        var limitedPixel = limited.FinalRgba.At<Vec4b>(0, 0);

        Assert.NotEqual(unrestrictedPixel, limitedPixel);
        Assert.Equal(unrestrictedPixel.Item3, limitedPixel.Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_EdgeCorrectionAppliesToBackgroundClosePixelsBelowAlphaThreshold()
    {
        using var original = new Mat(1, 3, MatType.CV_8UC3);
        original.Set(0, 0, new Vec3b(20, 230, 20));
        original.Set(0, 1, new Vec3b(20, 20, 20));
        original.Set(0, 2, new Vec3b(20, 230, 20));
        using var trimap = new Mat(1, 3, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 3, MatType.CV_8UC1);
        alpha.Set(0, 0, (byte)180);
        alpha.Set(0, 1, (byte)180);
        alpha.Set(0, 2, (byte)250);
        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0));

        using var processor = CreateProcessor();
        using var plain = processor.FinalizeFromPreResize(
            preResize,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(0, 255, 0),
                transparencyCut: 0.0d,
                edgeCorrectionStrength: 0.0d,
                edgeColor: null,
                maxContourWidthPx: 32));
        using var corrected = processor.FinalizeFromPreResize(
            preResize,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(0, 255, 0),
                transparencyCut: 0.0d,
                edgeCorrectionStrength: 1.0d,
                edgeColor: new RgbColor(0, 0, 0),
                maxContourWidthPx: 32));

        var plainNearPixel = plain.FinalRgba.At<Vec4b>(0, 0);
        var plainFarPixel = plain.FinalRgba.At<Vec4b>(0, 1);
        var plainHighAlphaPixel = plain.FinalRgba.At<Vec4b>(0, 2);
        var nearPixel = corrected.FinalRgba.At<Vec4b>(0, 0);
        var farPixel = corrected.FinalRgba.At<Vec4b>(0, 1);
        var highAlphaPixel = corrected.FinalRgba.At<Vec4b>(0, 2);

        Assert.NotEqual(plainNearPixel, nearPixel);
        Assert.Equal(plainFarPixel.Item3, farPixel.Item3);
        Assert.Equal(plainHighAlphaPixel, highAlphaPixel);
    }

    [Fact]
    public void Process_FractionalOutlineThicknessExtendsAlphaOutsideForeground()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(20, 20, 24, 24), new Scalar(20, 20, 20), -1);

        using var processor = CreateProcessor();
        using var plain = processor.Process(
            image,
            new CutoutParameters
            {
                BackgroundSpecificationMode = BackgroundSpecificationMode.ColorRange,
                BackgroundColor = new RgbColor(255, 255, 255),
                BackgroundTolerance = 0.2d,
                ContourTolerance = 0.0d,
                Resize = DefaultResize(),
            });
        using var outlined = processor.Process(
            image,
            new CutoutParameters
            {
                BackgroundSpecificationMode = BackgroundSpecificationMode.ColorRange,
                BackgroundColor = new RgbColor(255, 255, 255),
                BackgroundTolerance = 0.2d,
                ContourTolerance = 0.0d,
                Resize = DefaultResize(),
                Outline = new OutlineOptions
                {
                    Enabled = true,
                    Color = new RgbColor(0, 0, 0),
                    Thickness = 2.25d,
                },
            });

        var plainOuterAlphaCount = 0;
        var outlinedOuterAlphaCount = 0;
        for (var y = 17; y <= 46; y++)
        {
            for (var x = 17; x <= 46; x++)
            {
                var insideOriginal = x >= 20 && x <= 43 && y >= 20 && y <= 43;
                if (insideOriginal)
                {
                    continue;
                }

                if (plain.FinalRgba.At<Vec4b>(y, x).Item3 > 0)
                {
                    plainOuterAlphaCount++;
                }

                if (outlined.FinalRgba.At<Vec4b>(y, x).Item3 > 0)
                {
                    outlinedOuterAlphaCount++;
                }
            }
        }

        Assert.True(outlinedOuterAlphaCount > plainOuterAlphaCount);
    }

    [Fact]
    public void FinalizeFromPreResize_OutlineStartsFromAnyNonZeroAlpha()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(20, 20, 24, 24), new Scalar(20, 20, 20), -1);

        using var trimap = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(0d));
        using var alpha = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(0d));
        Cv2.Rectangle(trimap, new Rect(20, 20, 24, 24), new Scalar(255), -1);
        Cv2.Rectangle(alpha, new Rect(20, 20, 24, 24), new Scalar(64), -1);

        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), image.Clone(), new RgbColor(255, 255, 255));
        using var processor = CreateProcessor();
        using var plain = processor.FinalizeFromPreResize(
            preResize,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                transparencyCut: 0.0d,
                contourTolerance: 0.0d));
        using var outlined = processor.FinalizeFromPreResize(
            preResize,
            new CutoutParameters
            {
                BackgroundSpecificationMode = BackgroundSpecificationMode.ColorRange,
                BackgroundColor = new RgbColor(255, 255, 255),
                TransparencyCut = 0.0d,
                ContourTolerance = 0.0d,
                Resize = DefaultResize(),
                Outline = new OutlineOptions
                {
                    Enabled = true,
                    Color = new RgbColor(0, 0, 0),
                    Thickness = 2.0d,
                },
            });

        var outlinedOuterAlphaCount = 0;
        for (var y = 17; y <= 46; y++)
        {
            for (var x = 17; x <= 46; x++)
            {
                var insideOriginal = x >= 20 && x <= 43 && y >= 20 && y <= 43;
                if (insideOriginal)
                {
                    continue;
                }

                Assert.Equal(0, plain.FinalRgba.At<Vec4b>(y, x).Item3);
                if (outlined.FinalRgba.At<Vec4b>(y, x).Item3 > 0)
                {
                    outlinedOuterAlphaCount++;
                }
            }
        }

        Assert.True(outlinedOuterAlphaCount > 0);
    }

    [Fact]
    public void FinalizeFromPreResize_DespillOnlyAppliesWithinMaxContourWidth()
    {
        using var original = new Mat(1, 5, MatType.CV_8UC3, new Scalar(48, 220, 140));
        using var trimap = new Mat(1, 5, MatType.CV_8UC1, Scalar.All(255));
        trimap.Set(0, 1, 128);
        trimap.Set(0, 2, 128);
        trimap.Set(0, 3, 128);
        using var alpha = new Mat(1, 5, MatType.CV_8UC1);
        alpha.Set(0, 0, 0);
        alpha.Set(0, 1, 230);
        alpha.Set(0, 2, 230);
        alpha.Set(0, 3, 230);
        alpha.Set(0, 4, 230);
        using var preResize = new PreResizeCutoutResult(
            trimap.Clone(),
            alpha.Clone(),
            original.Clone(),
            new RgbColor(0, 255, 0));

        using var processor = CreateProcessor();
        using var result = processor.FinalizeFromPreResize(
            preResize,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(0, 255, 0),
                transparencyCut: 0.0d,
                edgeCorrectionStrength: 0.0d,
                maxContourWidthPx: 32,
                despillDetectionWidthPx: 1,
                edgeColor: null));

        var nearPixel = result.FinalRgba.At<Vec4b>(0, 1);
        var farPixel = result.FinalRgba.At<Vec4b>(0, 3);
        Assert.NotEqual(nearPixel, farPixel);
        Assert.Equal((byte)220, farPixel.Item1);
        Assert.Equal((byte)140, farPixel.Item2);
        Assert.Equal((byte)48, farPixel.Item0);
    }

    [Fact]
    public void FinalizeFromPreResize_DespillOnlyOnPartialAlpha_SkipsOpaquePixels()
    {
        using var original = new Mat(1, 2, MatType.CV_8UC3, new Scalar(48, 220, 140));
        using var trimap = new Mat(1, 2, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 2, MatType.CV_8UC1);
        alpha.Set(0, 0, 128);
        alpha.Set(0, 1, 255);
        using var preResize = new PreResizeCutoutResult(
            trimap.Clone(),
            alpha.Clone(),
            original.Clone(),
            new RgbColor(0, 255, 0));

        var partialOnlyParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            maxContourWidthPx: 32,
            edgeColor: null);
        partialOnlyParameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        partialOnlyParameters.DespillDetectionStrength = 0.99d;
        var baselineParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            maxContourWidthPx: 32,
            edgeColor: null);
        baselineParameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        baselineParameters.DespillDetectionStrength = 0.0d;

        using var processor = CreateProcessor();
        using var baseline = processor.FinalizeFromPreResize(preResize, baselineParameters);
        using var result = processor.FinalizeFromPreResize(preResize, partialOnlyParameters);

        var baselinePartialPixel = baseline.FinalRgba.At<Vec4b>(0, 0);
        var baselineOpaquePixel = baseline.FinalRgba.At<Vec4b>(0, 1);
        var nearPixel = result.FinalRgba.At<Vec4b>(0, 0);
        var farPixel = result.FinalRgba.At<Vec4b>(0, 1);

        Assert.NotEqual(baselinePartialPixel, nearPixel);
        Assert.Equal(baselineOpaquePixel.Item1, farPixel.Item1);
    }

    [Fact]
    public void FinalizeFromPreResize_DespillDetectionStrengthLimitsTransparentBandMode()
    {
        using var original = new Mat(1, 2, MatType.CV_8UC3, new Scalar(48, 220, 140));
        using var trimap = new Mat(1, 2, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 2, MatType.CV_8UC1);
        alpha.Set(0, 0, 128);
        alpha.Set(0, 1, 200);
        using var preResize = new PreResizeCutoutResult(
            trimap.Clone(),
            alpha.Clone(),
            original.Clone(),
            new RgbColor(0, 255, 0));

        var parameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            maxContourWidthPx: 32,
            edgeColor: null);
        parameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        parameters.DespillDetectionStrength = 0.6d;
        var baselineParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            maxContourWidthPx: 32,
            edgeColor: null);
        baselineParameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        baselineParameters.DespillDetectionStrength = 0.0d;

        using var processor = CreateProcessor();
        using var baseline = processor.FinalizeFromPreResize(preResize, baselineParameters);
        using var result = processor.FinalizeFromPreResize(preResize, parameters);

        var highAlphaPixel = result.FinalRgba.At<Vec4b>(0, 1);
        var baselineHighAlphaPixel = baseline.FinalRgba.At<Vec4b>(0, 1);

        Assert.Equal(baselineHighAlphaPixel, highAlphaPixel);
    }

    [Fact]
    public void FinalizeFromPreResize_OpaquePixelSkipsSimpleRestoreAndKeepsObservedColor()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(48, 220, 140));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        using var preResize = new PreResizeCutoutResult(
            trimap.Clone(),
            alpha.Clone(),
            original.Clone(),
            new RgbColor(0, 255, 0));

        var parameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            maxContourWidthPx: 0,
            edgeColor: null);
        parameters.Internal.AlphaColorRestore.DespillStrength = 1.0d;
        parameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        parameters.DespillDetectionStrength = 1.0d;

        using var processor = CreateProcessor();
        using var result = processor.FinalizeFromPreResize(preResize, parameters);

        var pixel = result.FinalRgba.At<Vec4b>(0, 0);
        Assert.Equal(48, pixel.Item0);
        Assert.Equal(220, pixel.Item1);
        Assert.Equal(140, pixel.Item2);
        Assert.Equal(255, pixel.Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_F0IsSkippedOutsideColorSpillDetectionTarget()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(100, 170, 100));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(255));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(77));
        using var preResize = new PreResizeCutoutResult(
            trimap.Clone(),
            alpha.Clone(),
            original.Clone(),
            new RgbColor(0, 255, 0));

        var parameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 0.0d,
            maxContourWidthPx: 0,
            edgeColor: null);
        parameters.Internal.AlphaColorRestore.DespillOnlyOnPartialAlpha = true;
        parameters.DespillDetectionStrength = 0.0d;

        using var processor = CreateProcessor();
        using var result = processor.FinalizeFromPreResize(preResize, parameters);

        var pixel = result.FinalRgba.At<Vec4b>(0, 0);
        Assert.Equal(100, pixel.Item0);
        Assert.Equal(170, pixel.Item1);
        Assert.Equal(100, pixel.Item2);
        Assert.Equal(77, pixel.Item3);
    }

    [Fact]
    public void FinalizeFromPreResize_DisablingEdgeColorCorrectionSkipsRepresentativeColorCorrection()
    {
        using var original = new Mat(1, 1, MatType.CV_8UC3, new Scalar(24, 236, 24));
        using var trimap = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(128));
        using var alpha = new Mat(1, 1, MatType.CV_8UC1, Scalar.All(77));
        using var preResize = new PreResizeCutoutResult(trimap.Clone(), alpha.Clone(), original.Clone(), new RgbColor(0, 255, 0));

        using var processor = CreateProcessor();

        var enabledParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 1.0d,
            edgeColor: new RgbColor(0, 0, 0),
            maxContourWidthPx: 32);
        enabledParameters.EnableEdgeColorCorrection = true;

        var disabledParameters = CreateParameters(
            backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
            backgroundColor: new RgbColor(0, 255, 0),
            transparencyCut: 0.0d,
            edgeCorrectionStrength: 1.0d,
            edgeColor: new RgbColor(0, 0, 0),
            maxContourWidthPx: 32);
        disabledParameters.EnableEdgeColorCorrection = false;

        using var enabledResult = processor.FinalizeFromPreResize(preResize, enabledParameters);
        using var disabledResult = processor.FinalizeFromPreResize(preResize, disabledParameters);

        Assert.NotEqual(enabledResult.FinalRgba.At<Vec4b>(0, 0), disabledResult.FinalRgba.At<Vec4b>(0, 0));
    }

    private static byte ClampToByteForTest(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
