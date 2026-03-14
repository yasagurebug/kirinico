using Kirinico.Core.Models;
using OpenCvSharp;
using System.IO;

namespace Kirinico.Core.Services;

public sealed class BatchImageProcessor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".jfif",
        ".bmp",
        ".webp",
        ".tif",
        ".tiff",
        ".exif",
    };

    private readonly CharacterCutoutProcessor _processor;

    public BatchImageProcessor(CharacterCutoutProcessor processor)
    {
        _processor = processor;
    }

    public async Task<BatchProcessSummary> ProcessAsync(
        IEnumerable<string> inputPaths,
        CutoutParameters parameters,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(parameters);

        var files = inputPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Where(IsSupportedImage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<BatchItemResult>(files.Count);

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var fileName = Path.GetFileName(file);
            progress?.Report(new BatchProgress(index + 1, files.Count, fileName, $"処理中 {index + 1} / {files.Count}"));

            var outputPath = FileNamingService.GetOutputPath(file);
            var backupPath = FileNamingService.GetBackupPath(file);

            if (File.Exists(backupPath))
            {
                results.Add(new BatchItemResult(file, outputPath, false, true, ".bak が既に存在します。"));
                continue;
            }

            try
            {
                await Task.Run(() =>
                {
                    using var source = ImageLoadService.LoadColorImage(file);
                    if (source.Empty())
                    {
                        throw new InvalidOperationException("入力画像を読み込めませんでした。");
                    }

                    File.Copy(file, backupPath);

                    using var seedMap = new Mat(source.Rows, source.Cols, MatType.CV_8UC1, Scalar.All(0d));
                    seedMap.Set(0, 0, 255);
                    using var manualMaps = new ManualEditMaps
                    {
                        BackgroundSeedAddMap = seedMap.Clone(),
                    };
                    using var result = _processor.Process(source, parameters, manualMaps);
                    if (!Cv2.ImWrite(outputPath, result.FinalRgba))
                    {
                        throw new InvalidOperationException("PNG 出力を保存できませんでした。");
                    }
                }, cancellationToken);

                results.Add(new BatchItemResult(file, outputPath, true, false, null));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new BatchItemResult(file, outputPath, false, false, exception.Message));
            }
        }

        progress?.Report(new BatchProgress(files.Count, files.Count, string.Empty, $"完了 {files.Count} / {files.Count}"));
        return new BatchProcessSummary(results);
    }

    private static bool IsSupportedImage(string path) => SupportedExtensions.Contains(Path.GetExtension(path));
}
