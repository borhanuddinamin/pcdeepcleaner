using CommunityToolkit.Mvvm.ComponentModel;
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

public abstract class SelectableRow : ObservableObject
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SelectableCache(CacheCandidate candidate) : SelectableRow
{
    public CacheCandidate Candidate { get; } = candidate;
}
