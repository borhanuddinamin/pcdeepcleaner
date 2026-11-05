using System.Text.Json;
using WCM.Application.Interfaces;

namespace WCM.Application.Services;

public sealed class UserProtectedPaths(ISettingsStore settings)
{
    private const string Key = "UserProtectedPaths";
    private volatile IReadOnlyList<string> _current = [];

    public IReadOnlyList<string> Current => _current;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var json = await settings.GetAsync(Key, cancellationToken);
        _current = json is null ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        await settings.SetAsync(Key, JsonSerializer.Serialize(paths), cancellationToken);
        _current = paths;
    }
}
