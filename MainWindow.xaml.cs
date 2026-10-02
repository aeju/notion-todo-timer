using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using FocusBar.Models;
using FocusBar.ViewModels;

namespace FocusBar;

public partial class MainWindow : Window
{
    private double _normalHeight;

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
}
