using WCM.Application.Interfaces;
using WCM.Desktop.ViewModels;

namespace WCM.Tests.Unit;

public class NavigationTests
{
    [Fact]
    public void Shell_lists_the_nine_specified_screens_in_order()
    {
        var titles = new MainWindowViewModel(new SettingsViewModel(new FakeDirectories())).Pages.Select(p => p.Title);

        Assert.Equal(
            ["Dashboard", "Applications", "Orphaned Data", "Cache Analyzer", "Storage Explorer",
             "Cleanup Center", "Quarantine & Restore", "Scan History", "Settings"],
            titles);
    }

    [Theory]
    [InlineData(true, "Settings")]
    [InlineData(false, "Dashboard")]
    public void Opens_settings_only_on_first_run(bool firstRun, string expected)
    {
        var shell = new MainWindowViewModel(new SettingsViewModel(new FakeDirectories { IsFirstRun = firstRun }));

        Assert.Equal(expected, shell.SelectedPage.Title);
    }

    [Fact]
    public void Save_errors_are_shown_to_the_user()
    {
        var settings = new SettingsViewModel(new FakeDirectories { SaveError = new ArgumentException("Bad path") });

        settings.SaveCommand.Execute(null);

        Assert.Equal("Bad path", settings.Status);
    }

    private sealed class FakeDirectories : IDataDirectoryService
    {
        public Exception? SaveError { get; init; }

        public bool IsPortable => false;

        public bool IsFirstRun { get; init; }

        public string DataRoot => @"C:\Data";

        public string LogsDirectory => @"C:\Data\Logs";

        public string DatabasePath => @"C:\Data\Data\cleanup.db";

        public void SaveDataRoot(string path)
        {
            if (SaveError is not null)
            {
                throw SaveError;
            }
        }
    }
}
