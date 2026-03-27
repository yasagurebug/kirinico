using Kirinico.App.Models;
using Kirinico.App.Services;
using OpenCvSharp;

namespace Kirinico.App.Tests;

public sealed class ImageDocumentTests
{
    [Fact]
    public void RestoreSeedSnapshots_IgnoresOutOfRangeSeeds()
    {
        using var document = new ImageDocument();
        document.ReplaceSourceImage(new Mat(4, 4, MatType.CV_8UC3, Scalar.All(0d)));

        document.RestoreSeedSnapshots(
        [
            new AppSettingsSnapshot.SeedPointSnapshot { X = 1, Y = 1 },
            new AppSettingsSnapshot.SeedPointSnapshot { X = 9, Y = 9 },
        ]);

        var snapshots = document.CollectSeedSnapshots();

        Assert.Single(snapshots);
        Assert.Equal(1, snapshots[0].X);
        Assert.Equal(1, snapshots[0].Y);
    }
}
