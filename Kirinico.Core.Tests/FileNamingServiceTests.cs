using Kirinico.Core.Services;

namespace Kirinico.Core.Tests;

public sealed class FileNamingServiceTests
{
    [Fact]
    public void GetBackupPath_AppendsBakSuffix()
    {
        Assert.Equal(@"C:\images\sample.jpg.bak", FileNamingService.GetBackupPath(@"C:\images\sample.jpg"));
    }

    [Fact]
    public void GetOutputPath_ReplacesExtensionWithPng()
    {
        Assert.Equal(@"C:\images\sample.png", FileNamingService.GetOutputPath(@"C:\images\sample.jpg"));
    }
}
