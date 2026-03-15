namespace Kirinico.Core.Models;

public sealed class CutoutParameters
{
    public BackgroundSpecificationMode BackgroundSpecificationMode { get; set; } = BackgroundSpecificationMode.ColorRange;

    public RgbColor BackgroundColor { get; set; } = new(255, 255, 255);

    public double Extraction { get; set; } = 0.7d;

    public double NoiseRemoval { get; set; } = 0.35d;

    public int ScanWidth { get; set; } = 5;

    public RgbColor? LineColor { get; set; }

    public ResizeOptions Resize { get; set; } = new();

    public OutlineOptions Outline { get; set; } = new();
}
