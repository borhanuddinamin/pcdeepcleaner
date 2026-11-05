using System.Globalization;

namespace WCM.Infrastructure.SQLite;

internal static class UtcTimestamp
{
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static string? Format(DateTimeOffset? value) => value is null ? null : Format(value.Value);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? ParseNullable(string? value) => value is null ? null : Parse(value);
}
