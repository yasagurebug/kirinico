namespace Kirinico.Core.Models;

public sealed class CutoutParameters
{
    public BackgroundSpecificationMode BackgroundSpecificationMode { get; set; } = BackgroundSpecificationMode.ColorRange;

    public RgbColor BackgroundColor { get; set; } = new(255, 255, 255);

    public double BackgroundTolerance { get; set; } = 0.5d;

    public double ContourTolerance { get; set; } = 0.4d;

    public bool DistanceFromBackgroundOnly { get; set; }

    public int MaxContourWidthPx { get; set; } = 32;

    public double DenoiseStrength { get; set; } = 0.3d;

    public MattingMethod MattingMethod { get; set; } = MattingMethod.Cf;

    public double TransparencyCut { get; set; } = 0.15d;

    public double OpaqueAlphaThreshold { get; set; } = 0.95d;

    public int DespillExpansionPx { get; set; } = 1;

    public double DespillMix { get; set; } = 0.5d;

    public double DespillExpand { get; set; }

    public double DespillBrightness { get; set; }

    public ResizeOptions Resize { get; set; } = new();

    public OutlineOptions Outline { get; set; } = new();

    public InternalSettings Internal { get; set; } = new();
}
