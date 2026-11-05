using WCM.Infrastructure.Logging;
using WCM.Tests.Fixtures;

namespace WCM.Tests.Unit;

public class LoggingConfigurationTests
{
    [Fact]
    public void Writes_structured_logs_to_the_given_directory()
    {
        using var fixture = new SyntheticDataFixture();
        var logs = Path.Combine(fixture.Root, "Logs");

        using (var logger = LoggingConfiguration.CreateLogger(logs))
        {
            logger.Information("Started {Phase}", 1);
        }

        var content = File.ReadAllText(Directory.GetFiles(logs).Single());
        Assert.Contains("\"Phase\":1", content, StringComparison.Ordinal);
    }
}
