using WCM.Application.DTOs;

namespace WCM.Desktop.ViewModels;

/// <summary>Items you chose for preview. Being here doesn't approve anything.</summary>
public sealed class CleanupBasket
{
    private readonly Dictionary<string, CleanupSelection> _items = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CleanupSelection> Items => _items.Values.ToList();

    public void Add(CleanupSelection selection) => _items[selection.Path] = selection;

    public void Clear() => _items.Clear();
}

public sealed class SelectableCache(CacheCandidate candidate)
{
    public CacheCandidate Candidate { get; } = candidate;

    public bool IsSelected { get; set; }
}
