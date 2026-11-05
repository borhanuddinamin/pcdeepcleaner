using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using WCM.Application.Interfaces;

namespace WCM.Infrastructure.Logging;

public sealed class LogLevelControl : ILogLevelControl
{
    public LoggingLevelSwitch Switch { get; } = new(LogEventLevel.Information);

    public void Set(string level)
    {
        if (Enum.TryParse<LogEventLevel>(level, out var parsed))
        {
            Switch.MinimumLevel = parsed;
        }
    }
}

public static class LoggingConfiguration
{
    public static Logger CreateLogger(string logsDirectory, LoggingLevelSwitch? level = null)
    {
        Directory.CreateDirectory(logsDirectory);

        return new LoggerConfiguration()
            .MinimumLevel.ControlledBy(level ?? new LoggingLevelSwitch())
            .WriteTo.File(
                new JsonFormatter(),
                Path.Combine(logsDirectory, "wcm-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();
    }
}
