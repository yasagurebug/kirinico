using Kirinico.App.Models;

namespace Kirinico.App.Tests;

public sealed class AppSettingsSnapshotTests
{
    [Fact]
    public void UiSettingsSnapshot_FastPreviewEnabled_DefaultsToTrue()
    {
        var snapshot = new AppSettingsSnapshot.UiSettingsSnapshot();

        Assert.True(snapshot.FastPreviewEnabled);
    }
}
