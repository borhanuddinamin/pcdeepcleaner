using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WCM.Application.DTOs;
using WCM.Application.Interfaces;
using WCM.Application.Services;
using WCM.Desktop.Services;

namespace WCM.Desktop.ViewModels;

public sealed class RuleRow(CleanupRule rule, bool enabled, Func<RuleRow, Task> changed) : ObservableObject
{
    private bool _enabled = enabled;

    public CleanupRule Rule { get; } = rule;

    // The setter runs the confirmation, so mouse, keyboard and assistive technology all behave the same.
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                _ = changed(this);
            }
        }
    }

    public void Revert()
    {
        _enabled = false;
        OnPropertyChanged(nameof(Enabled));
    }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly IDataDirectoryService _directories;
    private readonly IScanConfigurationStore _scan;
    private readonly IRuleCatalog _rules;
    private readonly ISettingsStore _store;
    private readonly UserProtectedPaths _protected;
    private readonly ThemeService _theme;
    private readonly ILogLevelControl _logLevel;
    private readonly IDialogService _dialogs;
    private string _dataRoot;
    private string _selectedTheme = ThemeService.System;
    private string _selectedLogLevel = "Information";
    private int _concurrency = 2;
    private int _retentionDays = 30;
    private ScanConfiguration? _configuration;
    private IReadOnlyList<RuleRow> _ruleRows = [];
    private ScanRoot? _selectedRoot;
    private string? _selectedProtected;

    public SettingsViewModel(
        IDataDirectoryService directories,
        IScanConfigurationStore scan,
        IRuleCatalog rules,
        ISettingsStore store,
        UserProtectedPaths protectedPaths,
        ThemeService theme,
        ILogLevelControl logLevel,
        IDialogService dialogs)
        : base("Settings", "\uE713")
    {
        _directories = directories;
        _scan = scan;
        _rules = rules;
        _store = store;
        _protected = protectedPaths;
        _theme = theme;
        _logLevel = logLevel;
        _dialogs = dialogs;
        _dataRoot = directories.DataRoot;
        IsFirstRun = directories.IsFirstRun;
        SaveDataRootCommand = new RelayCommand(SaveDataRoot, () => !IsPortable);
        BrowseDataRootCommand = new RelayCommand(BrowseDataRoot, () => !IsPortable);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        AddRootCommand = new RelayCommand(AddRoot);
        RemoveRootCommand = new RelayCommand(RemoveRoot);
        AddProtectedCommand = new AsyncRelayCommand(AddProtectedAsync);
        RemoveProtectedCommand = new AsyncRelayCommand(RemoveProtectedAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
    }

    public bool IsFirstRun { get; }

    public bool IsPortable => _directories.IsPortable;

    public string QuarantineFolder => Path.Combine(_dataRoot, "Quarantine");

    public IReadOnlyList<string> Themes { get; } = [ThemeService.System, ThemeService.Light, ThemeService.Dark];

    public IReadOnlyList<string> LogLevels { get; } = ["Debug", "Information", "Warning", "Error"];

    public IRelayCommand SaveDataRootCommand { get; }

    public IRelayCommand BrowseDataRootCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand AddRootCommand { get; }

    public IRelayCommand RemoveRootCommand { get; }

    public IAsyncRelayCommand AddProtectedCommand { get; }

    public IAsyncRelayCommand RemoveProtectedCommand { get; }

    public IAsyncRelayCommand ExportCommand { get; }

    public ObservableCollection<ScanRoot> Roots { get; } = [];

    public ObservableCollection<string> Exclusions { get; } = [];

    public ObservableCollection<string> ProtectedPaths { get; } = [];

    public IReadOnlyList<RuleRow> Rules
    {
        get => _ruleRows;
        private set => SetProperty(ref _ruleRows, value);
    }

    public ScanRoot? SelectedRoot
    {
        get => _selectedRoot;
        set => SetProperty(ref _selectedRoot, value);
    }

    public string? SelectedProtected
    {
        get => _selectedProtected;
        set => SetProperty(ref _selectedProtected, value);
    }

    public string DataRoot
    {
        get => _dataRoot;
        set
        {
            if (SetProperty(ref _dataRoot, value))
            {
                OnPropertyChanged(nameof(QuarantineFolder));
            }
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _theme.Apply(value);
                _ = RunAsync(string.Empty, () => _store.SetAsync("Theme", value, CancellationToken.None));
            }
        }
    }

    public string SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (SetProperty(ref _selectedLogLevel, value))
            {
                _logLevel.Set(value);
                _ = RunAsync(string.Empty, () => _store.SetAsync("LogLevel", value, CancellationToken.None));
            }
        }
    }

    public int Concurrency
    {
        get => _concurrency;
        set => SetProperty(ref _concurrency, Math.Clamp(value, 1, 16));
    }

    public int RetentionDays
    {
        get => _retentionDays;
        set => SetProperty(ref _retentionDays, Math.Clamp(value, 1, 3650));
    }

    public override Task RefreshAsync() => RunAsync(string.Empty, async () =>
    {
        _configuration = await _scan.LoadAsync(CancellationToken.None);
        Roots.Clear();
        foreach (var root in _configuration.Roots)
        {
            Roots.Add(root);
        }

        Exclusions.Clear();
        foreach (var exclusion in _configuration.Exclusions)
        {
            Exclusions.Add($"{exclusion.Path}: {exclusion.Reason}");
        }

        Concurrency = _configuration.MaxConcurrency;
        ProtectedPaths.Clear();
        foreach (var path in _protected.Current)
        {
            ProtectedPaths.Add(path);
        }

        Rules = (await _rules.LoadAsync(CancellationToken.None)).Select(rule => new RuleRow(rule, rule.CleanupAllowed, SetRuleAsync)).ToList();
        _selectedTheme = await _store.GetAsync("Theme", CancellationToken.None) ?? ThemeService.System;
        OnPropertyChanged(nameof(SelectedTheme));
        _selectedLogLevel = await _store.GetAsync("LogLevel", CancellationToken.None) ?? "Information";
        OnPropertyChanged(nameof(SelectedLogLevel));
        if (int.TryParse(await _store.GetAsync("RetentionDays", CancellationToken.None), out var days))
        {
            RetentionDays = days;
        }
    });

    private void SaveDataRoot()
    {
        Problem = string.Empty;
        try
        {
            _directories.SaveDataRoot(DataRoot);
            Status = "Saved. WCM will use this folder the next time it starts.";
        }
        catch (ArgumentException ex)
        {
            Status = string.Empty;
            Problem = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = string.Empty;
            Problem = $"Couldn't save to this folder: {ex.Message}";
        }
    }

    private void BrowseDataRoot()
    {
        var folder = _dialogs.PickFolder();
        if (folder is not null)
        {
            DataRoot = folder;
        }
    }

    private void AddRoot()
    {
        var folder = _dialogs.PickFolder();
        if (folder is not null)
        {
            Roots.Add(new ScanRoot(folder, "Added by you", "Scan; classify by rule"));
        }
    }

    private void RemoveRoot()
    {
        if (SelectedRoot is not null)
        {
            Roots.Remove(SelectedRoot);
        }
    }

    private Task SaveAsync() => RunAsync("Saving...", async () =>
    {
        var current = _configuration ?? await _scan.LoadAsync(CancellationToken.None);
        await _scan.SaveAsync(current with { Roots = Roots.ToList(), MaxConcurrency = Concurrency }, CancellationToken.None);
        await _store.SetAsync("RetentionDays", RetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture), CancellationToken.None);
        Status = "Settings saved.";
    });

    private Task AddProtectedAsync() => RunAsync(string.Empty, async () =>
    {
        var folder = _dialogs.PickFolder();
        if (folder is not null)
        {
            ProtectedPaths.Add(folder);
            await _protected.SaveAsync(ProtectedPaths.ToList(), CancellationToken.None);
            Status = "Protected path added. It applies immediately.";
        }
    });

    private async Task RemoveProtectedAsync()
    {
        if (SelectedProtected is not null && _dialogs.Confirm("Remove protection?", $"Stop protecting {SelectedProtected}? WCM's built-in protections still apply.", "Remove"))
        {
            var path = SelectedProtected;
            await RunAsync(string.Empty, async () =>
            {
                ProtectedPaths.Remove(path);
                await _protected.SaveAsync(ProtectedPaths.ToList(), CancellationToken.None);
                Status = "Protected path removed.";
            });
        }
    }

    private async Task SetRuleAsync(RuleRow row)
    {
        // Lets the setter return before a modal dialog opens.
        await Task.Yield();
        if (row.Enabled && !_dialogs.Confirm(
            "Enable this cleanup rule?",
            $"{row.Rule.RuleId}: {row.Rule.Target}\n\nRebuild cost: {row.Rule.RebuildCost}\nWarning: {row.Rule.SensitiveWarning}\n\nEnabling only makes items eligible for preview. You still approve each plan.",
            "Enable rule"))
        {
            row.Revert();
            return;
        }

        await RunAsync(string.Empty, async () =>
        {
            await _rules.SetEnabledAsync(row.Rule.RuleId, row.Enabled, CancellationToken.None);
            Status = row.Enabled ? $"Rule {row.Rule.RuleId} enabled." : $"Rule {row.Rule.RuleId} disabled.";
        });
    }

    private async Task ExportAsync()
    {
        var path = _dialogs.PickSaveFile("wcm-settings.json", "JSON file|*.json");
        if (path is not null)
        {
            await RunAsync("Exporting...", async () =>
            {
                var content = new
                {
                    Theme = SelectedTheme,
                    LogLevel = SelectedLogLevel,
                    RetentionDays,
                    Concurrency,
                    Roots = Roots.ToList(),
                    ProtectedPaths = ProtectedPaths.ToList(),
                    EnabledRules = Rules.Where(rule => rule.Enabled).Select(rule => rule.Rule.RuleId).ToList(),
                };
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }));
                Status = $"Settings exported to {path}";
            });
        }
    }
}
