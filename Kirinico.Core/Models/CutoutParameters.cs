namespace Kirinico.Core.Models;

public sealed class CutoutParameters
{
    public BackgroundSpecificationMode BackgroundSpecificationMode { get; set; } = BackgroundSpecificationMode.ColorRange;

    public RgbColor BackgroundColor { get; set; } = new(255, 255, 255);

    public double BackgroundTolerance { get; set; } = 0.5d;

    public double ContourTolerance { get; set; } = 0.4d;

    public int MaxContourWidthPx { get; set; } = 32;

    public double DenoiseStrength { get; set; } = 0.3d;

    public MattingMethod MattingMethod { get; set; } = MattingMethod.Cf;

    public double TransparencyCut { get; set; } = 0.15d;

    public double DespillDetectionStrength { get; set; } = 1.0d;

    public int DespillDetectionWidthPx { get; set; } = 3;

    public double EdgeCorrectionStrength { get; set; } = 0.5d;

    public bool EnableEdgeColorCorrection { get; set; } = true;

    public RgbColor? EdgeRepresentativeColor { get; set; }

    public ResizeOptions Resize { get; set; } = new();

    public OutlineOptions Outline { get; set; } = new();

    public InternalSettings Internal { get; set; } = new();
}
