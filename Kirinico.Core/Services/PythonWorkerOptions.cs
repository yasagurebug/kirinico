using System.IO;

namespace Kirinico.Core.Services;

public sealed class PythonWorkerOptions
{
    public string WorkerExecutablePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "python_worker", "Kirinico.PyWorker.exe");

    public string WorkingDirectory { get; set; } = AppContext.BaseDirectory;

    public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "kirinico", "worker");
}
