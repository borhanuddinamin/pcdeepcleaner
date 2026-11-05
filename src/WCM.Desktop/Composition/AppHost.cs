using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WCM.Application.Interfaces;
using WCM.Desktop.ViewModels;
using WCM.Infrastructure.Logging;
using WCM.Infrastructure.Windows;

namespace WCM.Desktop.Composition;

/// <summary>The only place in Desktop allowed to know about Infrastructure types.</summary>
public static class AppHost
{
    public static IHost Build()
    {
        var dataDirectories = DataDirectoryService.CreateDefault();
        var logger = LoggingConfiguration.CreateLogger(dataDirectories.LogsDirectory);

        // DisableDefaults: no config files, environment providers or console logging.
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddSerilog(logger, dispose: true);
        builder.Services.AddSingleton<IDataDirectoryService>(dataDirectories);
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
