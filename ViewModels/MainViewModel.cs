using System.Collections.ObjectModel;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FocusBar.Models;
using FocusBar.Services;

namespace FocusBar.ViewModels;

public enum TimerState { Idle, Running, Finished }

public sealed class MainViewModel : ObservableObject
{
    private static readonly CultureInfo Ko = new("ko-KR");

    private readonly AppSettings _settings;
    private readonly NotionClient? _notion;
    private readonly DispatcherTimer _ticker;
    private readonly DispatcherTimer _dayWatcher;

    private DateTime _loadedDay;
    private DateTime _endAt;
    private TimerState _state = TimerState.Idle;
    private TodoItem? _activeTask;
    private TimeSpan _remaining;
    private string _newTaskTitle = "";
    private int _minutes;
    private string _status = "";
    private bool _isMini;
    private bool _isBusy;

    public event EventHandler? TimerFinished;

    public ObservableCollection<TodoItem> TodayTasks { get; } = new();
    public ObservableCollection<TodoItem> LongTermTasks { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand ToggleDoneCommand { get; }
    public ICommand BeginRenameCommand { get; }
    public ICommand CommitRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand FinishDoneCommand { get; }
    public ICommand ExtendCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ToggleMiniCommand { get; }

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _minutes = settings.DefaultMinutes;
        _loadedDay = settings.CurrentDay;

        if (settings.LoadError.Length > 0) _status = settings.LoadError;
        else if (!settings.IsConfigured) _status = "appsettings.json에 NotionToken과 TodayDatabaseId를 넣어주세요.";
        else _notion = new NotionClient(settings);

        // 남은 시간은 종료 시각에서 역산한다 (틱 누적 오차 없음)
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += (_, _) => OnTick();

        // 켜둔 채로 날이 바뀌면(DayStartHour 기준) 오늘 목록을 다시 불러온다
        _dayWatcher = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _dayWatcher.Tick += async (_, _) =>
        {
            if (_settings.CurrentDay != _loadedDay) await RefreshAsync();
        };
        _dayWatcher.Start();

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => _notion != null && !_isBusy);
        AddCommand = new RelayCommand(async _ => await AddAsync(), _ => _notion != null && !_isBusy && !string.IsNullOrWhiteSpace(NewTaskTitle));
        StartCommand = new RelayCommand(p => Start((TodoItem)p!), p => p is TodoItem { IsDone: false } && Minutes > 0);
        ToggleDoneCommand = new RelayCommand(async p => await ToggleDoneAsync((TodoItem)p!), p => p is TodoItem && !_isBusy);
        BeginRenameCommand = new RelayCommand(p => BeginRename((TodoItem)p!), p => p is TodoItem && !_isBusy);
        CommitRenameCommand = new RelayCommand(async p => await CommitRenameAsync((TodoItem)p!), p => p is TodoItem);
        CancelRenameCommand = new RelayCommand(p => { if (p is TodoItem t) t.IsEditing = false; });
        DeleteCommand = new RelayCommand(async p => await DeleteAsync((TodoItem)p!), p => p is TodoItem && !_isBusy);
        FinishDoneCommand = new RelayCommand(async _ => await FinishActiveAsync(), _ => _activeTask != null && !_isBusy);
        ExtendCommand = new RelayCommand(_ => Extend(), _ => State != TimerState.Idle);
        StopCommand = new RelayCommand(_ => ResetTimer(), _ => State != TimerState.Idle);
        ToggleMiniCommand = new RelayCommand(_ => IsMini = !IsMini);
    }

    // ── 바인딩 속성 ─────────────────────────────

    public string NewTaskTitle { get => _newTaskTitle; set => Set(ref _newTaskTitle, value); }
    public int Minutes { get => _minutes; set => Set(ref _minutes, Math.Clamp(value, 1, 180)); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ExtendLabel => $"+{_settings.ExtendMinutes}분";
    public bool HasLongTerm => _settings.HasLongTerm;

    public string TodayHeader
    {
        get
        {
            var done = TodayTasks.Count(t => t.IsDone);
            return $"오늘 {_loadedDay.ToString("M/d (ddd)", Ko)} · {done}/{TodayTasks.Count}";
        }
    }

    public string LongTermHeader => $"장기 ({LongTermTasks.Count})";

    public bool IsMini
    {
        get => _isMini;
        set
        {
            if (!Set(ref _isMini, value)) return;
            OnPropertyChanged(nameof(IsListVisible));
            OnPropertyChanged(nameof(DialSize));
        }
    }

    public bool IsListVisible => !IsMini;
    public double DialSize => IsMini ? 40 : 72;

    public TimerState State
    {
        get => _state;
        private set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsTimerVisible));
            OnPropertyChanged(nameof(IsFinished));
            OnPropertyChanged(nameof(RemainingText));
        }
    }

    public bool IsTimerVisible => State != TimerState.Idle;
    public bool IsFinished => State == TimerState.Finished;
    public string ActiveTitle => _activeTask?.Title ?? "";

    public string RemainingText
    {
        get
        {
            if (State == TimerState.Finished) return "시간 종료";
            var secs = (int)Math.Ceiling(Math.Max(0, _remaining.TotalSeconds));
            return $"{secs / 60:00}:{secs % 60:00}";
        }
    }

    // 타임타이머처럼 60분 원판 기준 남은 비율
    public double DialFraction => Math.Clamp(_remaining.TotalMinutes / _settings.DialScaleMinutes, 0, 1);

    // ── Notion ─────────────────────────────

    public async Task RefreshAsync()
    {
        if (_notion == null) return;
        await RunAsync(async () =>
        {
            Status = "불러오는 중…";
            _loadedDay = _settings.CurrentDay;

            // 이 PC에서 오늘 처음이면 이월 + 루틴 생성 (루틴을 지웠을 때 다시 생기지 않도록 하루 한 번만)
            var prepared = "";
            if (DayState.LastPrepared() != _loadedDay)
            {
                var (carried, created) = await _notion.PrepareDayAsync(_loadedDay);
                DayState.MarkPrepared(_loadedDay);
                if (carried + created > 0) prepared = $" · 이월 {carried} · 루틴 {created}";
            }

            var today = await _notion.GetTodayTasksAsync(_loadedDay);
            Replace(TodayTasks, today.OrderBy(t => t.IsDone));

            if (_settings.HasLongTerm)
                Replace(LongTermTasks, await _notion.GetLongTermOpenTasksAsync());

            RaiseHeaders();
            Status = $"{DateTime.Now:HH:mm} 갱신{prepared}";
        });
    }

    private async Task AddAsync()
    {
        var title = NewTaskTitle.Trim();
        if (_notion == null || title.Length == 0) return;
        await RunAsync(async () =>
        {
            var item = await _notion.CreateTodayTaskAsync(title, _loadedDay);
            TodayTasks.Add(item);
            NewTaskTitle = "";
            RaiseHeaders();
            Status = $"추가: {item.Title}";
        });
    }

    // 오늘·장기 모두 체크/해제 토글. 체크한 항목은 취소선으로 남음 (장기는 새로고침 시 빠짐)
    private async Task ToggleDoneAsync(TodoItem item)
    {
        if (_notion == null) return;
        var target = !item.IsDone;
        await RunAsync(async () =>
        {
            await _notion.SetDoneAsync(item.Id, target);
            ApplyDone(item, target);
        });
    }

    // ── 우클릭: 이름 수정 / 삭제 ─────────────────

    private static void BeginRename(TodoItem item)
    {
        item.EditText = item.Title;
        item.IsEditing = true;
    }

    // Enter 또는 입력칸 밖을 누르면 저장. 내용이 그대로거나 비었으면 저장하지 않음
    public async Task CommitRenameAsync(TodoItem item)
    {
        if (!item.IsEditing) return;          // Enter 후 포커스가 빠질 때 두 번 저장되지 않도록
        item.IsEditing = false;

        var title = item.EditText.Trim();
        if (_notion == null || title.Length == 0 || title == item.Title) return;

        var before = item.Title;
        item.Title = title;
        if (ReferenceEquals(item, _activeTask)) OnPropertyChanged(nameof(ActiveTitle));
        await RunAsync(async () =>
        {
            try { await _notion.RenameAsync(item.Id, title); Status = $"이름 수정: {title}"; }
            catch { item.Title = before; throw; }   // 실패하면 원래 이름으로 되돌림
        });
    }

    private async Task DeleteAsync(TodoItem item)
    {
        if (_notion == null) return;
        var answer = MessageBox.Show(Application.Current.MainWindow,
            $"'{item.Title}'을(를) 삭제할까요?\nNotion 휴지통으로 이동하고, 30일 안에 복구할 수 있어요.",
            "삭제", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK) return;

        await RunAsync(async () =>
        {
            await _notion.DeleteAsync(item.Id);
            TodayTasks.Remove(item);
            LongTermTasks.Remove(item);
            if (ReferenceEquals(item, _activeTask)) ResetTimer();
            RaiseHeaders();
            Status = $"삭제: {item.Title}";
        });
    }

    // 타이머의 "끝" 버튼
    private async Task FinishActiveAsync()
    {
        var item = _activeTask;
        if (_notion == null || item == null) return;
        await RunAsync(async () =>
        {
            if (!item.IsDone) await _notion.SetDoneAsync(item.Id, true);
            ApplyDone(item, true);
            ResetTimer();
        });
    }

    private void ApplyDone(TodoItem item, bool done)
    {
        item.IsDone = done;
        MoveByDone(item.Source == TaskSource.Today ? TodayTasks : LongTermTasks, item);
        if (done && ReferenceEquals(item, _activeTask)) ResetTimer();
        RaiseHeaders();
        Status = done ? $"끝: {item.Title}" : $"되돌림: {item.Title}";
    }

    // 완료한 항목은 맨 아래로, 체크 해제한 항목은 완료 항목들 바로 위로
    private static void MoveByDone(ObservableCollection<TodoItem> list, TodoItem item)
    {
        var from = list.IndexOf(item);
        if (from < 0) return;
        var to = item.IsDone
            ? list.Count - 1
            : list.Count(t => !t.IsDone && !ReferenceEquals(t, item));
        if (from != to) list.Move(from, to);
    }

    private void RaiseHeaders()
    {
        OnPropertyChanged(nameof(TodayHeader));
        OnPropertyChanged(nameof(LongTermHeader));
    }

    private static void Replace(ObservableCollection<TodoItem> target, IEnumerable<TodoItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private async Task RunAsync(Func<Task> action)
    {
        _isBusy = true;
        CommandManager.InvalidateRequerySuggested();
        try { await action(); }
        catch (Exception ex) { Status = ex.Message; }
        finally
        {
            _isBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ── 타이머 ─────────────────────────────

    private void Start(TodoItem item)
    {
        _activeTask = item;
        OnPropertyChanged(nameof(ActiveTitle));
        _endAt = DateTime.Now.AddMinutes(Minutes);
        State = TimerState.Running;
        UpdateRemaining();
        _ticker.Start();
    }

    private void Extend()
    {
        var from = State == TimerState.Finished ? DateTime.Now : _endAt;
        _endAt = from.AddMinutes(_settings.ExtendMinutes);
        State = TimerState.Running;
        UpdateRemaining();
        _ticker.Start();
    }

    private void ResetTimer()
    {
        _ticker.Stop();
        _activeTask = null;
        _remaining = TimeSpan.Zero;
        State = TimerState.Idle;
        OnPropertyChanged(nameof(ActiveTitle));
        OnPropertyChanged(nameof(DialFraction));
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnTick()
    {
        UpdateRemaining();
        if (State != TimerState.Running || _remaining > TimeSpan.Zero) return;

        _ticker.Stop();
        State = TimerState.Finished;
        CommandManager.InvalidateRequerySuggested();
        PlayAlarm();
        TimerFinished?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateRemaining()
    {
        _remaining = _endAt - DateTime.Now;
        if (_remaining < TimeSpan.Zero) _remaining = TimeSpan.Zero;
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(DialFraction));
    }

    private static async void PlayAlarm()
    {
        for (var i = 0; i < 3; i++)
        {
            SystemSounds.Exclamation.Play();
            await Task.Delay(700);
        }
    }
}
