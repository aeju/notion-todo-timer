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

    // 오늘 DB에 새 행 추가 ('날짜' = day). 루틴에서 만들 때는 영역·루틴 연결도 같이 넣음
    public async Task<TodoItem> CreateTodayTaskAsync(string title, DateTime day, string category = "", string? routineId = null)
    {
        var props = new Dictionary<string, object>
        {
            [_s.TitleProperty] = new { title = new[] { new { text = new { content = title } } } },
            [_s.DateProperty] = new { date = new { start = day.ToString("yyyy-MM-dd") } }
        };
        if (category.Length > 0 && _s.CategoryProperty.Length > 0)
            props[_s.CategoryProperty] = new { select = new { name = category } };
        if (routineId != null && _s.RoutineRelationProperty.Length > 0)
            props[_s.RoutineRelationProperty] = new { relation = new[] { new { id = routineId } } };

        var body = new Dictionary<string, object>
        {
            ["parent"] = new { database_id = _s.TodayDatabaseId },
            ["properties"] = props
        };

        using var doc = await SendAsync(HttpMethod.Post, "pages", body);
        return ParsePage(doc.RootElement, TaskSource.Today);
    }

    // 하루 준비: ① 전날까지 미완료(루틴 제외)를 오늘로 이월 ② 오늘 요일 루틴을 할 일로 생성
    // 두 작업 모두 여러 번 실행해도 결과가 같다 (이월할 게 없거나, 이미 만든 루틴은 건너뜀)
    public async Task<(int Carried, int Created)> PrepareDayAsync(DateTime day)
    {
        var carried = 0;
        if (_s.CarryOver)
        {
            var filters = new List<object>
            {
                new Dictionary<string, object> { ["property"] = _s.DateProperty, ["date"] = new { before = day.ToString("yyyy-MM-dd") } },
                NotDoneFilter()
            };
            if (_s.RoutineRelationProperty.Length > 0)
                filters.Add(new Dictionary<string, object> { ["property"] = _s.RoutineRelationProperty, ["relation"] = new { is_empty = true } });

            var body = new Dictionary<string, object> { ["filter"] = new Dictionary<string, object> { ["and"] = filters } };
            foreach (var stale in await QueryAllAsync(_s.TodayDatabaseId, body, TaskSource.Today))
            {
                var patch = new Dictionary<string, object>
                {
                    ["properties"] = new Dictionary<string, object>
                    {
                        [_s.DateProperty] = new { date = new { start = day.ToString("yyyy-MM-dd") } }
                    }
                };
                using var _ = await SendAsync(HttpMethod.Patch, $"pages/{stale.Id}", patch);
                carried++;
            }
        }

        var created = 0;
        if (_s.HasRoutines)
        {
            var routines = await GetRoutinesForAsync(day);
            if (routines.Count > 0)
            {
                var already = (await GetTodayTasksAsync(day))
                    .SelectMany(t => t.RoutineIds).Select(NormalizeId).ToHashSet();
                foreach (var r in routines.Where(r => !already.Contains(NormalizeId(r.Id))))
                {
                    await CreateTodayTaskAsync(r.Name, day, r.Category, r.Id);
                    created++;
                }
            }
        }
        return (carried, created);
    }

    // 루틴 DB: 사용 = 켜짐 이고 요일에 오늘이 들어 있는 것
    private async Task<List<(string Id, string Name, string Category)>> GetRoutinesForAsync(DateTime day)
    {
        var dayName = "일월화수목금토"[(int)day.DayOfWeek].ToString();
        var body = new Dictionary<string, object>
        {
            ["filter"] = new Dictionary<string, object>
            {
                ["and"] = new object[]
                {
                    new Dictionary<string, object> { ["property"] = _s.RoutineEnabledProperty, ["checkbox"] = new { equals = true } },
                    new Dictionary<string, object> { ["property"] = _s.RoutineDaysProperty, ["multi_select"] = new { contains = dayName } }
                }
            },
            ["sorts"] = new[] { new Dictionary<string, object> { ["timestamp"] = "created_time", ["direction"] = "ascending" } }
        };

        var result = new List<(string, string, string)>();
        string? cursor = null;
        do
        {
            body["page_size"] = 100;
            if (cursor != null) body["start_cursor"] = cursor; else body.Remove("start_cursor");
            using var doc = await SendAsync(HttpMethod.Post, $"databases/{_s.RoutineDatabaseId}/query", body);
            var root = doc.RootElement;
            foreach (var page in root.GetProperty("results").EnumerateArray())
            {
                var props = page.GetProperty("properties");
                var name = ReadTitle(props, _s.RoutineNameProperty);
                if (name.Length == 0) continue;
                var (category, _) = ReadSelect(props, _s.CategoryProperty);
                result.Add((page.GetProperty("id").GetString()!, name, category));
            }
            cursor = NextCursor(root);
        } while (cursor != null);
        return result;
    }

    private static string NormalizeId(string id) => id.Replace("-", "");

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

            cursor = NextCursor(root);
        } while (cursor != null);

        return items;
    }

    private TodoItem ParsePage(JsonElement page, TaskSource source)
    {
        var props = page.GetProperty("properties");
        var title = ReadTitle(props, _s.TitleProperty);

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

        var (category, categoryColor) = ReadSelect(props, _s.CategoryProperty);

        var routineIds = new List<string>();
        if (_s.RoutineRelationProperty.Length > 0 &&
            props.TryGetProperty(_s.RoutineRelationProperty, out var rp) &&
            rp.TryGetProperty("relation", out var rel) && rel.ValueKind == JsonValueKind.Array)
            foreach (var r in rel.EnumerateArray())
                routineIds.Add(r.GetProperty("id").GetString()!);

        return new TodoItem
        {
            Id = page.GetProperty("id").GetString()!,
            Title = title.Length > 0 ? title : "(제목 없음)",
            Due = due,
            Source = source,
            IsDone = done,
            Category = category,
            CategoryColor = categoryColor,
            RoutineIds = routineIds
        };
    }

    private static string ReadTitle(JsonElement props, string name)
    {
        var sb = new StringBuilder();
        if (props.TryGetProperty(name, out var t) && t.TryGetProperty("title", out var parts))
            foreach (var part in parts.EnumerateArray())
                sb.Append(part.GetProperty("plain_text").GetString());
        return sb.ToString();
    }

    private static (string Name, string Color) ReadSelect(JsonElement props, string name)
    {
        if (name.Length > 0 && props.TryGetProperty(name, out var p) &&
            p.TryGetProperty("select", out var sel) && sel.ValueKind == JsonValueKind.Object)
        {
            var color = sel.TryGetProperty("color", out var c) ? c.GetString() ?? "default" : "default";
            return (sel.GetProperty("name").GetString() ?? "", color);
        }
        return ("", "default");
    }

    private static string? NextCursor(JsonElement root) =>
        root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True &&
        root.TryGetProperty("next_cursor", out var next) && next.ValueKind == JsonValueKind.String
            ? next.GetString()
            : null;

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
