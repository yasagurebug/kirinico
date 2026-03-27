using Kirinico.Core.Models;

namespace Kirinico.App.Services;

internal static class InternalSettingsCloner
{
    public static InternalSettings Clone(InternalSettings? source)
    {
        var settings = new InternalSettings();
        CopyTo(settings, source);
        return settings;
    }

    public static void CopyTo(InternalSettings target, InternalSettings? source)
    {
        ArgumentNullException.ThrowIfNull(target);

        var value = source ?? new InternalSettings();

        target.Matting.Cf.MaxIters = value.Matting.Cf.MaxIters;
        target.Matting.Cf.Tolerance = value.Matting.Cf.Tolerance;
        target.Matting.Cf.Preconditioner = value.Matting.Cf.Preconditioner;
        target.Matting.Cf.DiscardThreshold = value.Matting.Cf.DiscardThreshold;
        target.Matting.Cf.Shift = value.Matting.Cf.Shift;
        target.Matting.Cf.Epsilon = value.Matting.Cf.Epsilon;
        target.Matting.Cf.Radius = value.Matting.Cf.Radius;

        target.Matting.Knn.MaxIters = value.Matting.Knn.MaxIters;
        target.Matting.Knn.Tolerance = value.Matting.Knn.Tolerance;
        target.Matting.Knn.Preconditioner = value.Matting.Knn.Preconditioner;
        target.Matting.Knn.DiscardThreshold = value.Matting.Knn.DiscardThreshold;
        target.Matting.Knn.Shift = value.Matting.Knn.Shift;
        target.Matting.Knn.Neighbors1 = value.Matting.Knn.Neighbors1;
        target.Matting.Knn.Neighbors2 = value.Matting.Knn.Neighbors2;
        target.Matting.Knn.DistanceWeight1 = value.Matting.Knn.DistanceWeight1;
        target.Matting.Knn.DistanceWeight2 = value.Matting.Knn.DistanceWeight2;
        target.Matting.Knn.Kernel = value.Matting.Knn.Kernel;

        target.Matting.Lkm.MaxIters = value.Matting.Lkm.MaxIters;
        target.Matting.Lkm.Tolerance = value.Matting.Lkm.Tolerance;
        target.Matting.Lkm.Epsilon = value.Matting.Lkm.Epsilon;
        target.Matting.Lkm.Radius = value.Matting.Lkm.Radius;

        target.BackgroundThreshold.TbgMin = value.BackgroundThreshold.TbgMin;
        target.BackgroundThreshold.TbgMax = value.BackgroundThreshold.TbgMax;
        target.BackgroundThreshold.TfgDeltaMin = value.BackgroundThreshold.TfgDeltaMin;
        target.BackgroundThreshold.TfgDeltaMax = value.BackgroundThreshold.TfgDeltaMax;
        target.BackgroundThreshold.BgNoiseMinArea = value.BackgroundThreshold.BgNoiseMinArea;
        target.BackgroundThreshold.BgNoiseMaxHoleArea = value.BackgroundThreshold.BgNoiseMaxHoleArea;

        target.Preprocess.DenoiseRadiusMin = value.Preprocess.DenoiseRadiusMin;
        target.Preprocess.DenoiseRadiusMax = value.Preprocess.DenoiseRadiusMax;
        target.Preprocess.DenoiseSigmaMin = value.Preprocess.DenoiseSigmaMin;
        target.Preprocess.DenoiseSigmaMax = value.Preprocess.DenoiseSigmaMax;

        target.AlphaColorRestore.AlphaCutMin = value.AlphaColorRestore.AlphaCutMin;
        target.AlphaColorRestore.AlphaCutMax = value.AlphaColorRestore.AlphaCutMax;
        target.AlphaColorRestore.DespillExpand = value.AlphaColorRestore.DespillExpand;
    }
}
