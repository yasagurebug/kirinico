using System.IO;

namespace Kirinico.Core.Services;

public static class PythonWorkerTempDirectoryCleaner
{
    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
