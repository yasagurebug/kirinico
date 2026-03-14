namespace Kirinico.Core.Models;

public sealed record BatchItemResult(
    string InputPath,
    string OutputPath,
    bool Succeeded,
    bool Skipped,
    string? ErrorMessage);
