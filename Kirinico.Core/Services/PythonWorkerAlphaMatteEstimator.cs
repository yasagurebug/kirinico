using Kirinico.Core.Models;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kirinico.Core.Services;

public sealed class PythonWorkerAlphaMatteEstimator : IAlphaMatteEstimator
{
    private readonly PythonWorkerOptions _options;
    private readonly object _processSyncRoot = new();
    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };
    private readonly ConcurrentQueue<string> _stderrLines = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private bool _requestInFlight;
    private int _cancellationGeneration;

    public PythonWorkerAlphaMatteEstimator(PythonWorkerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Mat? EstimateAlpha(Mat referenceBgr, Mat trimapMask, MattingMethod method, MattingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(referenceBgr);
        ArgumentNullException.ThrowIfNull(trimapMask);
        ArgumentNullException.ThrowIfNull(settings);

        _requestSemaphore.Wait();
        try
        {
            StreamWriter stdin;
            StreamReader stdout;
            int requestGeneration;

            lock (_processSyncRoot)
            {
                EnsureStarted();
                stdin = _stdin ?? throw new InvalidOperationException("worker の stdin が初期化されていません。");
                stdout = _stdout ?? throw new InvalidOperationException("worker の stdout が初期化されていません。");
                _requestInFlight = true;
                requestGeneration = _cancellationGeneration;
            }

            Directory.CreateDirectory(_options.TempDirectory);
            var requestId = Guid.NewGuid().ToString("N");
            var requestDirectory = Path.Combine(_options.TempDirectory, requestId);
            Directory.CreateDirectory(requestDirectory);

            var imagePath = Path.Combine(requestDirectory, "image.png");
            var trimapPath = Path.Combine(requestDirectory, "trimap.png");
            var outputAlphaPath = Path.Combine(requestDirectory, "alpha.png");

            try
            {
                Cv2.ImWrite(imagePath, referenceBgr);
                Cv2.ImWrite(trimapPath, trimapMask);

                var request = new EstimateAlphaRequest
                {
                    Id = requestId,
                    Command = "estimate_alpha",
                    ImagePath = imagePath,
                    TrimapPath = trimapPath,
                    OutputAlphaPath = outputAlphaPath,
                    Method = method,
                    Settings = settings,
                };

                var requestJson = JsonSerializer.Serialize(request, _jsonOptions);
                try
                {
                    stdin.WriteLine(requestJson);
                    stdin.Flush();
                }
                catch (Exception) when (WasRequestCanceled(requestGeneration))
                {
                    return null;
                }

                string? responseLine;
                try
                {
                    responseLine = stdout.ReadLine();
                }
                catch (Exception) when (WasRequestCanceled(requestGeneration))
                {
                    return null;
                }

                if (responseLine is null && WasRequestCanceled(requestGeneration))
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    throw BuildWorkerException("worker から応答がありません。");
                }

                EstimateAlphaResponse response;
                try
                {
                    response = JsonSerializer.Deserialize<EstimateAlphaResponse>(responseLine, _jsonOptions)
                        ?? throw BuildWorkerException("worker 応答を解析できません。");
                }
                catch (JsonException)
                {
                    throw BuildWorkerException($"worker 応答が JSON ではありません: {responseLine}");
                }

                if (!response.Ok)
                {
                    throw BuildWorkerException(response.Error ?? "worker が alpha 推定に失敗しました。");
                }

                if (!File.Exists(outputAlphaPath))
                {
                    throw BuildWorkerException("worker が alpha 出力を生成しませんでした。");
                }

                var alpha = Cv2.ImRead(outputAlphaPath, ImreadModes.Grayscale);
                if (alpha.Empty())
                {
                    alpha.Dispose();
                    throw BuildWorkerException("worker の alpha 出力を読み込めませんでした。");
                }

                return alpha;
            }
            finally
            {
                lock (_processSyncRoot)
                {
                    _requestInFlight = false;
                }

                TryDeleteDirectory(requestDirectory);
            }
        }
        finally
        {
            _requestSemaphore.Release();
        }
    }

    public void CancelCurrentRequest()
    {
        Process? processToKill = null;

        lock (_processSyncRoot)
        {
            if (!_requestInFlight || _process is null)
            {
                return;
            }

            _cancellationGeneration++;
            _stdin = null;
            _stdout = null;
            processToKill = _process;
            _process = null;
        }

        try
        {
            if (!processToKill.HasExited)
            {
                processToKill.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        finally
        {
            processToKill.Dispose();
        }
    }

    public void Dispose()
    {
        Process? processToKill = null;
        StreamWriter? stdinToDispose = null;
        StreamReader? stdoutToDispose = null;

        lock (_processSyncRoot)
        {
            stdinToDispose = _stdin;
            stdoutToDispose = _stdout;
            processToKill = _process;
            _stdin = null;
            _stdout = null;
            _process = null;
            _requestInFlight = false;
        }

        try
        {
            stdinToDispose?.Dispose();
            stdoutToDispose?.Dispose();
        }
        finally
        {
            if (processToKill is not null)
            {
                try
                {
                    if (!processToKill.HasExited)
                    {
                        processToKill.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                processToKill.Dispose();
            }
        }
    }

    private void EnsureStarted()
    {
        lock (_processSyncRoot)
        {
            if (_process is { HasExited: false } && _stdin is not null && _stdout is not null)
            {
                return;
            }

            if (!File.Exists(_options.WorkerExecutablePath))
            {
                throw new FileNotFoundException("Python worker 実行ファイルが見つかりません。", _options.WorkerExecutablePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.WorkerExecutablePath,
                WorkingDirectory = _options.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.ErrorDataReceived += OnProcessErrorDataReceived;
            process.Start();
            process.BeginErrorReadLine();

            _process = process;
            _stdin = process.StandardInput;
            _stdout = process.StandardOutput;
        }
    }

    private bool WasRequestCanceled(int requestGeneration)
    {
        lock (_processSyncRoot)
        {
            return _cancellationGeneration != requestGeneration;
        }
    }

    private void OnProcessErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        _stderrLines.Enqueue(e.Data);
        while (_stderrLines.Count > 20)
        {
            _stderrLines.TryDequeue(out _);
        }
    }

    private InvalidOperationException BuildWorkerException(string message)
    {
        var stderr = _stderrLines.ToArray();
        return stderr.Length == 0
            ? new InvalidOperationException(message)
            : new InvalidOperationException($"{message} stderr: {string.Join(" | ", stderr)}");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class EstimateAlphaRequest
    {
        public string Id { get; set; } = string.Empty;

        public string Command { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;

        public string TrimapPath { get; set; } = string.Empty;

        public string OutputAlphaPath { get; set; } = string.Empty;

        public MattingMethod Method { get; set; }

        public MattingSettings Settings { get; set; } = new();
    }

    private sealed class EstimateAlphaResponse
    {
        public string Id { get; set; } = string.Empty;

        public bool Ok { get; set; }

        public string? Error { get; set; }
    }
}
