using FocusBar.ViewModels;

namespace FocusBar.Models;

public enum TaskSource { Today, LongTerm }

public sealed class TodoItem : ObservableObject
{
    private bool _isDone;

    public required string Id { get; init; }
    public required string Title { get; init; }
    public DateTime? Due { get; init; }
    public TaskSource Source { get; init; }

    public bool IsDone
    {
        get => _isDone;
        set { if (Set(ref _isDone, value)) OnPropertyChanged(nameof(DoneMark)); }
    }

    public string DoneMark => IsDone ? "●" : "○";
    public string DueText => Due is { } d ? $"~{d:MM/dd}" : "";
}
