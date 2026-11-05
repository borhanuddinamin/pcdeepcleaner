using CommunityToolkit.Mvvm.ComponentModel;

namespace WCM.Desktop.ViewModels;

public abstract class PageViewModel(string title) : ObservableObject
{
    public string Title { get; } = title;
}

public sealed class PlaceholderPageViewModel(string title) : PageViewModel(title)
{
    public string Message => "This screen isn't built yet.";
}
