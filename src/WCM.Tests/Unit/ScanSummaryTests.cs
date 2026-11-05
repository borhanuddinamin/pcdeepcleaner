using WCM.Application.DTOs;
using WCM.Desktop.ViewModels;

namespace WCM.Tests.Unit;

public class ScanSummaryTests
{
    [Theory]
    [InlineData(ScanStatus.CompletedWithErrors, "incomplete")]
    [InlineData(ScanStatus.Cancelled, "incomplete")]
    [InlineData(ScanStatus.Completed, "Scan complete")]
    public void Incomplete_totals_are_never_presented_as_complete(string status, string expected)
    {
        var summary = StorageViewModel.Summarize(new ScanResult(1, status, 5, 1, 2, 1024, 0, 0, 0, null));

        Assert.Contains(expected, summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_scan_with_no_errors_is_complete()
    {
        Assert.True(new ScanResult(1, ScanStatus.Completed, 0, 0, 0, 0, 0, 0, 0, null).IsComplete);
        Assert.False(new ScanResult(1, ScanStatus.CompletedWithErrors, 0, 0, 1, 0, 0, 0, 0, null).IsComplete);
    }
}
