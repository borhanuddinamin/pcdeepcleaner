using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Desktop.ViewModels;
using Xunit;

namespace WCM.Tests.Integration;

public sealed class CacheSelectionTests
{
    private static CacheCandidate Candidate(string path, string category, bool eligible) =>
        new("rule", "app", category, "data", path, 1024, 1, null, false, "Low.", string.Empty, true, eligible, "reason");

    private static async Task<CacheViewModel> PageAsync()
    {
        var page = new CacheViewModel(
            new StubAnalyzer([
                Candidate(@"C:\a", "Temporary files", true),
                Candidate(@"C:\locked", "Temporary files", false),
                Candidate(@"C:\b", "Build caches", true),
            ]),
            new CleanupBasket());
        await page.RefreshAsync();
        return page;
    }

    [Fact]
    public async Task Refresh_selects_nothing()
    {
        var page = await PageAsync();

        Assert.DoesNotContain(page.Items, row => row.IsSelected);
    }

    [Fact]
    public async Task Select_all_skips_locked_rows()
    {
        var page = await PageAsync();

        page.SelectAllCommand.Execute(null);

        Assert.False(page.Items.Single(row => row.Candidate.Path == @"C:\locked").IsSelected);
        Assert.StartsWith("2 selected", page.SelectionSummary);
    }

    [Fact]
    public async Task Select_all_covers_only_the_current_filter()
    {
        var page = await PageAsync();
        page.Filter = "Build caches";

        page.SelectAllCommand.Execute(null);

        Assert.StartsWith("1 selected", page.SelectionSummary);
        page.Filter = "All";
        Assert.False(page.Items.Single(row => row.Candidate.Path == @"C:\a").IsSelected);
    }

    [Fact]
    public async Task Clear_removes_selection_hidden_by_the_filter()
    {
        var page = await PageAsync();
        page.SelectAllCommand.Execute(null);
        page.Filter = "Build caches";

        page.ClearSelectionCommand.Execute(null);

        Assert.StartsWith("0 selected", page.SelectionSummary);
    }

    private sealed class StubAnalyzer(IReadOnlyList<CacheCandidate> items) : ICacheAnalyzer
    {
        public Task<IReadOnlyList<CacheCandidate>> AnalyzeAsync(CancellationToken cancellationToken) => Task.FromResult(items);
    }
}
