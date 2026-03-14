using OpenCvSharp;

namespace Kirinico.Core.Models;

public sealed class ManualEditMaps : IDisposable
{
    public Mat? BackgroundSeedAddMap { get; init; }

    public void Dispose()
    {
        BackgroundSeedAddMap?.Dispose();
    }
}
