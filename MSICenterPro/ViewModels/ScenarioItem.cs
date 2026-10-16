using MaterialDesignThemes.Wpf;
using MSICenterPro.Core;

namespace MSICenterPro.ViewModels;

public class ScenarioItem : ObservableObject
{
    private bool _isSelected;

    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public PackIconKind Icon { get; init; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}
