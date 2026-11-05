using System.Diagnostics;

namespace WCM.Infrastructure.Discovery;

internal sealed record ExecutableMetadata(string? ProductName, string? Company, string? Version);

internal static class ApplicationMetadataReader
{
    public static ExecutableMetadata? Read(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return new ExecutableMetadata(Clean(info.ProductName), Clean(info.CompanyName), Clean(info.ProductVersion));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
