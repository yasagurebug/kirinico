using Kirinico.App.Models;
using Kirinico.Core.Models;

namespace Kirinico.App.Services;

internal static class SettingsMapper
{
    public static CutoutParameters BuildCutoutParameters(AppSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return BuildCutoutParameters(snapshot.Ui, snapshot.Internal);
    }

    public static CutoutParameters BuildCutoutParameters(
        AppSettingsSnapshot.UiSettingsSnapshot? ui,
        InternalSettings? internalSettings)
    {
        var value = ui ?? new AppSettingsSnapshot.UiSettingsSnapshot();
        var copiedInternalSettings = InternalSettingsCloner.Clone(internalSettings);
        var outlineColor = ParseRequiredColor(value.OutlineColorHex, new RgbColor(0, 0, 0));

        return new CutoutParameters
        {
            BackgroundSpecificationMode = value.BackgroundSpecificationMode,
            BackgroundColor = ParseRequiredColor(value.BackgroundColorHex, new RgbColor(255, 255, 255)),
            BackgroundTolerance = value.BackgroundTolerance,
            ContourTolerance = value.ContourTolerance,
            DistanceFromBackgroundOnly = value.ContourSettingMethod == ContourSettingMethod.Width,
            MaxContourWidthPx = (int)Math.Round(Math.Clamp(value.MaxContourWidth, 0d, 1d) * 128d),
            DenoiseStrength = value.DenoiseStrength,
            MattingMethod = value.ContourInferenceMethod,
            TransparencyCut = value.TransparencyCut,
            OpaqueAlphaThreshold = ConvertUiOpaqueAlphaThresholdToInternal(value.OpaqueAlphaThreshold),
            DespillExpansionPx = (int)Math.Round(Math.Clamp(value.DespillExpansion, 0d, 1d) * 5d),
            DespillMix = Math.Clamp(value.DespillMix, 0d, 1d),
            DespillExpand = Math.Clamp(copiedInternalSettings.AlphaColorRestore.DespillExpand, 0d, 1d),
            DespillBrightness = ConvertUiBrightnessToInternal(value.DespillBrightness),
            Resize = new ResizeOptions
            {
                Mode = ResizeMode.Scale,
                Interpolation = value.ResizeInterpolation,
                ScalePercent = value.ScalePercent,
                OutputWidth = Math.Max(0, value.OutputWidth),
                OutputHeight = Math.Max(0, value.OutputHeight),
            },
            Outline = new OutlineOptions
            {
                Enabled = value.OutlineEnabled,
                Color = outlineColor,
                Thickness = value.OutlineThickness,
            },
            Internal = copiedInternalSettings,
        };
    }

    private static RgbColor ParseRequiredColor(string? hex, RgbColor fallback)
        => RgbColor.TryParseHex(hex, out var color) ? color : fallback;

    private static double ConvertUiBrightnessToInternal(double uiValue)
        => (Math.Clamp(uiValue, 0d, 1d) * 20d) - 10d;

    private static double ConvertUiOpaqueAlphaThresholdToInternal(double uiValue)
        => 0.8d + (Math.Clamp(uiValue, 0d, 1d) * 0.2d);
}
