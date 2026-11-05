using Microsoft.Extensions.DependencyInjection;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Application.Services;
using WCM.CleanupEngine.Quarantine;
using WCM.Desktop.Composition;
using WCM.Desktop.Services;
using WCM.Desktop.ViewModels;
using WCM.Domain.Policies;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Integration;

public sealed class AppCompositionTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly Microsoft.Extensions.Hosting.IHost _host;

    public AppCompositionTests()
    {
        _host = AppHost.Build(new Directories(_fixture.Root), _dialogs);
        _host.Services.GetRequiredService<IDatabaseInitializer>().Initialize();
    }

    public void Dispose()
    {
        _host.Dispose();
        _fixture.Dispose();
    }

    [Fact]
    public void Real_composition_builds_the_nine_specified_screens_in_order()
    {
        var shell = _host.Services.GetRequiredService<MainWindowViewModel>();

        Assert.Equal(
            ["Dashboard", "Applications", "Orphaned Data", "Cache Analyzer", "Storage Explorer",
             "Cleanup Center", "Quarantine & Restore", "Scan History", "Settings"],
            shell.Pages.Select(page => page.Title));
        Assert.All(shell.Pages, page => Assert.False(string.IsNullOrEmpty(page.Icon)));
    }

    [Fact]
    public async Task Every_screen_loads_on_an_empty_database_without_a_problem()
    {
        foreach (var page in _host.Services.GetRequiredService<MainWindowViewModel>().Pages.Where(page => page is not ApplicationsViewModel))
        {
            await page.RefreshAsync();
            Assert.True(page.Problem.Length == 0, $"{page.Title}: {page.Problem}");
        }
    }

    [Fact]
    public async Task Settings_persist_across_a_new_read()
    {
        var store = _host.Services.GetRequiredService<ISettingsStore>();
        await store.SetAsync("RetentionDays", "45", CancellationToken.None);
        var settings = _host.Services.GetRequiredService<SettingsViewModel>();

        await settings.RefreshAsync();

        Assert.Equal(45, settings.RetentionDays);
        Assert.NotEmpty(settings.Roots);
        Assert.NotEmpty(settings.Rules);
        Assert.All(settings.Rules, rule => Assert.False(rule.Enabled));
    }

    [Fact]
    public async Task User_protected_path_applies_immediately()
    {
        var policy = _host.Services.GetRequiredService<ProtectedPathPolicy>();
        Assert.Null(policy.Check(@"Z:\Mine\x"));

        await _host.Services.GetRequiredService<UserProtectedPaths>().SaveAsync([@"Z:\Mine"], CancellationToken.None);

        Assert.NotNull(policy.Check(@"Z:\Mine\x"));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files\App")]
    public void Built_in_locations_are_protected(string path) =>
        Assert.NotNull(_host.Services.GetRequiredService<ProtectedPathPolicy>().Check(path));

    [Fact]
    public async Task Delete_button_does_nothing_unless_the_typed_confirmation_succeeds()
    {
        var quarantine = new FakeQuarantine();
        var page = new QuarantineViewModel(quarantine, _dialogs, _host.Services.GetRequiredService<ISettingsStore>());
        await page.RefreshAsync();
        page.Items[0].IsSelected = true;

        _dialogs.TypedResult = false;
        await page.DeleteAsync();
        Assert.Equal(0, quarantine.DeleteCalls);

        _dialogs.TypedResult = true;
        await page.DeleteAsync();
        Assert.Equal(1, quarantine.DeleteCalls);
        Assert.Equal(QuarantineService.DeleteConfirmation, quarantine.LastConfirmation);
    }

    [Fact]
    public void Summaries_never_present_incomplete_totals_as_complete()
    {
        Assert.Contains("incomplete", StorageViewModel.Summarize(new ScanResult(1, ScanStatus.CompletedWithErrors, 5, 1, 2, 1024, 0, 0, 0, null)), StringComparison.Ordinal);
        Assert.Contains("incomplete", StorageViewModel.Summarize(new ScanResult(1, ScanStatus.Cancelled, 5, 1, 0, 1024, 0, 0, 0, null)), StringComparison.Ordinal);
        Assert.Contains("Scan complete", StorageViewModel.Summarize(new ScanResult(1, ScanStatus.Completed, 5, 1, 0, 1024, 0, 0, 0, null)), StringComparison.Ordinal);
    }

    private sealed class Directories(string root) : IDataDirectoryService
    {
        public bool IsPortable => false;

        public bool IsFirstRun => false;

        public string DataRoot => root;

        public string LogsDirectory => Path.Combine(root, "Logs");

        public string DatabasePath => Path.Combine(root, "cleanup.db");

        public void SaveDataRoot(string path)
        {
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool TypedResult { get; set; }

        public bool Confirm(string title, string message, string confirmText) => true;

        public bool ConfirmByTyping(string title, string message, string requiredText) => TypedResult;

        public string? PickFolder() => null;

        public string? PickSaveFile(string defaultName, string filter) => null;
    }

    private sealed class FakeQuarantine : IQuarantineService
    {
        public int DeleteCalls { get; private set; }

        public string? LastConfirmation { get; private set; }

        public Task<OperationResult> ExecuteAsync(long planId, IProgress<string>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<QuarantineItemView>> GetItemsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QuarantineItemView>>([new QuarantineItemView { Id = 1, Status = ItemState.Quarantined }]);

        public Task<OperationResult> RestoreAsync(IReadOnlyList<long> itemIds, string conflictMode, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult> DeletePermanentlyAsync(IReadOnlyList<long> itemIds, string confirmation, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            LastConfirmation = confirmation;
            return Task.FromResult(new OperationResult(null, []));
        }

        public Task<OperationResult> RecoverAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
