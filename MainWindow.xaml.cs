using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusBar.Models;
using FocusBar.ViewModels;

namespace FocusBar;

public partial class MainWindow : Window
{
    private double _normalHeight;
    private Point _dragStart;
    private TodoItem? _dragCandidate;

    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel(AppSettings.Load());
        DataContext = vm;

        // 시간이 끝나면 최소화 상태여도 창을 다시 띄운다
        vm.TimerFinished += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        };

        // 미니 모드: 내용 높이에 맞춰 줄이고, 돌아오면 직접 맞춰둔 높이로 복원
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.IsMini)) return;
            if (vm.IsMini)
            {
                _normalHeight = ActualHeight;
                SizeToContent = SizeToContent.Height;
            }
            else
            {
                SizeToContent = SizeToContent.Manual;
                Height = _normalHeight;
            }
        };

        Loaded += async (_, _) => await vm.RefreshAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Opacity = Topmost ? 1.0 : 0.35;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // 이름 수정 입력칸이 나타나면 바로 입력할 수 있게 포커스 + 전체 선택
    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
            Dispatcher.InvokeAsync(() => { box.Focus(); box.SelectAll(); });
    }

    // 입력칸 밖을 누르면 저장 (Esc로 취소한 경우는 이미 편집이 끝나 있어 아무 일도 안 함)
    private async void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: TodoItem item } && DataContext is MainViewModel vm)
            await vm.CommitRenameAsync(item);
    }

    // ── 오늘 목록 드래그로 순서 바꾸기 ─────────────────
    // 버튼(○, ▶)이나 이름 수정 입력칸을 누른 경우는 드래그로 보지 않음

    private void Row_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        if (sender is not FrameworkElement { DataContext: TodoItem { Source: TaskSource.Today, IsEditing: false } item }) return;
        if (IsInside<ButtonBase>(e.OriginalSource) || IsInside<TextBox>(e.OriginalSource)) return;
        _dragCandidate = item;
        _dragStart = e.GetPosition(this);
    }

    private void Row_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate == null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - _dragStart;
        if (Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance &&
            Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance) return;

        var item = _dragCandidate;
        _dragCandidate = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(TodoItem), item), DragDropEffects.Move);
    }

    private void Row_DragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetData(typeof(TodoItem)) is TodoItem dragged &&
                 sender is FrameworkElement { DataContext: TodoItem target } &&
                 target.Source == TaskSource.Today && dragged.IsDone == target.IsDone;
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Row_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TodoItem)) is TodoItem dragged &&
            sender is FrameworkElement { DataContext: TodoItem target } &&
            DataContext is MainViewModel vm)
            vm.MoveToday(dragged, target);
    }

    private static bool IsInside<T>(object source) where T : DependencyObject
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T) return true;
        return false;
    }
}
