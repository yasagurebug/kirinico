namespace Kirinico.Core.Models;

public sealed record BatchProgress(
    int CompletedCount,
    int TotalCount,
    string CurrentFileName,
    string StatusText);
