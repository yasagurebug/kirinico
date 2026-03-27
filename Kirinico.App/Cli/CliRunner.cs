using Kirinico.App.Models;
using Kirinico.App.Services;
using Kirinico.Core.Models;
using Kirinico.Core.Services;
using OpenCvSharp;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kirinico.App.Cli;

internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        NativeConsole.TryAttachToParent();

        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            var snapshot = await LoadSettingsAsync(options.SettingsPath);
            using var source = ImageLoadService.LoadColorImage(options.InputPath);
            if (source.Empty())
            {
                throw new InvalidOperationException("入力画像を読み込めませんでした。");
            }

            using var manualMaps = BuildManualEditMaps(snapshot.Ui, source.Size());
            var parameters = SettingsMapper.BuildCutoutParameters(snapshot);

            var outputDirectory = Path.GetDirectoryName(options.OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var workerTempDirectory = Path.Combine(Path.GetTempPath(), "Kirinico", "CliWorker");
            var workerOptions = new PythonWorkerOptions
            {
                WorkerExecutablePath = Path.Combine(AppContext.BaseDirectory, "python_worker", "Kirinico.PyWorker.exe"),
                WorkingDirectory = AppContext.BaseDirectory,
                TempDirectory = workerTempDirectory,
            };

            Console.WriteLine($"Input: {options.InputPath}");
            Console.WriteLine($"Settings: {options.SettingsPath}");
            Console.WriteLine($"Output: {options.OutputPath}");

            using var estimator = new PythonWorkerAlphaMatteEstimator(workerOptions);
            using var processor = new CharacterCutoutProcessor(estimator);
            using var result = await Task.Run(() => processor.Process(source, parameters, manualMaps));

            if (!Cv2.ImWrite(options.OutputPath, result.FinalRgba))
            {
                throw new InvalidOperationException("出力画像を保存できませんでした。");
            }

            Console.WriteLine("Done");
            return 0;
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            WriteUsage();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            PythonWorkerTempDirectoryCleaner.TryDelete(Path.Combine(Path.GetTempPath(), "Kirinico", "CliWorker"));
        }
    }

    private static async Task<AppSettingsSnapshot> LoadSettingsAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("設定 JSON が見つかりません。", path);
        }

        var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
        return JsonSerializer.Deserialize<AppSettingsSnapshot>(json, CreateJsonOptions())
            ?? throw new InvalidOperationException("設定 JSON を解析できませんでした。");
    }

    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private static ManualEditMaps? BuildManualEditMaps(AppSettingsSnapshot.UiSettingsSnapshot? ui, OpenCvSharp.Size sourceSize)
    {
        if (ui is null || ui.BackgroundSpecificationMode != BackgroundSpecificationMode.ManualSeed)
        {
            return null;
        }

        var seedMap = new Mat(sourceSize.Height, sourceSize.Width, MatType.CV_8UC1, Scalar.All(0d));
        if (ui.BackgroundSeeds is not null)
        {
            foreach (var seed in ui.BackgroundSeeds)
            {
                if (seed.X < 0 || seed.Y < 0 || seed.X >= sourceSize.Width || seed.Y >= sourceSize.Height)
                {
                    continue;
                }

                seedMap.Set(seed.Y, seed.X, 255);
            }
        }

        return new ManualEditMaps
        {
            BackgroundSeedAddMap = seedMap,
        };
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Kirinico.App.exe --input <image> --settings <settings.json> --output <output.png>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -i, --input       Input image path");
        Console.WriteLine("  -s, --settings    Settings JSON path");
        Console.WriteLine("  -o, --output      Output PNG path");
        Console.WriteLine("  -h, --help        Show help");
    }

    private sealed class CliOptions
    {
        public bool ShowHelp { get; private init; }

        public string InputPath { get; private init; } = string.Empty;

        public string SettingsPath { get; private init; } = string.Empty;

        public string OutputPath { get; private init; } = string.Empty;

        public static CliOptions Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                throw new CliUsageException("CLI mode requires arguments.");
            }

            string? input = null;
            string? settings = null;
            string? output = null;
            var showHelp = false;

            for (var index = 0; index < args.Count; index++)
            {
                var arg = args[index];
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        showHelp = true;
                        break;
                    case "-i":
                    case "--input":
                        input = ReadValue(args, ref index, arg);
                        break;
                    case "-s":
                    case "--settings":
                        settings = ReadValue(args, ref index, arg);
                        break;
                    case "-o":
                    case "--output":
                        output = ReadValue(args, ref index, arg);
                        break;
                    default:
                        throw new CliUsageException($"Unknown argument: {arg}");
                }
            }

            if (showHelp)
            {
                return new CliOptions { ShowHelp = true };
            }

            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(settings) || string.IsNullOrWhiteSpace(output))
            {
                throw new CliUsageException("Missing required arguments.");
            }

            return new CliOptions
            {
                InputPath = Path.GetFullPath(input),
                SettingsPath = Path.GetFullPath(settings),
                OutputPath = Path.GetFullPath(output),
            };
        }

        private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
        {
            if (index + 1 >= args.Count)
            {
                throw new CliUsageException($"Missing value for {option}");
            }

            index++;
            return args[index];
        }
    }

    private sealed class CliUsageException : Exception
    {
        public CliUsageException(string message)
            : base(message)
        {
        }
    }

    private static class NativeConsole
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        public static void TryAttachToParent()
        {
            if (AttachConsole(AttachParentProcess))
            {
                return;
            }

            AllocConsole();
        }
    }
}
