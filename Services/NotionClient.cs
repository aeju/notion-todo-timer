using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FocusBar.Models;

namespace FocusBar.Services;

public sealed class NotionClient
{
    private const string ApiVersion = "2022-06-28";
    private readonly HttpClient _http;
    private readonly AppSettings _s;

    public NotionClient(AppSettings settings)
    {
        _s = settings;
        _http = new HttpClient { BaseAddress = new Uri("https://api.notion.com/v1/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.NotionToken);
        _http.DefaultRequestHeaders.Add("Notion-Version", ApiVersion);
    }

    // 오늘 DB: '날짜' = day 인 행 전부 (완료 포함), 만든 순서대로
    public Task<List<TodoItem>> GetTodayTasksAsync(DateTime day)
    {
        var body = new Dictionary<string, object>
        {
            ["filter"] = new Dictionary<string, object>
            {
                ["property"] = _s.DateProperty,
                ["date"] = new { equals = day.ToString("yyyy-MM-dd") }
            },
            ["sorts"] = new[] { new Dictionary<string, object> { ["timestamp"] = "created_time", ["direction"] = "ascending" } }
        };
        return QueryAllAsync(_s.TodayDatabaseId, body, TaskSource.Today);
    }

    // 장기 DB: 미완료만, 마감 빠른 순
    public Task<List<TodoItem>> GetLongTermOpenTasksAsync()
    {
        var body = new Dictionary<string, object>
        {
            ["filter"] = NotDoneFilter(),
            ["sorts"] = new[] { new Dictionary<string, object> { ["property"] = _s.DueProperty, ["direction"] = "ascending" } }
        };
        return QueryAllAsync(_s.LongTermDatabaseId, body, TaskSource.LongTerm);
    }

    // 오늘 DB에 새 행 추가 ('날짜' = day)
    public async Task<TodoItem> CreateTodayTaskAsync(string title, DateTime day)
    {
        var body = new Dictionary<string, object>
        {
            ["parent"] = new { database_id = _s.TodayDatabaseId },
            ["properties"] = new Dictionary<string, object>
            {
                [_s.TitleProperty] = new { title = new[] { new { text = new { content = title } } } },
                [_s.DateProperty] = new { date = new { start = day.ToString("yyyy-MM-dd") } }
            }
        };

        using var doc = await SendAsync(HttpMethod.Post, "pages", body);
        return ParsePage(doc.RootElement, TaskSource.Today);
    }

    public async Task SetDoneAsync(string pageId, bool done)
    {
        object value = IsCheckboxDone
            ? new { checkbox = done }
            : done
                ? new { date = (object?)new { start = DateTime.Now.ToString("yyyy-MM-dd") } }
                : new { date = (object?)null };

        var body = new Dictionary<string, object>
        {
            ["properties"] = new Dictionary<string, object> { [_s.DoneProperty] = value }
        };

        using var _ = await SendAsync(HttpMethod.Patch, $"pages/{pageId}", body);
    }

    private bool IsCheckboxDone => _s.DonePropertyType.Equals("checkbox", StringComparison.OrdinalIgnoreCase);

    private object NotDoneFilter() => IsCheckboxDone
        ? new Dictionary<string, object> { ["property"] = _s.DoneProperty, ["checkbox"] = new { equals = false } }
        : new Dictionary<string, object> { ["property"] = _s.DoneProperty, ["date"] = new { is_empty = true } };

    // 100개 넘게 있어도 다음 페이지까지 이어서 가져온다
    private async Task<List<TodoItem>> QueryAllAsync(string databaseId, Dictionary<string, object> body, TaskSource source)
    {
        var items = new List<TodoItem>();
        string? cursor = null;
        do
        {
            body["page_size"] = 100;
            if (cursor != null) body["start_cursor"] = cursor; else body.Remove("start_cursor");

            using var doc = await SendAsync(HttpMethod.Post, $"databases/{databaseId}/query", body);
            var root = doc.RootElement;
            foreach (var page in root.GetProperty("results").EnumerateArray())
                items.Add(ParsePage(page, source));

            cursor = root.TryGetProperty("has_more", out var more) && more.GetBoolean() &&
                     root.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        } while (cursor != null);

        return items;
    }

    private TodoItem ParsePage(JsonElement page, TaskSource source)
    {
        var props = page.GetProperty("properties");

        var title = new StringBuilder();
        if (props.TryGetProperty(_s.TitleProperty, out var t) && t.TryGetProperty("title", out var parts))
            foreach (var part in parts.EnumerateArray())
                title.Append(part.GetProperty("plain_text").GetString());

        DateTime? due = null;
        if (props.TryGetProperty(_s.DueProperty, out var d) &&
            d.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.Object &&
            DateTime.TryParse(date.GetProperty("start").GetString(), out var parsed))
            due = parsed;

        var done = false;
        if (props.TryGetProperty(_s.DoneProperty, out var dp))
        {
            done = IsCheckboxDone
                ? dp.TryGetProperty("checkbox", out var cb) && cb.ValueKind == JsonValueKind.True
                : dp.TryGetProperty("date", out var dd) && dd.ValueKind == JsonValueKind.Object;
        }

        string category = "", categoryColor = "default";
        if (_s.CategoryProperty.Length > 0 &&
            props.TryGetProperty(_s.CategoryProperty, out var cp) &&
            cp.TryGetProperty("select", out var sel) && sel.ValueKind == JsonValueKind.Object)
        {
            category = sel.GetProperty("name").GetString() ?? "";
            if (sel.TryGetProperty("color", out var col)) categoryColor = col.GetString() ?? "default";
        }

        return new TodoItem
        {
            Id = page.GetProperty("id").GetString()!,
            Title = title.Length > 0 ? title.ToString() : "(제목 없음)",
            Due = due,
            Source = source,
            IsDone = done,
            Category = category,
            CategoryColor = categoryColor
        };
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            var message = text;
            try { message = JsonDocument.Parse(text).RootElement.GetProperty("message").GetString() ?? text; }
            catch (Exception) { }
            throw new InvalidOperationException($"Notion {(int)response.StatusCode}: {message}");
        }
        return JsonDocument.Parse(text);
    }
}
