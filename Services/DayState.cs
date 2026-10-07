using System.IO;

namespace FocusBar.Services;

// 하루 준비(이월·루틴 생성)를 마친 날짜를 PC에 기록한다.
// %LOCALAPPDATA%\FocusBar\last-prepared.txt
public static class DayState
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusBar", "last-prepared.txt");

    public static DateTime? LastPrepared()
    {
        try
        {
            return File.Exists(FilePath) && DateTime.TryParse(File.ReadAllText(FilePath).Trim(), out var d) ? d.Date : null;
        }
        catch (IOException) { return null; }
    }

    public static void MarkPrepared(DateTime day)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, day.ToString("yyyy-MM-dd"));
        }
        catch (IOException) { }
    }

    // 드래그로 바꾼 오늘 순서 (페이지 ID 목록). 날짜가 다르면 무시 → 매일 영역별 기본 순서로 시작
    private static readonly string OrderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusBar", "today-order.txt");

    public static List<string>? LoadOrder(DateTime day)
    {
        try
        {
            if (!File.Exists(OrderPath)) return null;
            var lines = File.ReadAllLines(OrderPath);
            if (lines.Length == 0 || lines[0] != day.ToString("yyyy-MM-dd")) return null;
            return lines.Skip(1).Where(l => l.Length > 0).ToList();
        }
        catch (IOException) { return null; }
    }

    public static void SaveOrder(DateTime day, IEnumerable<string> ids)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OrderPath)!);
            File.WriteAllLines(OrderPath, new[] { day.ToString("yyyy-MM-dd") }.Concat(ids));
        }
        catch (IOException) { }
    }
}
