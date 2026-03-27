using Kirinico.App.Services;

namespace Kirinico.App.Tests;

public sealed class PreviewSessionTests
{
    [Fact]
    public void MarkDirty_TrimapRequiresFullPipeline()
    {
        using var session = new PreviewSession();

        session.MarkDirty(PreviewDirtyKind.Trimap);

        Assert.True(session.IsDirty);
        Assert.Equal(PreviewRenderMode.FullPipeline, session.GetNextRenderMode(hasSourceImage: true, hasCachedPreResizeResult: true));
    }

    [Fact]
    public void MarkTrimapRendered_LeavesPresentationRenderPending()
    {
        using var session = new PreviewSession();
        session.MarkPendingFullRender();

        session.MarkTrimapRendered();

        Assert.True(session.IsDirty);
        Assert.Equal(PreviewRenderMode.PresentationOnly, session.GetNextRenderMode(hasSourceImage: true, hasCachedPreResizeResult: true));
    }
}
