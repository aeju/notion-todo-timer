using System.IO;
using System.Text.Json;

namespace FocusBar;

public sealed class AppSettings
{
    public string NotionToken { get; set; } = "";

    // 오늘 할 일: '날짜' = 오늘인 행만 보여줌 / 장기: 미완료 전체
    public string TodayDatabaseId { get; set; } = "";
    public string LongTermDatabaseId { get; set; } = "";

    // 두 DB가 같은 속성 이름을 쓴다 (Notion에 보이는 이름 그대로)
    public string TitleProperty { get; set; } = "할 일";
    public string DueProperty { get; set; } = "마감";
    public string DoneProperty { get; set; } = "완료";
    public string DonePropertyType { get; set; } = "checkbox";  // "checkbox" 또는 "date"
    public string DateProperty { get; set; } = "날짜";          // 오늘 DB에서 그날을 표시하는 속성
    public string CategoryProperty { get; set; } = "영역";      // 선택 속성. 비우면 표시 안 함

    // 이 시각 이전은 전날로 본다 (새벽 작업이 전날 목록에 남도록)
    public int DayStartHour { get; set; } = 5;

    public int DefaultMinutes { get; set; } = 25;
    public int ExtendMinutes { get; set; } = 5;
    public int DialScaleMinutes { get; set; } = 60;

    public string LoadError { get; private set; } = "";

    public bool IsConfigured =>
        NotionToken.Length > 0 && !NotionToken.Contains("여기에") &&
        TodayDatabaseId.Length == 32;

    public bool HasLongTerm => LongTermDatabaseId.Length == 32;

    public DateTime CurrentDay => DateTime.Now.AddHours(-DayStartHour).Date;

    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new AppSettings { LoadError = "appsettings.json이 실행 파일 옆에 없습니다." };

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), options) ?? new AppSettings();
            settings.TodayDatabaseId = NormalizeId(settings.TodayDatabaseId);
            settings.LongTermDatabaseId = NormalizeId(settings.LongTermDatabaseId);
            return settings;
        }
        catch (JsonException ex)
        {
            return new AppSettings { LoadError = $"appsettings.json 형식 오류: {ex.Message}" };
        }
    }

    // DB 링크를 통째로 붙여넣어도 ID(32자리)만 뽑아낸다.
    private static string NormalizeId(string raw)
    {
        var s = raw.Trim();
        var q = s.IndexOf('?');
        if (q >= 0) s = s[..q];
        s = s[(s.LastIndexOf('/') + 1)..];
        var compact = s.Replace("-", "");
        return compact.Length > 32 ? compact[^32..] : compact;
    }
}
