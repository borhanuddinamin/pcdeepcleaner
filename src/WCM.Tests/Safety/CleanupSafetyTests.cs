using System.Diagnostics;
using Dapper;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.CleanupEngine.Cache;
using WCM.CleanupEngine.Orphans;
using WCM.CleanupEngine.Planning;
using WCM.CleanupEngine.Quarantine;
using WCM.CleanupEngine.Validation;
using WCM.Domain.Enums;
using WCM.Domain.Policies;
using WCM.Infrastructure.FileSystem;
using WCM.Infrastructure.SQLite;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Safety;

/// <summary>The plan's required safety table. Every destructive step runs on synthetic temp files only.</summary>
public sealed class CleanupSafetyTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly string _root;
    private readonly SqliteConnectionFactory _connections;
    private readonly CleanupRepository _repository;
    private readonly AuditRepository _audit;
    private readonly FakeRules _rules = new();
    private readonly FakeProcesses _processes = new();
    private readonly CleanupValidator _validator;
    private readonly CleanupPlanner _planner;
    private readonly QuarantineService _quarantine;
    private readonly FixedDirectories _directories;
    private readonly ProtectedPathPolicy _policy;

    public CleanupSafetyTests()
    {
        _root = Path.Combine(PathNormalizer.Normalize(_fixture.Root), "root");
        Directory.CreateDirectory(_root);
        var data = Path.Combine(PathNormalizer.Normalize(_fixture.Root), "data");
        _connections = new SqliteConnectionFactory(Path.Combine(data, "cleanup.db"));
        new DatabaseInitializer(_connections).Initialize();
        _repository = new CleanupRepository(_connections);
        _audit = new AuditRepository(_connections);
        _directories = new FixedDirectories(data);
        _policy = new ProtectedPathPolicy([Path.Combine(_root, "Documents")], [Path.Combine(_root, ".nuget")], []);
        _validator = new CleanupValidator(new FixedSettings(_root), _rules, _repository, new PathNormalizerService(), _processes, _policy);
        _planner = new CleanupPlanner(_validator, _repository, _audit, _directories);
        _quarantine = new QuarantineService(_repository, _audit, _validator, _directories);
    }

    public void Dispose() => _fixture.Dispose();

    private string Rel(string relative) => Path.Combine(_root, relative);

    private string AddFile(string relative, string content = "synthetic") => _fixture.AddFile(Path.Combine("root", relative), content);

    private async Task ScanAsync() => await new StorageScanner(new ScanRepository(_connections))
        .ScanAsync(new ScanConfiguration([new ScanRoot(_root, "Local application data", "t")], [], 2), null, CancellationToken.None);

    private CleanupSelection Cache(string path) => new(path, SourceKind.Cache, null, FakeRules.CacheRule(_root).RuleId);

    private async Task<PlanItem> PlanSingleAsync(CleanupSelection selection)
    {
        _rules.Root = _root;
        var preview = await _planner.CreatePlanAsync([selection], CancellationToken.None);
        return Assert.Single(preview.Items);
    }

    private async Task<(CleanupPreview Preview, string Source)> ApprovedCachePlanAsync(string relative = @"App\Cache\a.bin", string content = "cache-bytes")
    {
        _rules.Root = _root;
        AddFile(relative, content);
        await ScanAsync();
        var target = Rel(@"App\Cache");
        var preview = await _planner.CreatePlanAsync([Cache(target)], CancellationToken.None);
        Assert.True(preview.Items[0].IsValid, preview.Items[0].BlockReason);
        Assert.True(await _planner.ApproveAsync(preview.Plan.Id, CancellationToken.None));
        return (preview, target);
    }

    private async Task MapAsync(string appStatus, string state, string relative)
    {
        using var connection = _connections.Create();
        await connection.ExecuteAsync(
            """
            INSERT INTO Applications (AppKey, Name, DetectionSource, Status, FirstDetectedAt, LastDetectedAt) VALUES ('a' || @Path, 'App', 'Registry', @appStatus, 't', 't');
            INSERT INTO StorageLocations (Path, DataType, OwnershipStatus) VALUES (@Path, 'ApplicationData', @state);
            INSERT INTO ApplicationStorageMappings (ApplicationId, StorageLocationId, ConfidenceScore, OwnershipStatus)
            VALUES ((SELECT MAX(Id) FROM Applications), (SELECT MAX(Id) FROM StorageLocations), 100, @state);
            """,
            new { Path = Rel(relative), appStatus, state });
    }

    private async Task AddOrphanAsync(string relative, string decision)
    {
        using var connection = _connections.Create();
        await connection.ExecuteAsync(
            "INSERT INTO OrphanCandidates (Path, Classification, Decision, Evidence) VALUES (@Path, @c, @decision, 'e')",
            new { Path = Rel(relative), c = OrphanClassification.Potential, decision });
    }

    private async Task<string> AllBlockReasonsForOrphanAsync(string relative)
    {
        var item = await PlanSingleAsync(new CleanupSelection(Rel(relative), SourceKind.Orphan, null, FakeRules.OrphanRule.RuleId));
        Assert.False(item.IsValid);
        return item.BlockReason!;
    }

    [Fact]
    public async Task Installed_app_folder_with_top_score_is_blocked()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        AddFile(@"Foo\a.txt");
        await ScanAsync();
        await MapAsync(ApplicationStatus.Installed, OwnershipState.InstalledProtected, "Foo");
        await AddOrphanAsync("Foo", CandidateDecision.Approved);

        Assert.Contains("still detected", await AllBlockReasonsForOrphanAsync("Foo"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Child_of_an_installed_folder_is_blocked()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        AddFile(@"Foo\Sub\a.txt");
        await ScanAsync();
        await MapAsync(ApplicationStatus.Installed, OwnershipState.InstalledProtected, "Foo");
        await AddOrphanAsync(@"Foo\Sub", CandidateDecision.Approved);

        Assert.Contains("still detected", await AllBlockReasonsForOrphanAsync(@"Foo\Sub"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parent_of_an_installed_folder_is_blocked()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        AddFile(@"Pub\Foo\a.txt");
        await ScanAsync();
        await MapAsync(ApplicationStatus.Installed, OwnershipState.InstalledProtected, @"Pub\Foo");
        await AddOrphanAsync("Pub", CandidateDecision.Approved);

        Assert.Contains("still detected", await AllBlockReasonsForOrphanAsync("Pub"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_folder_is_blocked_for_cache_rules()
    {
        _rules.Root = _root;
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        AddFile(@"App\Cache\a.bin");
        await ScanAsync();
        await MapAsync(ApplicationStatus.Installed, OwnershipState.SharedProtected, "App");

        var item = await PlanSingleAsync(Cache(Rel(@"App\Cache")));

        Assert.Contains("More than one", item.BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unapproved_orphan_and_disabled_rule_are_each_blocked()
    {
        AddFile(@"Old\a.txt");
        await ScanAsync();
        await AddOrphanAsync("Old", CandidateDecision.Pending);
        Assert.Contains("No cleanup rule", await AllBlockReasonsForOrphanAsync("Old"), StringComparison.Ordinal);

        _rules.Enable(FakeRules.OrphanRule.RuleId);
        Assert.Contains("haven't approved", await AllBlockReasonsForOrphanAsync("Old"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"Cand\.git\config", "Contains")]
    [InlineData(@"Cand\Default\Login Data", "sensitive")]
    [InlineData(@"Cand\store.sqlite", "database")]
    [InlineData(@"Cand\app.db", "database")]
    [InlineData(@"Cand\proj\App.csproj", "source")]
    [InlineData(@"Cand\data.vhdx", "database")]
    public async Task Sensitive_content_blocks_the_whole_item(string file, string expected)
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        AddFile(file);
        await ScanAsync();
        await AddOrphanAsync("Cand", CandidateDecision.Approved);

        Assert.Contains(expected, await AllBlockReasonsForOrphanAsync("Cand"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"Chrome\User Data\Default")]
    [InlineData(@"Docker Desktop\vm")]
    [InlineData(@"x\volumes\y")]
    [InlineData(@"Documents\report")]
    [InlineData(".nuget")]
    public async Task Protected_paths_are_blocked(string relative)
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        AddFile(relative + @"\a.txt");
        await ScanAsync();
        await AddOrphanAsync(relative, CandidateDecision.Approved);

        var reason = await AllBlockReasonsForOrphanAsync(relative);

        Assert.True(reason.Contains("protected", StringComparison.OrdinalIgnoreCase) || reason.Contains("whole tool", StringComparison.Ordinal), reason);
    }

    [Fact]
    public async Task Junction_inside_an_item_blocks_it_and_target_survives()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        var outside = _fixture.AddFile(@"outside\keep.txt");
        AddFile(@"Cand\a.txt");
        var link = Rel(@"Cand\Link");
        await Cmd($"""/c mklink /J "{link}" "{Path.GetDirectoryName(outside)}" """);
        try
        {
            await ScanAsync();
            await AddOrphanAsync("Cand", CandidateDecision.Approved);

            Assert.Contains("link", await AllBlockReasonsForOrphanAsync("Cand"), StringComparison.Ordinal);
            Assert.True(File.Exists(outside));
        }
        finally
        {
            await Cmd($"""/c rmdir "{link}" """);
        }
    }

    [Fact]
    public async Task Path_through_a_junction_is_blocked()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        _fixture.AddFile(@"outside\deep\keep.txt");
        var link = Rel("Link");
        await Cmd($"""/c mklink /J "{link}" "{Path.Combine(PathNormalizer.Normalize(_fixture.Root), "outside")}" """);
        try
        {
            await ScanAsync();
            await AddOrphanAsync(@"Link\deep", CandidateDecision.Approved);

            Assert.Contains("link", await AllBlockReasonsForOrphanAsync(@"Link\deep"), StringComparison.Ordinal);
        }
        finally
        {
            await Cmd($"""/c rmdir "{link}" """);
        }
    }

    [Fact]
    public async Task Locked_file_is_blocked()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        var file = AddFile(@"Cand\a.txt");
        await ScanAsync();
        await AddOrphanAsync("Cand", CandidateDecision.Approved);
        using var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Assert.Contains("In use", await AllBlockReasonsForOrphanAsync("Cand"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Required_process_running_blocks_cache_cleanup()
    {
        _rules.Root = _root;
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        _processes.Running = "app";
        AddFile(@"App\Cache\a.bin");
        await ScanAsync();

        Assert.Contains("must be closed", (await PlanSingleAsync(Cache(Rel(@"App\Cache")))).BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_newer_than_the_minimum_age_is_blocked()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        _rules.OrphanMinimumAge = 30;
        AddFile(@"Cand\a.txt");
        await ScanAsync();
        await AddOrphanAsync("Cand", CandidateDecision.Approved);

        Assert.Contains("changed within", await AllBlockReasonsForOrphanAsync("Cand"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Path_outside_approved_roots_is_blocked()
    {
        _rules.Root = _root;
        var outside = _fixture.AddFile(@"elsewhere\a.txt");

        Assert.Contains("outside", (await PlanSingleAsync(Cache(Path.GetDirectoryName(outside)!))).BlockReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dry_run_changes_no_files_and_needs_approval()
    {
        _rules.Root = _root;
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var file = AddFile(@"App\Cache\a.bin");
        await ScanAsync();
        var before = File.GetLastWriteTimeUtc(file);

        var preview = await _planner.CreatePlanAsync([Cache(Rel(@"App\Cache"))], CancellationToken.None);

        Assert.True(File.Exists(file));
        Assert.Equal(before, File.GetLastWriteTimeUtc(file));
        Assert.False(Directory.Exists(preview.QuarantineRoot));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Plan_with_only_blocked_items_cannot_be_approved()
    {
        AddFile(@"Old\a.txt");
        await ScanAsync();
        await AddOrphanAsync("Old", CandidateDecision.Pending);

        var preview = await _planner.CreatePlanAsync([new CleanupSelection(Rel("Old"), SourceKind.Orphan, null, FakeRules.OrphanRule.RuleId)], CancellationToken.None);

        Assert.False(await _planner.ApproveAsync(preview.Plan.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Quarantine_and_restore_round_trip_exactly()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();

        var moved = await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.Equal(ItemState.Quarantined, Assert.Single(moved.Items).Status);
        Assert.False(Directory.Exists(source));
        var item = Assert.Single(await _quarantine.GetItemsAsync(CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(item.QuarantinePath, "a.bin")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(item.QuarantinePath))!, "manifest.json")));
        Assert.NotNull(item.VerificationHash);

        var restored = await _quarantine.RestoreAsync([item.Id], RestoreConflictMode.Stop, CancellationToken.None);

        Assert.Equal(ItemState.Restored, Assert.Single(restored.Items).Status);
        Assert.Equal("cache-bytes", File.ReadAllText(Path.Combine(source, "a.bin")));
        Assert.Contains(await _audit.GetRecentAsync(20, CancellationToken.None), entry => entry.Action == "Restore");
    }

    [Fact]
    public async Task Approved_plan_cannot_run_twice()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, _) = await ApprovedCachePlanAsync();
        await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None));
    }

    [Fact]
    public async Task Stale_plan_is_rejected_and_files_stay()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        using (var connection = _connections.Create())
        {
            await connection.ExecuteAsync("UPDATE CleanupPlans SET ApprovedAt = '2020-01-01T00:00:00.0000000Z'");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None));

        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task Change_after_preview_blocks_the_move()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        File.WriteAllText(Path.Combine(source, "new.bin"), "added later");

        var result = await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.Contains("changed after", Assert.Single(result.Items).Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(source, "new.bin")));
    }

    [Fact]
    public async Task App_installed_after_preview_blocks_the_orphan_move()
    {
        _rules.Enable(FakeRules.OrphanRule.RuleId);
        var file = AddFile(@"Old\a.txt");
        await ScanAsync();
        await AddOrphanAsync("Old", CandidateDecision.Approved);
        var preview = await _planner.CreatePlanAsync([new CleanupSelection(Rel("Old"), SourceKind.Orphan, null, FakeRules.OrphanRule.RuleId)], CancellationToken.None);
        Assert.True(await _planner.ApproveAsync(preview.Plan.Id, CancellationToken.None));
        await MapAsync(ApplicationStatus.Installed, OwnershipState.InstalledProtected, "Old");

        var result = await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.Equal(ItemState.Failed, Assert.Single(result.Items).Status);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Rule_disabled_after_preview_blocks_the_move()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        await _rules.SetEnabledAsync(FakeRules.CacheRule(_root).RuleId, false, CancellationToken.None);

        var result = await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.Equal(ItemState.Failed, Assert.Single(result.Items).Status);
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task Existing_quarantine_destination_is_never_overwritten()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        var blocker = Path.Combine(_directories.DataRoot, "Quarantine", "op-1", "1", "payload");
        Directory.CreateDirectory(blocker);
        File.WriteAllText(Path.Combine(blocker, "existing.txt"), "keep me");

        var result = await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.NotEqual(ItemState.Quarantined, Assert.Single(result.Items).Status);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(blocker, "existing.txt")));
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task Restore_conflict_never_overwrites_and_keep_both_is_explicit()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);
        var item = Assert.Single(await _quarantine.GetItemsAsync(CancellationToken.None));
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.bin"), "newer data");

        var stopped = await _quarantine.RestoreAsync([item.Id], RestoreConflictMode.Stop, CancellationToken.None);

        Assert.Equal("Conflict", Assert.Single(stopped.Items).Status);
        Assert.Equal("newer data", File.ReadAllText(Path.Combine(source, "a.bin")));
        Assert.Equal(ItemState.Quarantined, (await _repository.GetItemAsync(item.Id, CancellationToken.None))!.Status);

        var both = await _quarantine.RestoreAsync([item.Id], RestoreConflictMode.KeepBoth, CancellationToken.None);

        var outcome = Assert.Single(both.Items);
        Assert.Equal(ItemState.Restored, outcome.Status);
        Assert.Equal("newer data", File.ReadAllText(Path.Combine(source, "a.bin")));
        Assert.Equal("cache-bytes", File.ReadAllText(Path.Combine(outcome.Path, "a.bin")));
    }

    [Fact]
    public async Task Tampered_quarantine_item_is_not_restored()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);
        var item = Assert.Single(await _quarantine.GetItemsAsync(CancellationToken.None));
        File.AppendAllText(Path.Combine(item.QuarantinePath, "a.bin"), "tampered");

        var result = await _quarantine.RestoreAsync([item.Id], RestoreConflictMode.Stop, CancellationToken.None);

        Assert.Equal(ItemState.VerificationFailed, Assert.Single(result.Items).Status);
        Assert.False(Directory.Exists(source));
    }

    [Fact]
    public async Task Permanent_delete_needs_exact_confirmation_and_keeps_audit()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, _) = await ApprovedCachePlanAsync();
        await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);
        var item = Assert.Single(await _quarantine.GetItemsAsync(CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _quarantine.DeletePermanentlyAsync([item.Id], "delete", CancellationToken.None));
        Assert.True(Directory.Exists(item.QuarantinePath));

        var result = await _quarantine.DeletePermanentlyAsync([item.Id], QuarantineService.DeleteConfirmation, CancellationToken.None);

        Assert.Equal(ItemState.PermanentlyDeleted, Assert.Single(result.Items).Status);
        Assert.False(Directory.Exists(item.QuarantinePath));
        Assert.Equal(ItemState.PermanentlyDeleted, (await _repository.GetItemAsync(item.Id, CancellationToken.None))!.Status);
        Assert.Contains(await _audit.GetRecentAsync(20, CancellationToken.None), entry => entry.Action == "PermanentDeletion");
    }

    [Fact]
    public async Task Permanent_delete_of_an_original_path_is_refused()
    {
        var file = AddFile(@"Original\keep.txt");

        Assert.Throws<InvalidOperationException>(() => FileOperations.DeleteInsideQuarantine(Path.GetDirectoryName(file)!, Path.Combine(_directories.DataRoot, "Quarantine")));
        Assert.Throws<InvalidOperationException>(() => FileOperations.DeleteInsideQuarantine(Path.Combine(_directories.DataRoot, "Quarantine"), Path.Combine(_directories.DataRoot, "Quarantine")));
        Assert.Throws<InvalidOperationException>(() => FileOperations.DeleteInsideQuarantine(Path.Combine(_directories.DataRoot, "Quarantine", "..", "cleanup.db"), Path.Combine(_directories.DataRoot, "Quarantine")));

        Assert.True(File.Exists(file));

        // A database row pointing at an original must fail too.
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, _) = await ApprovedCachePlanAsync();
        await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);
        var item = Assert.Single(await _quarantine.GetItemsAsync(CancellationToken.None));
        await _repository.SetQuarantinePathAsync(item.Id, Path.GetDirectoryName(file)!, CancellationToken.None);

        await _quarantine.DeletePermanentlyAsync([item.Id], QuarantineService.DeleteConfirmation, CancellationToken.None);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Restored_item_cannot_be_deleted_or_restored_again()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        await _quarantine.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);
        var item = Assert.Single(await _quarantine.GetItemsAsync(CancellationToken.None));
        await _quarantine.RestoreAsync([item.Id], RestoreConflictMode.Stop, CancellationToken.None);

        var result = await _quarantine.DeletePermanentlyAsync([item.Id], QuarantineService.DeleteConfirmation, CancellationToken.None);

        Assert.Equal(ItemState.Failed, Assert.Single(result.Items).Status);
        Assert.True(File.Exists(Path.Combine(source, "a.bin")));
    }

    [Fact]
    public async Task Cross_volume_quarantine_verifies_before_removing_and_restores()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        var crossVolume = new QuarantineService(_repository, _audit, _validator, _directories, (_, _) => false);

        var moved = await crossVolume.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.Equal(ItemState.Quarantined, Assert.Single(moved.Items).Status);
        Assert.False(Directory.Exists(source));
        var item = Assert.Single(await crossVolume.GetItemsAsync(CancellationToken.None));
        Assert.Equal("cache-bytes", File.ReadAllText(Path.Combine(item.QuarantinePath, "a.bin")));

        var restored = await crossVolume.RestoreAsync([item.Id], RestoreConflictMode.Stop, CancellationToken.None);

        Assert.Equal(ItemState.Restored, Assert.Single(restored.Items).Status);
        Assert.Equal("cache-bytes", File.ReadAllText(Path.Combine(source, "a.bin")));
    }

    [Fact]
    public async Task Cross_volume_blocks_files_with_alternate_streams_and_keeps_them()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        _rules.Root = _root;
        var file = AddFile(@"App\Cache\a.bin");
        using (var stream = new FileStream(file + ":extra", FileMode.Create))
        {
            stream.WriteByte(1);
        }

        await ScanAsync();
        var preview = await _planner.CreatePlanAsync([Cache(Rel(@"App\Cache"))], CancellationToken.None);
        await _planner.ApproveAsync(preview.Plan.Id, CancellationToken.None);
        var crossVolume = new QuarantineService(_repository, _audit, _validator, _directories, (_, _) => false);

        var result = await crossVolume.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None);

        Assert.Contains("alternate", Assert.Single(result.Items).Message, StringComparison.Ordinal);
        Assert.True(File.Exists(file));
    }

    [Theory]
    [InlineData(true, false, ItemState.Failed)]
    [InlineData(false, true, ItemState.Quarantined)]
    [InlineData(true, true, ItemState.RecoveryRequired)]
    [InlineData(false, false, ItemState.RecoveryRequired)]
    public async Task Crash_recovery_works_out_the_state_and_changes_no_files(bool originalExists, bool quarantinedExists, string expected)
    {
        var original = Rel("Interrupted");
        var quarantined = Path.Combine(_directories.DataRoot, "Quarantine", "op-9", "1", "payload");
        if (originalExists)
        {
            AddFile(@"Interrupted\a.txt");
        }

        if (quarantinedExists)
        {
            Directory.CreateDirectory(quarantined);
            File.WriteAllText(Path.Combine(quarantined, "a.txt"), "x");
        }

        long itemId;
        using (var connection = _connections.Create())
        {
            await connection.ExecuteAsync("INSERT INTO CleanupPlans (CreatedAt, Status) VALUES ('t', 'InProgress'); INSERT INTO QuarantineOperations (CleanupPlanId, CreatedAt, Status) VALUES (1, 't', 'InProgress');");
            itemId = await connection.ExecuteScalarAsync<long>(
                "INSERT INTO QuarantineItems (OperationId, OriginalPath, QuarantinePath, SizeBytes, Status) VALUES (1, @original, @quarantined, 1, 'InProgress') RETURNING Id",
                new { original, quarantined });
        }

        var result = await _quarantine.RecoverAsync(CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Items).Status);
        Assert.Equal(originalExists, Directory.Exists(original));
        Assert.Equal(quarantinedExists, Directory.Exists(quarantined));
        Assert.Equal(expected, (await _repository.GetItemAsync(itemId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Database_unavailable_blocks_cleanup_and_files_stay()
    {
        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        var (preview, source) = await ApprovedCachePlanAsync();
        var broken = new SqliteConnectionFactory(Path.Combine(_directories.DataRoot, "missing", "none.db"));
        var repository = new CleanupRepository(broken);
        var service = new QuarantineService(repository, new AuditRepository(broken), _validator, _directories);

        await Assert.ThrowsAnyAsync<Exception>(() => service.ExecuteAsync(preview.Plan.Id, null, CancellationToken.None));

        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task Orphans_need_history_strong_evidence_and_a_missing_install_folder()
    {
        var reliable = new OrphanSource("a", "A", "Registry", @"D:\A", "t", 3, @"D:\Data\A", 1, "e", true);

        Assert.Equal(OrphanClassification.ConfirmedByHistory, OrphanService.Classify(reliable, false));
        Assert.Equal(OrphanClassification.Potential, OrphanService.Classify(reliable, true));
        Assert.Equal(OrphanClassification.Potential, OrphanService.Classify(reliable with { InstalledSnapshots = 0 }, false));
        Assert.Equal(OrphanClassification.Potential, OrphanService.Classify(reliable with { DetectionSource = "StartMenu" }, false));
        Assert.Equal(OrphanClassification.Potential, OrphanService.Classify(reliable with { HasStrongEvidence = false }, false));
        Assert.Equal(OrphanClassification.Potential, OrphanService.Classify(reliable with { InstallLocation = null }, false));
    }

    [Fact]
    public async Task Detection_lists_old_unlinked_folders_as_unconfirmed_and_skips_installed_and_protected()
    {
        AddFile(@"OldThing\big.bin", new string('x', 2000));
        AddFile(@"Owned\big.bin", new string('x', 2000));
        AddFile(@"Tiny\small.bin");
        await ScanAsync();
        using (var connection = _connections.Create())
        {
            await connection.ExecuteAsync("UPDATE ScanItems SET LastModifiedAt = '2020-01-01T00:00:00.0000000Z' WHERE ItemType = 'Folder'");
            await connection.ExecuteAsync(
                "INSERT INTO StorageLocations (Path, DataType, OwnershipStatus, SizeBytes) VALUES (@a, 'ApplicationData', 'UnknownPreserved', 2000), (@b, 'ApplicationData', 'UnknownPreserved', 10)",
                new { a = Rel("OldThing"), b = Rel("Tiny") });
        }

        await MapAsync(ApplicationStatus.Installed, OwnershipState.InstalledProtected, "Owned");
        var service = new OrphanService(_repository, _audit, _policy, new OrphanOptions(180, 1000));

        var found = await service.DetectAsync(CancellationToken.None);

        var candidate = Assert.Single(found);
        Assert.Equal(Rel("OldThing"), candidate.Path);
        Assert.Equal(OrphanClassification.Unconfirmed, candidate.Classification);
        Assert.Equal(CandidateDecision.Pending, candidate.Decision);
    }

    [Fact]
    public async Task Decision_on_a_candidate_survives_a_new_detection()
    {
        AddFile(@"OldThing\big.bin", new string('x', 2000));
        await ScanAsync();
        using (var connection = _connections.Create())
        {
            await connection.ExecuteAsync("UPDATE ScanItems SET LastModifiedAt = '2020-01-01T00:00:00.0000000Z' WHERE ItemType = 'Folder'");
            await connection.ExecuteAsync("INSERT INTO StorageLocations (Path, DataType, OwnershipStatus, SizeBytes) VALUES (@a, 'ApplicationData', 'UnknownPreserved', 2000)", new { a = Rel("OldThing") });
        }

        var service = new OrphanService(_repository, _audit, _policy, new OrphanOptions(180, 1000));
        var first = Assert.Single(await service.DetectAsync(CancellationToken.None));

        await service.SetDecisionAsync(first.Id, CandidateDecision.Kept, CancellationToken.None);

        Assert.Equal(CandidateDecision.Kept, Assert.Single(await service.DetectAsync(CancellationToken.None)).Decision);
    }

    [Fact]
    public async Task Cache_analyzer_lists_only_rule_targets_and_selects_nothing_by_default()
    {
        _rules.Root = _root;
        AddFile(@"App\Cache\a.bin");
        AddFile(@"App\Settings\b.bin");
        await ScanAsync();

        var candidate = Assert.Single(await new CacheAnalyzer(_rules, _repository, _policy).AnalyzeAsync(CancellationToken.None));

        Assert.Equal(Rel(@"App\Cache"), candidate.Path);
        Assert.False(candidate.IsEligible);

        _rules.Enable(FakeRules.CacheRule(_root).RuleId);
        Assert.True(Assert.Single(await new CacheAnalyzer(_rules, _repository, _policy).AnalyzeAsync(CancellationToken.None)).IsEligible);
    }

    [Theory]
    [InlineData(ReportKind.Applications)]
    [InlineData(ReportKind.StorageScan)]
    [InlineData(ReportKind.OwnershipEvidence)]
    [InlineData(ReportKind.OrphanCandidates)]
    [InlineData(ReportKind.CleanupPreview)]
    [InlineData(ReportKind.QuarantineHistory)]
    [InlineData(ReportKind.DeletionHistory)]
    [InlineData(ReportKind.RecoveredSpace)]
    public async Task Every_report_exports_as_csv_and_json(string kind)
    {
        var service = new ReportService(_connections);
        var csv = Path.Combine(_directories.DataRoot, kind + ".csv");
        var json = Path.Combine(_directories.DataRoot, kind + ".json");

        await service.ExportAsync(kind, "csv", csv, CancellationToken.None);
        await service.ExportAsync(kind, "json", json, CancellationToken.None);

        Assert.True(new FileInfo(csv).Length > 0);
        Assert.StartsWith("[", File.ReadAllText(json), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=cmd()", "'=cmd()")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("-5", "-5")]
    public void Csv_values_are_escaped(string value, string expected) => Assert.Equal(expected, ReportService.Escape(value));

    private static async Task Cmd(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class FixedSettings(string root) : IScanConfigurationStore
    {
        public Task<ScanConfiguration> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ScanConfiguration([new ScanRoot(root, "t", "t")], [], 2));

        public Task SaveAsync(ScanConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedDirectories(string data) : IDataDirectoryService
    {
        public bool IsPortable => false;

        public bool IsFirstRun => false;

        public string DataRoot => data;

        public string LogsDirectory => Path.Combine(data, "Logs");

        public string DatabasePath => Path.Combine(data, "cleanup.db");

        public void SaveDataRoot(string path)
        {
        }
    }

    private sealed class FakeProcesses : IProcessInspector
    {
        public string? Running { get; set; }

        public bool IsRunning(string processName) => processName == Running;

        public string? FindProcessInside(string path) => null;
    }

    private sealed class FakeRules : IRuleCatalog
    {
        private readonly HashSet<string> _enabled = [];

        public string Root { get; set; } = @"C:\unset";

        public int OrphanMinimumAge { get; set; }

        public static CleanupRule OrphanRule => new("orphan", "1", "generic", "*", "Folder", "OrphanedData", "Leftovers", false, false, [], 0, "r", "w", "v");

        public static CleanupRule CacheRule(string root) => new("cache", "1", "App", Path.Combine(root, "App", "Cache"), "Folder", "ApplicationCache", "Application cache", false, true, ["app"], 0, "r", "w", "v");

        public void Enable(string id) => _enabled.Add(id);

        public Task<IReadOnlyList<CleanupRule>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CleanupRule>>(
        [
            OrphanRule with { CleanupAllowed = _enabled.Contains(OrphanRule.RuleId), MinimumAgeDays = OrphanMinimumAge },
            CacheRule(Root) with { CleanupAllowed = _enabled.Contains("cache") },
        ]);

        public Task SetEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken)
        {
            if (enabled)
            {
                _enabled.Add(ruleId);
            }
            else
            {
                _enabled.Remove(ruleId);
            }

            return Task.CompletedTask;
        }
    }
}
