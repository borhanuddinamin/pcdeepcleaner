using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WCM.Application.Interfaces;
using WCM.Application.Services;
using WCM.CleanupEngine.Cache;
using WCM.CleanupEngine.Orphans;
using WCM.CleanupEngine.Planning;
using WCM.CleanupEngine.Quarantine;
using WCM.CleanupEngine.Validation;
using WCM.Desktop.Services;
using WCM.Desktop.ViewModels;
using WCM.Domain.Policies;
using WCM.Infrastructure.Discovery;
using WCM.Infrastructure.FileSystem;
using WCM.Infrastructure.Logging;
using WCM.Infrastructure.SQLite;
using WCM.Infrastructure.Windows;

namespace WCM.Desktop.Composition;

/// <summary>The only place in Desktop allowed to know about Infrastructure and CleanupEngine types.</summary>
public static class AppHost
{
    private static readonly string[] DevelopmentFolders =
        [".nuget", ".npm", ".gradle", ".docker", ".vscode", ".cursor", ".claude", ".config", ".cache", ".local", ".m2", ".cargo", ".rustup", ".git"];

    public static IHost Build(IDataDirectoryService? directories = null, IDialogService? dialogs = null)
    {
        var dataDirectories = directories ?? DataDirectoryService.CreateDefault();
        var logLevel = new LogLevelControl();
        var logger = LoggingConfiguration.CreateLogger(dataDirectories.LogsDirectory, logLevel.Switch);

        // DisableDefaults: no config files, environment providers or console logging.
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var services = builder.Services;
        builder.Logging.AddSerilog(logger, dispose: true);
        services.AddSingleton(dataDirectories);
        services.AddSingleton<ILogLevelControl>(logLevel);
        services.AddSingleton(new SqliteConnectionFactory(dataDirectories.DatabasePath));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IApplicationRepository, ApplicationRepository>();
        services.AddSingleton<IScanRepository, ScanRepository>();
        services.AddSingleton<IAuditRepository, AuditRepository>();
        services.AddSingleton<IMappingRepository, MappingRepository>();
        services.AddSingleton<ICleanupRepository, CleanupRepository>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IDashboardQuery, DashboardQuery>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IPathNormalizer, PathNormalizerService>();
        services.AddSingleton<ICandidateLocationReader, CandidateLocationReader>();
        services.AddSingleton<ISignatureCatalog>(SignatureCatalog.CreateDefault());
        services.AddSingleton<IRuleCatalog>(provider => new RuleCatalog(RuleCatalog.DefaultPath, provider.GetRequiredService<ISettingsStore>()));
        services.AddSingleton<IOwnershipService, OwnershipService>();
        foreach (var provider in DiscoveryProviders.CreateDefault())
        {
            services.AddSingleton(provider);
        }

        services.AddSingleton<IApplicationDiscoveryService, ApplicationDiscoveryService>();
        services.AddSingleton<IStorageScanner, StorageScanner>();
        services.AddSingleton<IScanConfigurationStore>(provider => new ScanConfigurationStore(
            provider.GetRequiredService<SqliteConnectionFactory>(),
            ScanDefaults.Create(dataDirectories.DataRoot)));
        services.AddSingleton<UserProtectedPaths>();
        services.AddSingleton(provider => CreatePolicy(dataDirectories.DataRoot, provider.GetRequiredService<UserProtectedPaths>()));
        services.AddSingleton<IProcessInspector, RunningProcessDetector>();
        services.AddSingleton<CleanupValidator>();
        services.AddSingleton(new OrphanOptions());
        services.AddSingleton<IOrphanService, OrphanService>();
        services.AddSingleton<ICacheAnalyzer, CacheAnalyzer>();
        services.AddSingleton<ICleanupPlanner, CleanupPlanner>();
        services.AddSingleton<IQuarantineService, QuarantineService>();

        services.AddSingleton(dialogs ?? new DialogService());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<CleanupBasket>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ApplicationsViewModel>();
        services.AddSingleton<OrphansViewModel>();
        services.AddSingleton<CacheViewModel>();
        services.AddSingleton<StorageViewModel>();
        services.AddSingleton<CleanupCenterViewModel>();
        services.AddSingleton<QuarantineViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return builder.Build();
    }

    private static ProtectedPathPolicy CreatePolicy(string dataRoot, UserProtectedPaths userPaths)
    {
        static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
        var profile = Folder(Environment.SpecialFolder.UserProfile);
        string[] roots =
        [
            Folder(Environment.SpecialFolder.Windows),
            Folder(Environment.SpecialFolder.ProgramFiles),
            Folder(Environment.SpecialFolder.ProgramFilesX86),
            Folder(Environment.SpecialFolder.CommonApplicationData),
            Folder(Environment.SpecialFolder.MyDocuments),
            Folder(Environment.SpecialFolder.DesktopDirectory),
            Folder(Environment.SpecialFolder.MyPictures),
            Folder(Environment.SpecialFolder.MyVideos),
            Folder(Environment.SpecialFolder.MyMusic),
            System.IO.Path.Combine(profile, "Downloads"),
            dataRoot,
        ];
        string[] exact =
        [
            profile,
            Folder(Environment.SpecialFolder.LocalApplicationData),
            Folder(Environment.SpecialFolder.ApplicationData),
            System.IO.Path.Combine(profile, "AppData", "LocalLow"),
            PathNormalizer.Normalize(System.IO.Path.GetTempPath()),
            .. DevelopmentFolders.Select(folder => System.IO.Path.Combine(profile, folder)),
        ];
        return new ProtectedPathPolicy(roots.Where(path => path.Length > 0).ToList(), exact, [], () => userPaths.Current);
    }
}
