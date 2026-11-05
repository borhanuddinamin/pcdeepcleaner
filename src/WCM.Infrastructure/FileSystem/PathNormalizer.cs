using System.Runtime.InteropServices;
using System.Text;

namespace WCM.Infrastructure.FileSystem;

public static partial class PathNormalizer
{
    /// <summary>Returns a full path with long folder names and no trailing separator.</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var buffer = new StringBuilder(1024);
        var length = GetLongPathName(full, buffer, buffer.Capacity);

        // Short (8.3) names such as NASSSO~1 would otherwise defeat path comparison.
        if (length > 0 && length < buffer.Capacity)
        {
            full = buffer.ToString();
        }

        return Path.TrimEndingDirectorySeparator(full);
    }

#pragma warning disable SYSLIB1054 // StringBuilder output needs the classic marshaller.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLongPathName(string shortPath, StringBuilder longPath, int size);
#pragma warning restore SYSLIB1054
}
