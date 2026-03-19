namespace Kirinico.Core.Models;

public sealed class InternalSettings
{
    public MattingSettings Matting { get; set; } = new();

    public BackgroundThresholdSettings BackgroundThreshold { get; set; } = new();

    public PreprocessSettings Preprocess { get; set; } = new();

    public AlphaColorRestoreSettings AlphaColorRestore { get; set; } = new();
}

public sealed class MattingSettings
{
    public MattingMethod Method { get; set; } = MattingMethod.Cf;

    public CfMattingSettings Cf { get; set; } = new();

    public KnnMattingSettings Knn { get; set; } = new();

    public LkmMattingSettings Lkm { get; set; } = new();
}

public enum MattingMethod
{
    Cf,
    Knn,
    Lkm,
}

public sealed class CfMattingSettings
{
    public int MaxIters { get; set; } = 2000;

    public double Tolerance { get; set; } = 1e-7d;

    public string Preconditioner { get; set; } = "ichol";

    public double DiscardThreshold { get; set; } = 1e-5d;

    public double Shift { get; set; } = 1e-6d;

    public double Epsilon { get; set; } = 1e-7d;

    public int Radius { get; set; } = 1;
}

public sealed class KnnMattingSettings
{
    public int MaxIters { get; set; } = 2000;

    public double Tolerance { get; set; } = 1e-7d;

    public string Preconditioner { get; set; } = "jacobi";

    public double DiscardThreshold { get; set; } = 1e-5d;

    public double Shift { get; set; } = 1e-6d;

    public int Neighbors1 { get; set; } = 20;

    public int Neighbors2 { get; set; } = 10;

    public double DistanceWeight1 { get; set; } = 2.0d;

    public double DistanceWeight2 { get; set; } = 0.1d;

    public string Kernel { get; set; } = "binary";
}

public sealed class LkmMattingSettings
{
    public int MaxIters { get; set; } = 2000;

    public double Tolerance { get; set; } = 1e-7d;

    public double Epsilon { get; set; } = 1e-7d;

    public int Radius { get; set; } = 10;
}

public sealed class BackgroundThresholdSettings
{
    public double TbgMin { get; set; } = 2d;

    public double TbgMax { get; set; } = 64d;

    public double TfgDeltaMin { get; set; } = 0d;

    public double TfgDeltaMax { get; set; } = 441d;

    public int BgNoiseMinArea { get; set; } = 4;

    public int BgNoiseMaxHoleArea { get; set; } = 4;
}

public sealed class PreprocessSettings
{
    public int DenoiseRadiusMin { get; set; } = 1;

    public int DenoiseRadiusMax { get; set; } = 5;

    public double DenoiseSigmaMin { get; set; } = 0.2d;

    public double DenoiseSigmaMax { get; set; } = 1.2d;
}

public sealed class AlphaColorRestoreSettings
{
    public double AlphaCutMin { get; set; } = 0.01d;

    public double AlphaCutMax { get; set; } = 0.99d;

    public double MidAlphaUpperMin { get; set; } = 0.45d;

    public double MidAlphaUpperMax { get; set; } = 0.75d;

    public double EdgeConstraintMin { get; set; } = 0d;

    public double EdgeConstraintMax { get; set; } = 1d;

    public double DespillStrength { get; set; } = 1d;

    public double RestoreEpsilon { get; set; } = 0.02d;

    public bool UseEdgeColorOnlyIfProvided { get; set; } = true;
}
