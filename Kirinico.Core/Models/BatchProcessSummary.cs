namespace Kirinico.Core.Models;

public sealed class BatchProcessSummary
{
    public BatchProcessSummary(IReadOnlyList<BatchItemResult> items)
    {
        Items = items;
    }

    public IReadOnlyList<BatchItemResult> Items { get; }

    public int SucceededCount => Items.Count(static item => item.Succeeded);

    public int SkippedCount => Items.Count(static item => item.Skipped);

    public int FailedCount => Items.Count(static item => !item.Succeeded && !item.Skipped);
}
