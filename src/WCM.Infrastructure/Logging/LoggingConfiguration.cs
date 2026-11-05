using Serilog;
using Serilog.Core;
using Serilog.Formatting.Json;

namespace WCM.Infrastructure.Logging;

public static class LoggingConfiguration
{
    public static Logger CreateLogger(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                new JsonFormatter(),
                Path.Combine(logsDirectory, "wcm-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }
}
