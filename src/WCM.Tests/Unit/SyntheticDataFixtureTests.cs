using WCM.Tests.Fixtures;

namespace WCM.Tests.Unit;

public class SyntheticDataFixtureTests
{
    [Fact]
    public void Creates_files_under_temp_and_removes_them_on_dispose()
    {
        string root;
        using (var fixture = new SyntheticDataFixture())
        {
            root = fixture.Root;
            Assert.StartsWith(Path.GetTempPath(), root, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(fixture.AddFile(@"App\Cache\a.bin")));
        }

        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData(@"..\outside.txt")]
    [InlineData(@"C:\outside.txt")]
    public void Rejects_paths_outside_the_fixture_root(string path)
    {
        using var fixture = new SyntheticDataFixture();
        Assert.Throws<ArgumentException>(() => fixture.AddFile(path));
    }
}
