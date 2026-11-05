using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WCM.Application.Exceptions;
using WCM.Application.Interfaces;
using WCM.Application.Services;
using WCM.Desktop.Composition;
using WCM.Desktop.Services;

namespace WCM.Desktop;

// Fully qualified: a bare "Application" here resolves to the WCM.Application namespace.
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = AppHost.Build();
            _host.Start();
            _host.Services.GetRequiredService<IDatabaseInitializer>().Initialize();
            await _host.Services.GetRequiredService<UserProtectedPaths>().LoadAsync(CancellationToken.None);
            var settings = _host.Services.GetRequiredService<ISettingsStore>();
            _host.Services.GetRequiredService<ThemeService>().Apply(await settings.GetAsync("Theme", CancellationToken.None) ?? ThemeService.System);
            var level = await settings.GetAsync("LogLevel", CancellationToken.None);
            if (level is not null)
            {
                _host.Services.GetRequiredService<ILogLevelControl>().Set(level);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DatabaseStateException)
        {
            MessageBox.Show(
                $"Windows Cleanup Manager couldn't start. No files were changed.\n\n{ex.Message}",
                "Windows Cleanup Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _logger = _host.Services.GetRequiredService<ILogger<App>>();
        DispatcherUnhandledException += (_, args) =>
        {
            _logger.LogCritical(args.Exception, "Unhandled UI exception");
            MessageBox.Show($"Something went wrong: {args.Exception.Message}", "Windows Cleanup Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        _logger.LogInformation("Application started");

        MainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Application stopped");
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        base.OnExit(e);
    }
}
