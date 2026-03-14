using System.IO;

namespace Kirinico.Core.Services;

public static class FileNamingService
{
    public static string GetBackupPath(string inputPath) => $"{inputPath}.bak";

    public static string GetOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(directory, $"{fileNameWithoutExtension}.png");
    }
}
