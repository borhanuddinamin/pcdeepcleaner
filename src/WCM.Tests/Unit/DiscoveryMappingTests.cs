using WCM.Domain.Enums;
using WCM.Infrastructure.Discovery;

namespace WCM.Tests.Unit;

public class DiscoveryMappingTests
{
    [Theory]
    [InlineData(null, false, false)]
    [InlineData("  ", false, false)]
    [InlineData("App", true, false)]
    [InlineData("App", false, true)]
    public void Hidden_or_nameless_entries_are_skipped(string? name, bool system, bool parent) =>
        Assert.Null(UninstallEntryMapper.Map(Entry(name, null, null, system, parent), _ => true, _ => true));

    [Fact]
    public void Missing_install_folder_is_flagged()
    {
        var app = UninstallEntryMapper.Map(Entry("App", @"D:\Gone", null), _ => false, _ => true);

        Assert.Equal(ApplicationStatus.InstallPathMissing, app!.Status);
    }

    [Fact]
    public void Entry_without_paths_is_installed_and_key_uses_identity_not_name()
    {
        var app = UninstallEntryMapper.Map(Entry("App", null, null), _ => false, _ => false);

        Assert.Equal(ApplicationStatus.Installed, app!.Status);
        Assert.Equal("registry:hklm:x64:{abc}", app.AppKey);
    }

    [Theory]
    [InlineData(@"""C:\App\app.exe"",0", @"C:\App\app.exe")]
    [InlineData(@"C:\App\app.exe", @"C:\App\app.exe")]
    [InlineData(@"C:\App\app.ico", null)]
    [InlineData(@"app.exe", null)]
    [InlineData(null, null)]
    public void Display_icon_is_parsed(string? icon, string? expected) =>
        Assert.Equal(expected, UninstallEntryMapper.ParseExecutable(icon));

    [Fact]
    public void Package_identity_parses_and_resource_packages_are_skipped()
    {
        var identity = PackageIdentity.Parse("Contoso.App_1.2.3.0_x64__8wekyb3d8bbwe");

        Assert.Equal("Contoso.App", identity!.Name);
        Assert.Equal("1.2.3.0", identity.Version);
        Assert.Equal("x64", identity.Architecture);
        Assert.Equal("8wekyb3d8bbwe", identity.PublisherId);
        Assert.Null(PackageIdentity.Parse("Contoso.App_1.2.3.0_neutral_split.scale-100_8wekyb3d8bbwe"));
        Assert.Null(PackageIdentity.Parse("not-a-package"));
    }

    private static UninstallEntry Entry(string? name, string? location, string? icon, bool system = false, bool parent = false) =>
        new("hklm", "x64", "{ABC}", name, null, null, location, icon, system, parent);
}
