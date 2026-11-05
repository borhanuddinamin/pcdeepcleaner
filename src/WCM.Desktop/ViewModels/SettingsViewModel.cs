using System.IO;
using CommunityToolkit.Mvvm.Input;
using WCM.Application.Interfaces;

namespace WCM.Desktop.ViewModels;

public sealed class SettingsViewModel : PageViewModel
{
    private readonly IDataDirectoryService _dataDirectories;
    private string _dataRoot;
    private string _status = string.Empty;

    public SettingsViewModel(IDataDirectoryService dataDirectories)
        : base("Settings")
    {
        _dataDirectories = dataDirectories;
        _dataRoot = dataDirectories.DataRoot;
        IsFirstRun = dataDirectories.IsFirstRun;
        SaveCommand = new RelayCommand(Save, () => !IsPortable);
    }

    public bool IsFirstRun { get; }

    public bool IsPortable => _dataDirectories.IsPortable;

    public IRelayCommand SaveCommand { get; }

    public string DataRoot
    {
        get => _dataRoot;
        set => SetProperty(ref _dataRoot, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private void Save()
    {
        try
        {
            _dataDirectories.SaveDataRoot(DataRoot);
            Status = "Saved. WCM will use this folder the next time it starts.";
        }
        catch (ArgumentException ex)
        {
            Status = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't save to this folder: {ex.Message}";
        }
    }
}
