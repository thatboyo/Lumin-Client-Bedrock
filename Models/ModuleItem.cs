using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lumin.Models;

public sealed class ModuleItem : INotifyPropertyChanged
{
    private bool _isEnabled;
    private bool _isSelected;

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Availability { get; init; }
    public bool IsExperimental { get; init; }
    public bool CanEnable { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { if (_isEnabled == value) return; _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateLabel)); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    public string StateLabel => IsEnabled ? "ENABLED" : CanEnable ? "DISABLED" : "UNAVAILABLE";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new(propertyName));
}
