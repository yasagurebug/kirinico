using Kirinico.Core.Models;

namespace Kirinico.App.Models;

public sealed class AppSettingsSnapshot
{
    public sealed class SeedPointSnapshot
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    public sealed class UiSettingsSnapshot
    {
        public BackgroundSpecificationMode BackgroundSpecificationMode { get; set; } = BackgroundSpecificationMode.ColorRange;

        public string BackgroundColorHex { get; set; } = "FFFFFF";

        public double BackgroundTolerance { get; set; } = 0.5d;

        public ContourSettingMethod ContourSettingMethod { get; set; } = ContourSettingMethod.Width;

        public double ContourTolerance { get; set; } = 0.4d;

        public double MaxContourWidth { get; set; } = 0.1d;

        public double DenoiseStrength { get; set; } = 0.3d;

        public MattingMethod ContourInferenceMethod { get; set; } = MattingMethod.Cf;

        public List<SeedPointSnapshot> BackgroundSeeds { get; set; } = [];

        public double TransparencyCut { get; set; } = 0.15d;

        public DespillDetectionMethod DespillDetectionMethod { get; set; } = DespillDetectionMethod.AlphaBand;

        public double DespillDetectionStrength { get; set; } = 0.6d;

        public double DespillDetectionWidth { get; set; } = 0.3d;

        public double DespillStrength { get; set; } = 1.0d;

        public bool EnableEdgeColorCorrection { get; set; } = true;

        public double EdgeCorrectionStrength { get; set; } = 0.5d;

        public string? EdgeRepresentativeColorHex { get; set; }

        public bool AutoReprocess { get; set; } = true;

        public ResizeInterpolationMode ResizeInterpolation { get; set; } = ResizeInterpolationMode.Lanczos4;

        public double ScalePercent { get; set; } = 100d;

        public int OutputWidth { get; set; }

        public int OutputHeight { get; set; }

        public bool OutlineEnabled { get; set; }

        public string OutlineColorHex { get; set; } = "000000";

        public double OutlineThickness { get; set; } = 1d;
    }

    public UiSettingsSnapshot Ui { get; set; } = new();

    public InternalSettings Internal { get; set; } = new();
}
