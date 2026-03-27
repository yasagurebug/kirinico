namespace Kirinico.App.Services;

internal enum PreviewDirtyKind
{
    Trimap,
    Presentation,
}

internal enum PreviewRenderMode
{
    TrimapOnly,
    PresentationOnly,
    FullPipeline,
}

internal readonly record struct PreviewTicket(CancellationToken Token, int RenderVersion);

internal sealed class PreviewSession : IDisposable
{
    private CancellationTokenSource? _renderCts;
    private int _renderVersion;

    public bool IsDirty { get; private set; }

    public bool RequiresCoreRender { get; private set; } = true;

    public bool RequiresPresentationRender { get; private set; } = true;

    public bool IsRendering { get; private set; }

    public PreviewRenderMode? ActiveRenderMode { get; private set; }

    public PreviewTicket BeginRender(PreviewRenderMode mode)
    {
        CancelPending(incrementVersion: false);
        _renderCts = new CancellationTokenSource();
        IsRendering = true;
        ActiveRenderMode = mode;
        switch (mode)
        {
            case PreviewRenderMode.FullPipeline:
                RequiresCoreRender = false;
                RequiresPresentationRender = false;
                break;
            case PreviewRenderMode.PresentationOnly:
                RequiresPresentationRender = false;
                break;
            case PreviewRenderMode.TrimapOnly:
                RequiresCoreRender = false;
                RequiresPresentationRender = false;
                break;
        }

        IsDirty = RequiresCoreRender || RequiresPresentationRender;
        return new PreviewTicket(_renderCts.Token, Interlocked.Increment(ref _renderVersion));
    }

    public void MarkDirty(PreviewDirtyKind kind)
    {
        if (kind == PreviewDirtyKind.Trimap)
        {
            RequiresCoreRender = true;
            RequiresPresentationRender = true;
        }
        else
        {
            RequiresPresentationRender = true;
        }

        IsDirty = true;
    }

    public PreviewRenderMode? GetNextRenderMode(bool hasSourceImage, bool hasCachedPreResizeResult)
    {
        if (!hasSourceImage)
        {
            return null;
        }

        if (RequiresCoreRender)
        {
            return PreviewRenderMode.FullPipeline;
        }

        if (RequiresPresentationRender)
        {
            return hasCachedPreResizeResult ? PreviewRenderMode.PresentationOnly : PreviewRenderMode.FullPipeline;
        }

        return null;
    }

    public void MarkPendingFullRender()
    {
        RequiresCoreRender = true;
        RequiresPresentationRender = true;
        IsDirty = true;
    }

    public void MarkTrimapRendered()
    {
        IsRendering = false;
        ActiveRenderMode = null;
        RequiresCoreRender = false;
        RequiresPresentationRender = true;
        IsDirty = true;
    }

    public void MarkRenderCompleted()
    {
        IsRendering = false;
        ActiveRenderMode = null;
        IsDirty = RequiresCoreRender || RequiresPresentationRender;
    }

    public void MarkRenderInterrupted(PreviewRenderMode mode)
    {
        IsRendering = false;
        ActiveRenderMode = null;
        switch (mode)
        {
            case PreviewRenderMode.FullPipeline:
                RequiresCoreRender = true;
                RequiresPresentationRender = true;
                break;
            case PreviewRenderMode.PresentationOnly:
                RequiresPresentationRender = true;
                break;
            case PreviewRenderMode.TrimapOnly:
                RequiresCoreRender = true;
                RequiresPresentationRender = true;
                break;
        }

        IsDirty = RequiresCoreRender || RequiresPresentationRender;
    }

    public bool IsCurrentVersion(int renderVersion) => renderVersion == Volatile.Read(ref _renderVersion);

    public void InvalidateCurrentRender()
    {
        CancelPending(incrementVersion: true);
    }

    public void Dispose()
    {
        CancelPending(incrementVersion: false);
    }

    private void CancelPending(bool incrementVersion)
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = null;
        IsRendering = false;
        ActiveRenderMode = null;
        if (incrementVersion)
        {
            Interlocked.Increment(ref _renderVersion);
        }
    }
}
