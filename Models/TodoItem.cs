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
    public string Category { get; init; } = "";
    public string CategoryColor { get; init; } = "default";   // Notion 선택 옵션 색 이름

    public bool HasCategory => Category.Length > 0;
    public string CategoryBackground => NotionColors.Background(CategoryColor);
    public string CategoryForeground => NotionColors.Foreground(CategoryColor);

    public bool IsDone
    {
        get => _isDone;
        set { if (Set(ref _isDone, value)) OnPropertyChanged(nameof(DoneMark)); }
    }

    public string DoneMark => IsDone ? "●" : "○";
    public string DueText => Due is { } d ? $"~{d:MM/dd}" : "";
}

// Notion 태그 색과 비슷한 값. Notion에서 옵션 색을 바꾸면 여기도 따라 바뀐다.
public static class NotionColors
{
    private static readonly Dictionary<string, (string Bg, string Fg)> Map = new()
    {
        ["default"] = ("#E8E7E4", "#37352F"),
        ["gray"]    = ("#E3E2E0", "#32302C"),
        ["brown"]   = ("#EEE0DA", "#442A1E"),
        ["orange"]  = ("#FADEC9", "#49290E"),
        ["yellow"]  = ("#FDECC8", "#402C1B"),
        ["green"]   = ("#DBEDDB", "#1C3829"),
        ["blue"]    = ("#D3E5EF", "#183347"),
        ["purple"]  = ("#E8DEEE", "#412454"),
        ["pink"]    = ("#F5E0E9", "#4C2337"),
        ["red"]     = ("#FFE2DD", "#5D1715"),
    };

    public static string Background(string color) => (Map.TryGetValue(color, out var c) ? c : Map["default"]).Bg;
    public static string Foreground(string color) => (Map.TryGetValue(color, out var c) ? c : Map["default"]).Fg;
}
