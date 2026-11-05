using WCM.Infrastructure.Windows;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Unit;

public sealed class DataDirectoryServiceTests : IDisposable
{
    private readonly SyntheticDataFixture _fixture = new();
    private readonly string _settings;
    private readonly string _exe;
    private readonly string _protected;

    public DataDirectoryServiceTests()
    {
        _settings = Path.Combine(_fixture.Root, "settings");
        _exe = Path.Combine(_fixture.Root, "exe");
        _protected = Path.Combine(_fixture.Root, "Windows");
        Directory.CreateDirectory(_exe);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void First_run_defaults_to_settings_directory_and_writes_nothing()
    {
        var service = Create();

        Assert.True(service.IsFirstRun);
        Assert.Equal(_settings, service.DataRoot);
        Assert.False(Directory.Exists(_settings));
    }

    [Fact]
    public void Saved_root_is_used_by_a_new_instance()
    {
        var chosen = Path.Combine(_fixture.Root, "chosen");

        Create().SaveDataRoot(chosen);
        var next = Create();

        Assert.False(next.IsFirstRun);
        Assert.Equal(chosen, next.DataRoot);
        Assert.Equal(Path.Combine(chosen, "Logs"), next.LogsDirectory);
    }

    [Fact]
    public void Portable_marker_uses_executable_directory()
    {
        _fixture.AddFile(@"exe\portable.marker");

        var service = Create();

        Assert.True(service.IsPortable);
        Assert.False(service.IsFirstRun);
        Assert.Equal(_exe, service.DataRoot);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"relative\path")]
    public void Rejects_paths_that_are_not_fully_qualified(string path) =>
        Assert.Throws<ArgumentException>(() => Create().SaveDataRoot(path));

    [Fact]
    public void Rejects_protected_location_and_writes_nothing()
    {
        var inside = Path.Combine(_protected, "WCM");

        Assert.Throws<ArgumentException>(() => Create().SaveDataRoot(inside));
        Assert.False(Directory.Exists(inside));
        Assert.False(Directory.Exists(_settings));
    }

    [Fact]
    public void Similar_prefix_is_not_treated_as_protected()
    {
        var sibling = _protected + "Data";

        Create().SaveDataRoot(sibling);

        Assert.Equal(sibling, Create().DataRoot);
    }

    [Fact]
    public void Corrupt_settings_fail_closed()
    {
        _fixture.AddFile(@"settings\bootstrap.json", "{ not json");

        Assert.Throws<InvalidDataException>(() => Create().DataRoot);
    }

    private DataDirectoryService Create() => new(_settings, _exe, [_protected]);
}
