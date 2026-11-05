using CommunityToolkit.Mvvm.ComponentModel;

namespace WCM.Desktop.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel()
    {
    }

    protected PageViewModel(string title)
    {
        Title = title;
    }

    public virtual string Title { get; } = string.Empty;
}

public sealed class PlaceholderPageViewModel(string title) : PageViewModel(title)
{
    public string Message => "This screen isn't built yet.";
}
