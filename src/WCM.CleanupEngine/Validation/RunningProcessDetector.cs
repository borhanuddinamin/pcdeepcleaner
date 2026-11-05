using System.ComponentModel;
using System.Diagnostics;
using WCM.Application.Interfaces;
using WCM.Domain.Policies;

namespace WCM.CleanupEngine.Validation;

public sealed class RunningProcessDetector : IProcessInspector
{
    public bool IsRunning(string processName)
    {
        var running = Process.GetProcessesByName(processName);
        foreach (var process in running)
        {
            process.Dispose();
        }

        return running.Length > 0;
    }

    public string? FindProcessInside(string path)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    if (executable is not null && ProtectedPathPolicy.IsSameOrInside(executable, path))
                    {
                        return process.ProcessName;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Elevated and system processes can't be inspected.
                }
            }
        }

        return null;
    }
}
