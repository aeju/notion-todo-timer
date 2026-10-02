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
}
