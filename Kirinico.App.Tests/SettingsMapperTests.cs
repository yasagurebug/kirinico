using Kirinico.App.Models;
using Kirinico.App.Services;
using Kirinico.Core.Models;

namespace Kirinico.App.Tests;

public sealed class SettingsMapperTests
{
    [Fact]
    public void BuildCutoutParameters_MapsUiSettingsIntoCoreParameters()
    {
        var ui = new AppSettingsSnapshot.UiSettingsSnapshot
        {
            BackgroundColorHex = "AABBCC",
            OutlineColorHex = "102030",
            BackgroundTolerance = 0.25d,
            ContourTolerance = 0.75d,
            MaxContourWidth = 0.5d,
            DenoiseStrength = 0.4d,
            ContourInferenceMethod = MattingMethod.Knn,
            TransparencyCut = 0.3d,
            OpaqueAlphaThreshold = 1d,
            DespillExpansion = 1d,
            DespillMix = 1.5d,
            DespillBrightness = 0d,
            ResizeInterpolation = ResizeInterpolationMode.Cubic,
            ScalePercent = 80d,
            OutputWidth = 640,
            OutputHeight = 480,
            OutlineEnabled = true,
            OutlineThickness = 3d,
        };
        var internalSettings = new InternalSettings();
        internalSettings.AlphaColorRestore.DespillExpand = 2d;

        var parameters = SettingsMapper.BuildCutoutParameters(ui, internalSettings);

        Assert.Equal(new RgbColor(170, 187, 204), parameters.BackgroundColor);
        Assert.Equal(new RgbColor(16, 32, 48), parameters.Outline.Color);
        Assert.Equal(64, parameters.MaxContourWidthPx);
        Assert.Equal(1d, parameters.DespillMix);
        Assert.Equal(5, parameters.DespillExpansionPx);
        Assert.Equal(1d, parameters.DespillExpand);
        Assert.Equal(-10d, parameters.DespillBrightness);
        Assert.Equal(1.0d, parameters.OpaqueAlphaThreshold);
        Assert.Equal(ResizeInterpolationMode.Cubic, parameters.Resize.Interpolation);
        Assert.Equal(3d, parameters.Outline.Thickness);
    }

    [Fact]
    public void BuildCutoutParameters_UsesFallbackColorsForInvalidHex()
    {
        var ui = new AppSettingsSnapshot.UiSettingsSnapshot
        {
            BackgroundColorHex = "bad",
            OutlineColorHex = "nope",
        };

        var parameters = SettingsMapper.BuildCutoutParameters(ui, new InternalSettings());

        Assert.Equal(new RgbColor(255, 255, 255), parameters.BackgroundColor);
        Assert.Equal(new RgbColor(0, 0, 0), parameters.Outline.Color);
    }
}
