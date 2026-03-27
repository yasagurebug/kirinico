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

    [Fact]
    public void PresentationDirtyDuringFullPipeline_QueuesPresentationOnlyAfterCompletion()
    {
        using var session = new PreviewSession();
        session.MarkPendingFullRender();

        session.BeginRender(PreviewRenderMode.FullPipeline);
        session.MarkDirty(PreviewDirtyKind.Presentation);
        session.MarkRenderCompleted();

        Assert.True(session.IsDirty);
        Assert.False(session.RequiresCoreRender);
        Assert.True(session.RequiresPresentationRender);
        Assert.Equal(PreviewRenderMode.PresentationOnly, session.GetNextRenderMode(hasSourceImage: true, hasCachedPreResizeResult: true));
    }
}
