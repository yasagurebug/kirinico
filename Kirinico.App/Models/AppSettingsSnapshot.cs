using Kirinico.Core.Models;

namespace Kirinico.App.Models;

public sealed class AppSettingsSnapshot
{
    public sealed class SeedPointSnapshot
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    public BackgroundSpecificationMode BackgroundSpecificationMode { get; set; } = BackgroundSpecificationMode.ColorRange;

    public string BackgroundColorHex { get; set; } = "FFFFFF";

    public double Extraction { get; set; } = 0.7d;

    public double NoiseRemoval { get; set; } = 0.35d;

    public double ScanWidth { get; set; } = 5d;

    public string LineColorHex { get; set; } = string.Empty;

    public double ScalePercent { get; set; } = 100d;

    public ResizeInterpolationMode ResizeInterpolation { get; set; } = ResizeInterpolationMode.Lanczos4;

    public int OutputWidth { get; set; }

    public int OutputHeight { get; set; }

    public bool OutlineEnabled { get; set; }

    public string OutlineColorHex { get; set; } = "000000";

    public double OutlineThickness { get; set; } = 1d;

    public bool AutoReprocess { get; set; } = true;

    public List<SeedPointSnapshot> BackgroundSeeds { get; set; } = [];
}
