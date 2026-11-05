using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WCM.Desktop.Services;

public interface IDialogService
{
    bool Confirm(string title, string message, string confirmText);

    /// <returns>True only when the exact word was typed.</returns>
    bool ConfirmByTyping(string title, string message, string requiredText);

    string? PickFolder();

    string? PickSaveFile(string defaultName, string filter);
}

public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message, string confirmText) =>
        Show(title, message, confirmText, null);

    public bool ConfirmByTyping(string title, string message, string requiredText) =>
        Show(title, message, "Delete permanently", requiredText);

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string defaultName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = defaultName, Filter = filter, OverwritePrompt = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static bool Show(string title, string message, string confirmText, string? requiredText)
    {
        var resources = System.Windows.Application.Current;
        var window = new Window
        {
            Title = title,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = resources.MainWindow,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)resources.FindResource("WindowBrush"),
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var confirm = new Button { Content = confirmText, IsDefault = requiredText is null };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, requiredText is null ? "PrimaryButton" : "DangerButton");
        var box = new TextBox { Margin = new Thickness(0, 0, 0, 14) };
        if (requiredText is not null)
        {
            confirm.IsEnabled = false;
            box.TextChanged += (_, _) => confirm.IsEnabled = string.Equals(box.Text, requiredText, StringComparison.Ordinal);
            AutomationPropertiesHelper.Name(box, $"Type {requiredText} to confirm");
            panel.Children.Add(new TextBlock { Text = $"Type {requiredText} to confirm:", Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(box);
        }

        confirm.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        window.Content = panel;
        var accepted = window.ShowDialog() == true;
        return accepted && (requiredText is null || string.Equals(box.Text, requiredText, StringComparison.Ordinal));
    }

    private static class AutomationPropertiesHelper
    {
        public static void Name(DependencyObject element, string name) =>
            System.Windows.Automation.AutomationProperties.SetName(element, name);
    }
}

public sealed class ThemeService
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    public void Apply(string theme)
    {
        var dark = theme == Dark || (theme != Light && SystemUsesDark());
        var dictionaries = System_App.Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = new Uri($"Themes/{(dark ? Dark : Light)}.xaml", UriKind.Relative) };
        foreach (Window window in System_App.Windows)
        {
            SetTitleBar(window, dark);
        }

        IsDark = dark;
    }

    public bool IsDark { get; private set; }

    public static void SetTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            var value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
        }
    }

    private static global::System.Windows.Application System_App => global::System.Windows.Application.Current;

    private static bool SystemUsesDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int and 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
