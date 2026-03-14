using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;

namespace Kirinico.Core.Tests;

public sealed class CharacterCutoutProcessorTests
{
    private static ResizeOptions DefaultResize() =>
        new()
        {
            Mode = ResizeMode.Scale,
            ScalePercent = 100d,
        };

    private static CutoutParameters CreateParameters(
        BackgroundSpecificationMode backgroundSpecificationMode = BackgroundSpecificationMode.ManualSeed,
        RgbColor? backgroundColor = null,
        double extraction = 0.7d,
        double noiseRemoval = 0.3d,
        int scanWidth = 2,
        LinePolarity linePolarity = LinePolarity.Unspecified) =>
        new()
        {
            BackgroundSpecificationMode = backgroundSpecificationMode,
            BackgroundColor = backgroundColor ?? new RgbColor(255, 255, 255),
            Extraction = extraction,
            NoiseRemoval = noiseRemoval,
            ScanWidth = scanWidth,
            LinePolarity = linePolarity,
            Resize = DefaultResize(),
        };

    private static ManualEditMaps CreateDefaultSeedMaps(Mat image)
    {
        var seedMap = new Mat(image.Rows, image.Cols, MatType.CV_8UC1, Scalar.All(0d));
        seedMap.Set(0, 0, 255);
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

        var processor = new CharacterCutoutProcessor();
        var sampled = processor.SampleBackgroundColor(image, new Point(2, 2), radius: 0);

        Assert.Equal(new RgbColor(120, 110, 100), sampled);
    }

    [Fact]
    public void Process_ProducesOpaqueForegroundAndTransparentBackgroundForSimpleRectangle()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(18, 18, 28, 28), new Scalar(10, 20, 30), -1);

        var processor = new CharacterCutoutProcessor();
        using var seedMaps = CreateDefaultSeedMaps(image);
        using var result = processor.Process(image, CreateParameters(), seedMaps);

        Assert.True(result.AlphaMask.Get<byte>(32, 32) > 200);
        Assert.True(result.AlphaMask.Get<byte>(4, 4) < 32);
    }

    [Fact]
    public void Process_AdditionalBackgroundSeedCanMarkEnclosedHoleAsBackground()
    {
        using var image = new Mat(96, 96, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Circle(image, new Point(48, 48), 28, new Scalar(20, 20, 20), 8);

        using var seedMaps = CreateDefaultSeedMaps(image);
        seedMaps.BackgroundSeedAddMap!.Set(48, 48, 255);

        var processor = new CharacterCutoutProcessor();
        using var result = processor.Process(image, CreateParameters(extraction: 0.2d), seedMaps);

        Assert.True(result.AlphaMask.Get<byte>(48, 48) < 32);
        Assert.True(result.AlphaMask.Get<byte>(48, 20) > 64);
    }

    [Fact]
    public void Process_ThrowsWhenNoBackgroundSeedExists()
    {
        using var image = new Mat(32, 32, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(8, 8, 16, 16), new Scalar(16, 16, 16), -1);
        using var emptySeed = new Mat(32, 32, MatType.CV_8UC1, Scalar.All(0d));

        var processor = new CharacterCutoutProcessor();
        Assert.Throws<InvalidOperationException>(() =>
            processor.Process(
                image,
                CreateParameters(),
                new ManualEditMaps { BackgroundSeedAddMap = emptySeed.Clone() }));
    }

    [Fact]
    public void Process_ColorRangeModeDoesNotRequireBackgroundSeed()
    {
        using var image = new Mat(48, 48, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(12, 12, 24, 24), new Scalar(16, 16, 16), -1);

        var processor = new CharacterCutoutProcessor();
        using var result = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                extraction: 0.2d));

        Assert.True(result.AlphaMask.Get<byte>(24, 24) > 200);
        Assert.True(result.AlphaMask.Get<byte>(4, 4) < 16);
    }

    [Fact]
    public void Process_ColorRangeModeIgnoresManualSeeds()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(18, 18, 28, 28), new Scalar(32, 32, 32), -1);

        using var seedMaps = new ManualEditMaps
        {
            BackgroundSeedAddMap = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(0d)),
        };
        seedMaps.BackgroundSeedAddMap!.Set(32, 32, 255);

        var processor = new CharacterCutoutProcessor();
        using var result = processor.Process(
            image,
            CreateParameters(
                backgroundSpecificationMode: BackgroundSpecificationMode.ColorRange,
                backgroundColor: new RgbColor(255, 255, 255),
                extraction: 0.2d),
            seedMaps);

        Assert.True(result.AlphaMask.Get<byte>(32, 32) > 200);
        Assert.True(result.AlphaMask.Get<byte>(4, 4) < 16);
    }

    [Fact]
    public void Process_HigherExtraction_MonotonicallyReducesBrightBoundaryAlpha()
    {
        using var image = new Mat(100, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(28, 28, 44, 44), new Scalar(228, 228, 228), -1);
        Cv2.Rectangle(image, new Rect(30, 30, 40, 40), new Scalar(18, 24, 30), -1);

        var processor = new CharacterCutoutProcessor();
        using var seedMapsA = CreateDefaultSeedMaps(image);
        using var lowExtraction = processor.Process(image, CreateParameters(extraction: 0d), seedMapsA);

        using var seedMapsB = CreateDefaultSeedMaps(image);
        using var defaultExtraction = processor.Process(image, CreateParameters(extraction: 0.7d), seedMapsB);

        using var seedMapsC = CreateDefaultSeedMaps(image);
        using var maxExtraction = processor.Process(image, CreateParameters(extraction: 1d), seedMapsC);

        var edgeLow = lowExtraction.AlphaMask.Get<byte>(29, 50);
        var edgeDefault = defaultExtraction.AlphaMask.Get<byte>(29, 50);
        var edgeMax = maxExtraction.AlphaMask.Get<byte>(29, 50);

        Assert.True(edgeLow >= edgeDefault, $"Expected extraction to be monotonic. low={edgeLow}, default={edgeDefault}");
        Assert.True(edgeDefault >= edgeMax, $"Expected stronger extraction to remove at least as much boundary alpha. default={edgeDefault}, max={edgeMax}");
        Assert.True(defaultExtraction.AlphaMask.Get<byte>(50, 50) > 200);
    }

    [Fact]
    public void Process_HigherExtraction_IsMonotonicOnGreenBackground()
    {
        using var image = new Mat(100, 100, MatType.CV_8UC3, new Scalar(48, 224, 48));
        Cv2.Rectangle(image, new Rect(28, 28, 44, 44), new Scalar(64, 214, 64), -1);
        Cv2.Rectangle(image, new Rect(30, 30, 40, 40), new Scalar(24, 32, 224), -1);

        var processor = new CharacterCutoutProcessor();
        using var seedMapsA = CreateDefaultSeedMaps(image);
        using var lowExtraction = processor.Process(image, CreateParameters(extraction: 0d), seedMapsA);

        using var seedMapsB = CreateDefaultSeedMaps(image);
        using var defaultExtraction = processor.Process(image, CreateParameters(extraction: 0.7d), seedMapsB);

        using var seedMapsC = CreateDefaultSeedMaps(image);
        using var maxExtraction = processor.Process(image, CreateParameters(extraction: 1d), seedMapsC);

        var edgeLow = lowExtraction.AlphaMask.Get<byte>(29, 50);
        var edgeDefault = defaultExtraction.AlphaMask.Get<byte>(29, 50);
        var edgeMax = maxExtraction.AlphaMask.Get<byte>(29, 50);

        Assert.True(edgeLow >= edgeDefault, $"Expected extraction to be monotonic on green background. low={edgeLow}, default={edgeDefault}");
        Assert.True(edgeDefault >= edgeMax, $"Expected stronger extraction to remove at least as much edge alpha on green background. default={edgeDefault}, max={edgeMax}");
        Assert.True(defaultExtraction.AlphaMask.Get<byte>(50, 50) > 200);
    }

    [Fact]
    public void Process_BlackLinePolarityPreservesDarkOutline()
    {
        using var image = new Mat(96, 96, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(22, 22, 52, 52), new Scalar(24, 24, 24), 3);
        Cv2.Rectangle(image, new Rect(26, 26, 44, 44), new Scalar(210, 160, 120), -1);

        var processor = new CharacterCutoutProcessor();
        using var seedMaps = CreateDefaultSeedMaps(image);
        using var result = processor.Process(image, CreateParameters(scanWidth: 3, linePolarity: LinePolarity.Black), seedMaps);

        Assert.True(result.AlphaMask.Get<byte>(23, 48) > 80);
        Assert.True(result.FinalRgba.At<Vec4b>(23, 48).Item3 > 80);
    }

    [Fact]
    public void Process_FractionalOutlineThicknessExtendsAlphaOutsideForeground()
    {
        using var image = new Mat(64, 64, MatType.CV_8UC3, new Scalar(255, 255, 255));
        Cv2.Rectangle(image, new Rect(20, 20, 24, 24), new Scalar(20, 20, 20), -1);

        var processor = new CharacterCutoutProcessor();
        using var seedMaps = CreateDefaultSeedMaps(image);
        using var result = processor.Process(
            image,
            new CutoutParameters
            {
                Extraction = 0.7d,
                NoiseRemoval = 0.3d,
                ScanWidth = 2,
                LinePolarity = LinePolarity.Unspecified,
                Resize = DefaultResize(),
                Outline = new OutlineOptions
                {
                    Enabled = true,
                    Color = new RgbColor(0, 0, 0),
                    Thickness = 0.5d,
                },
            },
            seedMaps);

        var outlineAlpha = result.FinalRgba.At<Vec4b>(19, 32).Item3;
        Assert.True(outlineAlpha > 0);
    }
}
