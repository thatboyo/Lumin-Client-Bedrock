using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Lumin.Configuration;
using Lumin.Diagnostics;
using Lumin.Models;
using Lumin.Modules;

namespace Lumin.Views;

public partial class MainWindow : Window
{
    private const int HideGuiHotkeyId = 0x4C55;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkH = 0x48;
    private const int WmHotkey = 0x0312;

    private readonly ObservableCollection<ModuleItem> _allModules = new();
    private readonly ObservableCollection<ModuleItem> _viewModules = new();
    private readonly ModuleRegistry _registry = new();
    private readonly ConfigurationStore _configurationStore = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly ActivityLogger _activityLogger = new();
    private ClientConfiguration _configuration;
    private string _filter = "All";
    private string _category = "All";
    private bool _suppressPersistence;
    private bool _updatingProfileSelector;
    private bool _updatingThemeSelector;
    private bool _isScaleDragging;
    private IntPtr _windowHandle;
    private HwndSource? _windowSource;
    private bool _hideGuiHotkeyRegistered;

    public ObservableCollection<ModuleItem> ViewModules => _viewModules;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += EnableSystemBackdrop;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        DataContext = this;
        try
        {
            _configuration = _configurationStore.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _configuration = ConfigurationStore.CreateDefault();
            ProfileStatus.Text = $"Saved configuration could not be loaded: {ex.Message}";
        }

        var activeProfile = GetActiveProfile();
        ApplyTheme(activeProfile.Theme);
        foreach (var module in _registry.All)
        {
            module.IsEnabled = module.CanEnable && activeProfile.Modules.GetValueOrDefault(module.Id);
            _allModules.Add(module);
            module.PropertyChanged += Module_PropertyChanged;
        }
        QuickModules.ItemsSource = _allModules.Take(4).ToList();
        RefreshModules();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); PersistActiveProfile(); };
        ScaleSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(ScaleSlider_DragStarted), true);
        ScaleSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(ScaleSlider_DragCompleted), true);
        _updatingProfileSelector = true;
        ProfileSelector.DisplayMemberPath = nameof(ConfigurationProfile.Name);
        ProfileSelector.SelectedValuePath = nameof(ConfigurationProfile.Id);
        ProfileSelector.ItemsSource = _configuration.Profiles.ToList();
        ProfileSelector.SelectedValue = _configuration.ActiveProfileId;
        _updatingProfileSelector = false;
        _updatingThemeSelector = true;
        ThemeSelector.SelectedValue = NormalizeTheme(activeProfile.Theme);
        _updatingThemeSelector = false;
        InterfaceScaleTransform.ScaleX = activeProfile.InterfaceScale;
        InterfaceScaleTransform.ScaleY = activeProfile.InterfaceScale;
        ScaleSlider.Value = activeProfile.InterfaceScale;
        ScaleValue.Text = $"{activeProfile.InterfaceScale:P0}";
        ActiveProfileBadge.Text = $"  {activeProfile.Name.ToUpperInvariant()}";
        _activityLogger.IsEnabled = _registry.Find("activity-log")?.IsEnabled ?? false;
        UpdateActivityLogStatus();
        UpdateActiveCount();
    }

    private void EnableSystemBackdrop(object? sender, EventArgs e)
    {
        // Windows 11 supplies the blurred system backdrop. Older Windows versions keep
        // the app's own charcoal gradient and translucent surfaces as a graceful fallback.
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            _windowHandle = hwnd;
            _windowSource = HwndSource.FromHwnd(hwnd);
            _windowSource?.AddHook(WindowMessageHook);
            _hideGuiHotkeyRegistered = RegisterHotKey(hwnd, HideGuiHotkeyId, ModControl | ModShift, VkH);
            if (!_hideGuiHotkeyRegistered)
            {
                ProfileStatus.Text = "Hide GUI shortcut could not be registered (Ctrl+Shift+H may be in use).";
                var hideModule = _registry.Find("hide-gui");
                if (hideModule is not null) hideModule.IsEnabled = false;
            }
            var backdrop = 2; // DWMSBT_MAINWINDOW
            _ = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hideGuiHotkeyRegistered && _registry.Find("hide-gui")?.IsEnabled == true)
            WindowState = WindowState.Minimized;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        if (_hideGuiHotkeyRegistered)
            _ = UnregisterHotKey(_windowHandle, HideGuiHotkeyId);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != HideGuiHotkeyId) return IntPtr.Zero;

        var hideModule = _registry.Find("hide-gui");
        if (hideModule is not null)
            hideModule.IsEnabled = !hideModule.IsEnabled;
        handled = true;
        return IntPtr.Zero;
    }

    private void Module_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModuleItem.IsEnabled))
        {
            if (sender is ModuleItem changedModule)
            {
                if (changedModule.Id == "activity-log")
                {
                    if (changedModule.IsEnabled)
                    {
                        _activityLogger.IsEnabled = true;
                        RecordActivity("activity_log_enabled", changedModule.Id, "true");
                    }
                    else
                    {
                        RecordActivity("activity_log_disabled", changedModule.Id, "false");
                        _activityLogger.IsEnabled = false;
                    }
                    UpdateActivityLogStatus();
                }
                else
                {
                    RecordActivity("module_state_changed", changedModule.Id, changedModule.IsEnabled ? "enabled" : "disabled");
                }

                if (changedModule.Id == "hide-gui" && _hideGuiHotkeyRegistered)
                {
                    if (changedModule.IsEnabled)
                        WindowState = WindowState.Minimized;
                    else
                    {
                        WindowState = WindowState.Normal;
                        Activate();
                    }
                }
            }
            UpdateActiveCount();
            RefreshModules();
            if (!_suppressPersistence) PersistActiveProfile();
        }
    }

    private void UpdateActiveCount() => ActiveCount.Text = _allModules.Count(m => m.IsEnabled).ToString();

    private ConfigurationProfile GetActiveProfile() =>
        _configuration.Profiles.First(profile => profile.Id == _configuration.ActiveProfileId);

    private void ApplyProfile(ConfigurationProfile profile)
    {
        _suppressPersistence = true;
        try
        {
            foreach (var module in _allModules)
                module.IsEnabled = module.CanEnable && profile.Modules.GetValueOrDefault(module.Id);
            InterfaceScaleTransform.ScaleX = profile.InterfaceScale;
            InterfaceScaleTransform.ScaleY = profile.InterfaceScale;
            ScaleSlider.Value = profile.InterfaceScale;
            ScaleValue.Text = $"{profile.InterfaceScale:P0}";
            ActiveProfileBadge.Text = $"  {profile.Name.ToUpperInvariant()}";
            ApplyTheme(profile.Theme);
            _updatingThemeSelector = true;
            ThemeSelector.SelectedValue = NormalizeTheme(profile.Theme);
            _updatingThemeSelector = false;
            _activityLogger.IsEnabled = _registry.Find("activity-log")?.IsEnabled ?? false;
            UpdateActiveCount();
            RefreshModules();
        }
        finally
        {
            _suppressPersistence = false;
        }
        RecordActivity("profile_applied", null, profile.Name);
        UpdateActivityLogStatus();
    }

    private void PersistActiveProfile()
    {
        var profile = GetActiveProfile();
        profile.Modules = _allModules.ToDictionary(module => module.Id, module => module.CanEnable && module.IsEnabled, StringComparer.Ordinal);
        try
        {
            _configurationStore.Save(_configuration);
            ProfileStatus.Text = "Saved locally.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ProfileStatus.Text = $"Could not save configuration: {ex.Message}";
        }
    }

    private string UniqueProfileName(string baseName)
    {
        var used = _configuration.Profiles.Select(profile => profile.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = baseName[..Math.Min(baseName.Length, 40)];
        for (var index = 2; used.Contains(candidate); index++)
        {
            var suffix = $" {index}";
            candidate = baseName[..Math.Min(baseName.Length, 40 - suffix.Length)] + suffix;
        }
        return candidate;
    }

    private void RefreshProfileSelector()
    {
        _updatingProfileSelector = true;
        ProfileSelector.ItemsSource = _configuration.Profiles.ToList();
        ProfileSelector.SelectedValue = _configuration.ActiveProfileId;
        ActiveProfileBadge.Text = $"  {GetActiveProfile().Name.ToUpperInvariant()}";
        _updatingProfileSelector = false;
    }

    private void RecordActivity(string eventType, string? moduleId = null, string? value = null)
    {
        try
        {
            _activityLogger.Record(eventType, _configuration.ActiveProfileId, moduleId, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLogStatus.Text = $"Log write failed: {ex.Message}";
        }
    }

    private void UpdateActivityLogStatus() =>
        ActivityLogStatus.Text = _activityLogger.IsEnabled ? "Recording local Lumin events · no game data" : "Activity logging is off";

    private void RefreshModules()
    {
        var query = ModuleSearch?.Text?.Trim() ?? string.Empty;
        var filtered = _allModules.Where(module =>
            (_category == "All" || module.Category == _category) &&
            (_filter == "All" || _filter == "Enabled" && module.IsEnabled || _filter == "Disabled" && !module.IsEnabled || _filter == "Experimental" && module.IsExperimental) &&
            (string.IsNullOrEmpty(query) || module.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || module.Description.Contains(query, StringComparison.OrdinalIgnoreCase) || module.Category.Contains(query, StringComparison.OrdinalIgnoreCase)));
        _viewModules.Clear();
        foreach (var module in filtered) _viewModules.Add(module);
    }

    private void ModuleSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshModules();

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            _filter = tag;
            RefreshModules();
        }
    }

    private void SelectModule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ModuleItem selected }) return;
        foreach (var module in _allModules) module.IsSelected = ReferenceEquals(module, selected);
        DetailName.Text = selected.Name;
        DetailCategory.Text = $"{selected.Category.ToUpperInvariant()}  ·  {selected.StateLabel}";
        DetailDescription.Text = selected.Description;
        DetailAvailability.Text = selected.Availability;
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string section } || PageTitle is null) return;
        var isDashboard = section == "Dashboard";
        var isWorkspace = section is "Modules" or "Visuals" or "Movement" or "Combat" or "Utility";
        DashboardView.Visibility = isDashboard ? Visibility.Visible : Visibility.Collapsed;
        ModulesView.Visibility = isWorkspace ? Visibility.Visible : Visibility.Collapsed;
        OtherView.Visibility = section is "Settings" or "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        SettingsControls.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ProfileControls.Visibility = section == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        _category = section is "Visuals" or "Movement" or "Combat" or "Utility" ? section : "All";
        _filter = "All";
        if (ModuleSearch is not null) ModuleSearch.Text = string.Empty;
        RefreshModules();
        PageEyebrow.Text = section.ToUpperInvariant();
        PageTitle.Text = section switch
        {
            "Dashboard" => "A quieter kind of power.",
            "Modules" => "Module library",
            "Settings" => "Settings",
            "Profiles" => "Profiles",
            _ => section
        };
        PageDescription.Text = section switch
        {
            "Dashboard" => "Your modular workspace, shaped around the tools you choose.",
            "Modules" => "Explore module availability and preview local state changes.",
            "Visuals" => "Visual tools · availability is reported for the standalone preview.",
            "Movement" => "Movement experiments are isolated from live game controls.",
            "Combat" => "Training and diagnostics remain unavailable without an integration.",
            "Utility" => "Standalone tools and local application state.",
            "Settings" => "Lumin appearance and interface preferences.",
            "Profiles" => "Manage the configuration profile used by this preview.",
            _ => string.Empty
        };
        if (section == "Settings")
        {
            OtherHeading.Text = "Choose your atmosphere.";
            OtherBody.Text = $"{GetThemeDescription(GetActiveProfile().Theme)} Theme and interface preferences are saved with the active profile.";
        }
        else if (section == "Profiles")
        {
            OtherHeading.Text = "Lumin Default";
            OtherBody.Text = "Profiles store module states, interface scale, and visual theme locally. JSON import is validated before it replaces the current profile set.";
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleValue is null || InterfaceScaleTransform is null) return;
        var scale = e.NewValue;
        ScaleValue.Text = $"{scale:P0}";
        if (_isScaleDragging) return;
        ApplyInterfaceScale(scale);
    }

    private void ScaleSlider_DragStarted(object sender, DragStartedEventArgs e) => _isScaleDragging = true;

    private void ScaleSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isScaleDragging = false;
        ApplyInterfaceScale(ScaleSlider.Value);
    }

    private void ApplyInterfaceScale(double scale)
    {
        if (_suppressPersistence || _configuration is null) return;
        InterfaceScaleTransform.ScaleX = scale;
        InterfaceScaleTransform.ScaleY = scale;
        GetActiveProfile().InterfaceScale = scale;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingThemeSelector || ThemeSelector.SelectedValue is not string selectedTheme) return;
        var theme = NormalizeTheme(selectedTheme);
        var profile = GetActiveProfile();
        profile.Theme = theme;
        ApplyTheme(theme);
        OtherBody.Text = $"{GetThemeDescription(theme)} Theme and interface preferences are saved with the active profile.";
        PersistActiveProfile();
    }

    private static string NormalizeTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "rain" => "Rain",
        "synth" => "Synth",
        _ => "Lumin"
    };

    private static void ApplyTheme(string? theme)
    {
        var themeName = NormalizeTheme(theme);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = new Uri($"/Lumin;component/Themes/{themeName}.xaml", UriKind.Relative) };
        if (dictionaries.Count == 0) dictionaries.Add(replacement);
        else dictionaries[0] = replacement;
    }

    private static string GetThemeDescription(string? theme) => NormalizeTheme(theme) switch
    {
        "Rain" => "Rain brings storm-slate glass and cool aqua highlights.",
        "Synth" => "Synth pairs deep violet surfaces with neon rose and cyan accents.",
        _ => "Lumin keeps the interface monochrome with a restrained icy blue-white module glow."
    };

    private void ProfileSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfileSelector || ProfileSelector.SelectedValue is not string profileId) return;
        var selected = _configuration.Profiles.FirstOrDefault(profile => profile.Id == profileId);
        if (selected is null) return;
        _configuration.ActiveProfileId = selected.Id;
        ApplyProfile(selected);
        PersistActiveProfile();
        ProfileStatus.Text = $"Switched to {selected.Name}.";
    }

    private void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        var source = GetActiveProfile();
        var copy = new ConfigurationProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = UniqueProfileName($"{source.Name} Copy"),
            Modules = new Dictionary<string, bool>(source.Modules, StringComparer.Ordinal),
            InterfaceScale = source.InterfaceScale,
            Theme = source.Theme
        };
        _configuration.Profiles.Add(copy);
        _configuration.ActiveProfileId = copy.Id;
        RefreshProfileSelector();
        ApplyProfile(copy);
        PersistActiveProfile();
        ProfileStatus.Text = $"Created {copy.Name}.";
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_configuration.Profiles.Count <= 1)
        {
            ProfileStatus.Text = "Keep at least one profile.";
            return;
        }
        var current = GetActiveProfile();
        if (MessageBox.Show($"Delete the profile '{current.Name}'?", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _configuration.Profiles.Remove(current);
        var next = _configuration.Profiles[0];
        _configuration.ActiveProfileId = next.Id;
        RefreshProfileSelector();
        ApplyProfile(next);
        PersistActiveProfile();
        ProfileStatus.Text = $"Deleted {current.Name}.";
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Lumin configuration (*.json)|*.json", FileName = "lumin-configuration.json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _configurationStore.Export(_configuration, dialog.FileName);
            ProfileStatus.Text = "Configuration exported.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ProfileStatus.Text = $"Export failed: {ex.Message}";
        }
    }

    private void ExportActivityLog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Lumin activity log (*.jsonl)|*.jsonl", FileName = "lumin-activity.jsonl", AddExtension = true, DefaultExt = ".jsonl" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var count = _activityLogger.ExportTo(dialog.FileName);
            ActivityLogStatus.Text = $"Exported {count} log file(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActivityLogStatus.Text = $"Export failed: {ex.Message}";
        }
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Lumin configuration (*.json)|*.json", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = _configurationStore.Import(dialog.FileName);
            _configuration = imported;
            var selected = GetActiveProfile();
            RefreshProfileSelector();
            ApplyProfile(selected);
            PersistActiveProfile();
            ProfileStatus.Text = $"Imported {imported.Profiles.Count} profile(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            ProfileStatus.Text = $"Import rejected: {ex.Message}";
        }
    }

    private void BrowseModules_Click(object sender, RoutedEventArgs e)
    {
        // Navigation is selected by its Tag; find the matching item without coupling to visual order.
        var target = FindNavigation("Modules");
        if (target is not null) target.IsChecked = true;
        ModuleSearch.Focus();
    }

    private RadioButton? FindNavigation(string tag)
    {
        return FindVisualChildren<RadioButton>(this).FirstOrDefault(item => Equals(item.Tag, tag));
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }
}
