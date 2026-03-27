using Kirinico.Core.Models;
using Kirinico.Core.Services;

namespace Kirinico.App.Services;

internal sealed record BatchCoordinatorUpdate(double ProgressPercent, string StatusText);

internal sealed class BatchCoordinator
{
    private readonly BatchImageProcessor _batchProcessor;

    public BatchCoordinator(BatchImageProcessor batchProcessor)
    {
        _batchProcessor = batchProcessor;
    }

    public async Task<BatchProcessSummary> RunAsync(
        IEnumerable<string> files,
        CutoutParameters parameters,
        Action<BatchCoordinatorUpdate> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(update);

        var progress = new Progress<BatchProgress>(report =>
        {
            var percent = report.TotalCount == 0 ? 0d : report.CompletedCount * 100d / report.TotalCount;
            var statusText = string.IsNullOrWhiteSpace(report.CurrentFileName)
                ? report.StatusText
                : $"{report.StatusText}\n{report.CurrentFileName}";
            update(new BatchCoordinatorUpdate(percent, statusText));
        });

        return await _batchProcessor.ProcessAsync(files, parameters, progress, cancellationToken);
    }
}
