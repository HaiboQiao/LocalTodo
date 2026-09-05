using CommunityToolkit.Mvvm.ComponentModel;
using LocalTodo.Helpers;
using LocalTodo.Models;

namespace LocalTodo.ViewModels;

public sealed partial class WeeklyPlanCardViewModel(
    WeeklyPlanItem item) :
    ObservableObject
{
    public WeeklyPlanItem Item
    { get; } = item;

    [ObservableProperty]
    private double left;

    [ObservableProperty]
    private double top;

    [ObservableProperty]
    private double width;

    [ObservableProperty]
    private double height;

    [ObservableProperty]
    private bool isCompact;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isPlacementValid = true;

    public void Apply(
        WeeklyPlanCardPlacement placement)
    {
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        IsCompact = placement.IsCompact;
    }
}
