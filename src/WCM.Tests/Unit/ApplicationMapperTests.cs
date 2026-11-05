using WCM.Application.DTOs;
using WCM.Application.Services;
using WCM.Domain.Enums;
using WCM.Domain.Policies;

namespace WCM.Tests.Unit;

public class ApplicationMapperTests
{
    private const string Local = @"C:\Users\me\AppData\Local";

    [Fact]
    public void Installed_app_is_mapped_to_many_locations_with_evidence()
    {
        var app = App("a", "Foo", install: @"C:\Program Files\Foo", publisher: "Acme Inc", exe: @"C:\Program Files\Foo\foo.exe");

        var plan = Map([app], [Pf(@"C:\Program Files\Foo"), Data(@"Acme\Foo")]);

        Assert.Equal(2, plan.Mappings.Count);
        var install = plan.Mappings.Single(m => m.Path.StartsWith(@"C:\Program", StringComparison.Ordinal));
        Assert.Equal(OwnershipState.InstalledProtected, install.State);
        Assert.Contains(install.Evidence, e => e.Type == EvidenceType.InstallPath);
        Assert.Contains(install.Evidence, e => e.Type == EvidenceType.ExecutableMetadata);
        var data = plan.Mappings.Single(m => m.Path.StartsWith(Local, StringComparison.Ordinal));
        Assert.Equal(OwnershipState.InstalledProtected, data.State);
        Assert.Equal(20, data.Score);
        Assert.All(plan.Mappings, m => Assert.NotEmpty(m.Evidence));
    }

    [Fact]
    public void Weak_name_match_alone_stays_unknown()
    {
        var plan = Map([App("a", "Foo")], [Data("Foo")]);

        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(plan.Mappings).State);
        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(plan.Locations).OwnershipStatus);
    }

    [Fact]
    public void Publisher_folder_alone_links_nothing()
    {
        var plan = Map([App("a", "Foo", publisher: "Microsoft"), App("b", "Bar", publisher: "Microsoft")], [Data("Microsoft")]);

        Assert.Empty(plan.Mappings);
        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(plan.Locations).OwnershipStatus);
    }

    [Fact]
    public void Folder_used_by_two_detected_apps_is_shared_and_score_is_reduced()
    {
        var path = @"C:\Program Files\Suite";
        var plan = Map([App("a", "Alpha", install: path), App("b", "Beta", install: path)], [Pf(path)]);

        Assert.Equal(OwnershipState.SharedProtected, Assert.Single(plan.Locations).OwnershipStatus);
        Assert.Equal(2, plan.Mappings.Count);
        Assert.All(plan.Mappings, m =>
        {
            Assert.Equal(OwnershipState.SharedProtected, m.State);
            Assert.Contains(m.Evidence, e => e.Type == EvidenceType.SharedActive);
            Assert.Equal(0, m.Score);
        });
    }

    [Fact]
    public void App_no_longer_detected_does_not_own_or_share_but_stays_unknown()
    {
        var path = @"C:\Program Files\Foo";
        var plan = Map([App("a", "Foo", install: path, status: ApplicationStatus.NotDetected)], [Pf(path)]);

        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(plan.Mappings).State);
        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(plan.Locations).OwnershipStatus);
    }

    [Fact]
    public void Weak_mapping_beside_a_real_owner_gets_conflict_evidence()
    {
        var path = @"C:\Program Files\Foo";
        var plan = Map([App("a", "Owner", install: path), App("b", "Foo")], [Pf(path)]);

        var weak = plan.Mappings.Single(m => m.AppKey == "b");
        Assert.Contains(weak.Evidence, e => e.Type == EvidenceType.Conflict);
        Assert.Equal(OwnershipState.UnknownPreserved, weak.State);
        Assert.Equal(OwnershipState.InstalledProtected, Assert.Single(plan.Locations).OwnershipStatus);
    }

    [Fact]
    public void Signature_with_publisher_is_exact_and_without_publisher_is_known_path()
    {
        var signature = new KnownApplicationSignature("s", "1", "Tool", "Acme", [Local + @"\Tool"]);

        var exact = Map([App("a", "Tool X", publisher: "Acme")], [Data("Tool")], [signature]);
        var known = Map([App("a", "Tool X")], [Data("Tool")], [signature]);

        Assert.Contains(Assert.Single(exact.Mappings).Evidence, e => e.Type == EvidenceType.ExactSignature);
        Assert.Contains(Assert.Single(known.Mappings).Evidence, e => e.Type == EvidenceType.KnownPath);
    }

    [Fact]
    public void Decisions_and_history_are_kept_and_rejection_never_makes_an_owner()
    {
        var path = @"C:\Program Files\Foo";
        var rejected = Map([App("a", "Foo", install: path)], [Pf(path)], prior: [new PriorMapping("a", path, UserDecision.Rejected)]);
        var confirmed = Map([App("a", "Foo")], [Data("Foo")], prior: [new PriorMapping("a", Local + @"\Foo", UserDecision.Confirmed)]);

        Assert.Equal(OwnershipState.UserRejectedMapping, Assert.Single(rejected.Mappings).State);
        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(rejected.Locations).OwnershipStatus);
        Assert.Contains(Assert.Single(rejected.Mappings).Evidence, e => e.Type == EvidenceType.Historical);
        Assert.Equal(OwnershipState.UserConfirmedOwnership, Assert.Single(confirmed.Mappings).State);
        Assert.Equal(OwnershipState.InstalledProtected, Assert.Single(confirmed.Locations).OwnershipStatus);
    }

    [Theory]
    [InlineData("Cache")]
    [InlineData("Programs")]
    public void Generic_folder_names_are_not_evidence(string name) =>
        Assert.Empty(Map([App("a", name)], [Data(name)]).Mappings);

    [Theory]
    [InlineData("7-Zip 26.03 (x64)", "7zip")]
    [InlineData("Foo Bar 1.2.3", "foobar")]
    [InlineData("Tool v2", "tool")]
    public void Versions_are_ignored_in_names(string name, string expected) =>
        Assert.Equal(expected, ApplicationMapper.SignificantName(name));

    [Fact]
    public void No_data_type_is_ever_cache()
    {
        var plan = Map([], [Data(@"Foo\Cache"), new CandidateLocation(@"C:\Users\me\.npm", @"C:\Users\me\.npm", "Development data", 1, 1, true), new CandidateLocation(Local + @"\Temp\x", Local + @"\Temp", "User temporary files", 1, 1, true)]);

        Assert.All(plan.Locations, l => Assert.DoesNotContain("Cache", l.DataType, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parent_of_an_install_folder_is_not_owned_by_its_apps()
    {
        var plan = Map(
            [App("a", "Alpha", install: @"C:\Program Files\Acme\Alpha", exe: @"C:\Program Files\Acme\Alpha\a.exe"), App("b", "Beta", install: @"C:\Program Files\Acme\Beta")],
            [Pf(@"C:\Program Files\Acme")]);

        Assert.Empty(plan.Mappings);
        Assert.Equal(OwnershipState.UnknownPreserved, Assert.Single(plan.Locations).OwnershipStatus);
    }

    private static MappingPlan Map(
        IReadOnlyList<ApplicationRecord> apps,
        IReadOnlyList<CandidateLocation> candidates,
        IReadOnlyList<KnownApplicationSignature>? signatures = null,
        IReadOnlyList<PriorMapping>? prior = null) =>
        ApplicationMapper.Build(apps, candidates, signatures ?? [], prior ?? []);

    private static CandidateLocation Data(string relative) => new(Local + "\\" + relative, Local, "Local application data", 10, 2, true);

    private static CandidateLocation Pf(string path) => new(path, @"C:\Program Files", "Program Files", 10, 2, true);

    private static ApplicationRecord App(string key, string name, string? install = null, string? publisher = null, string? exe = null, string status = ApplicationStatus.Installed) =>
        new(key, name, publisher, null, install, exe, "Registry", status, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
}
