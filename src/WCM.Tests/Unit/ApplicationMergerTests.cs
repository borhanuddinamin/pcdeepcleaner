using WCM.Application.DTOs;
using WCM.Application.Services;
using WCM.Domain.Enums;

namespace WCM.Tests.Unit;

public class ApplicationMergerTests
{
    [Fact]
    public void Shortcut_inside_install_location_merges_and_keeps_both_sources()
    {
        var merged = Assert.Single(ApplicationMerger.Merge(
        [
            App("shortcut:foo", "Foo", "StartMenu", ApplicationStatus.Unverified, location: @"C:\Program Files\Foo\bin", exe: @"C:\Program Files\Foo\bin\foo.exe"),
            App("registry:foo", "Foo 1.2 (x64)", "Registry", ApplicationStatus.Installed, location: @"C:\Program Files\Foo\", publisher: "Foo Inc"),
        ]));

        Assert.Equal("registry:foo", merged.Application.AppKey);
        Assert.Equal(ApplicationStatus.Installed, merged.Application.Status);
        Assert.Equal(@"C:\Program Files\Foo\bin\foo.exe", merged.Application.ExecutablePath);
        Assert.Equal("Registry, StartMenu", merged.Application.DetectionSource);
        Assert.Equal(2, merged.Sources.Count);
    }

    [Fact]
    public void Same_location_with_different_publishers_is_not_merged_and_needs_review()
    {
        var merged = ApplicationMerger.Merge(
        [
            App("registry:a", "Tool", "Registry", ApplicationStatus.Installed, location: @"D:\Shared", publisher: "Alpha"),
            App("registry:b", "Tool", "Registry", ApplicationStatus.Installed, location: @"D:\Shared", publisher: "Beta"),
        ]);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, m =>
        {
            Assert.Equal(ApplicationStatus.NeedsReview, m.Application.Status);
            Assert.NotNull(m.ReviewReason);
        });
    }

    [Fact]
    public void Same_location_with_unrelated_names_is_not_merged()
    {
        var merged = ApplicationMerger.Merge(
        [
            App("registry:a", "Alpha Editor", "Registry", ApplicationStatus.Installed, location: @"D:\Suite"),
            App("registry:b", "Beta Viewer", "Registry", ApplicationStatus.Installed, location: @"D:\Suite"),
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Users\me\AppData\Local\Programs")]
    [InlineData(@"C:\")]
    public void Generic_folders_are_not_evidence(string location)
    {
        var merged = ApplicationMerger.Merge(
        [
            App("registry:a", "Foo", "Registry", ApplicationStatus.Installed, location: location),
            App("registry:b", "Foo Tools", "Registry", ApplicationStatus.Installed, location: location),
        ]);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, m => Assert.Null(m.ReviewReason));
    }

    [Fact]
    public void Same_name_alone_does_not_merge()
    {
        var merged = ApplicationMerger.Merge(
        [
            App("registry:a", "Client", "Registry", ApplicationStatus.Installed),
            App("registry:b", "Client", "Registry", ApplicationStatus.Installed),
        ]);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void Same_name_and_publisher_merges_across_hives()
    {
        var merged = Assert.Single(ApplicationMerger.Merge(
        [
            App("registry:hklm", "Client", "Registry", ApplicationStatus.Installed, publisher: "Acme"),
            App("registry:hkcu", "CLIENT", "Registry", ApplicationStatus.Installed, publisher: "Acme"),
        ]));

        Assert.Equal(2, merged.Sources.Count);
    }

    [Fact]
    public void Shortcut_only_application_stays_unverified()
    {
        var merged = Assert.Single(ApplicationMerger.Merge(
        [
            App("shortcut:a", "Tool", "StartMenu", ApplicationStatus.Unverified, exe: @"D:\Tool\tool.exe"),
        ]));

        Assert.Equal(ApplicationStatus.Unverified, merged.Application.Status);
    }


    [Fact]
    public void Shortcut_to_the_same_executable_merges_despite_different_publisher_text()
    {
        var merged = Assert.Single(ApplicationMerger.Merge(
        [
            App("registry:n", "Editor (64-bit)", "Registry", ApplicationStatus.Installed, exe: @"C:\Program Files\Editor\editor.exe", publisher: "Editor Team"),
            App("shortcut:n", "Editor", "StartMenu", ApplicationStatus.Unverified, location: @"C:\Program Files\Editor", exe: @"C:\Program Files\Editor\editor.exe", publisher: "Some Person"),
        ]));

        Assert.Equal(ApplicationStatus.Installed, merged.Application.Status);
    }

    [Fact]
    public void Shortcut_into_a_folder_from_a_different_publisher_is_not_merged()
    {
        var merged = ApplicationMerger.Merge(
        [
            App("registry:a", "Host", "Registry", ApplicationStatus.Installed, location: @"D:\Host", publisher: "Alpha"),
            App("shortcut:b", "Plugin", "StartMenu", ApplicationStatus.Unverified, location: @"D:\Host\plugin", exe: @"D:\Host\plugin\p.exe", publisher: "Beta"),
        ]);

        Assert.Equal(2, merged.Count);
    }

    private static ApplicationInfo App(
        string key,
        string name,
        string source,
        string status,
        string? location = null,
        string? exe = null,
        string? publisher = null) =>
        new(key, name, publisher, null, location, exe, source, status);
}
