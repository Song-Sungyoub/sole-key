using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using SoleKey.ViewModels;

namespace SoleKey.Views;
public partial class MainWindow : Window
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MainViewModel vm;
    public MainWindow(MainViewModel vm)
    {
        this.vm = vm; InitializeComponent(); DataContext = vm; WindowDialogs.Connect(this, vm);
        Loaded += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var shift = new TranslateTransform(); MainContent.RenderTransform = shift;
            var duration = TimeSpan.FromMilliseconds(240);
            MainContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { FillBehavior = FillBehavior.Stop });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        vm.EditRequested += OpenEditor; vm.SettingsRequested += OpenSettings;
        InputManager.Current.PreProcessInput += InputReceived;
        SystemEvents.SessionSwitch += SessionChanged;
        timer.Tick += Tick; timer.Start();
        Closed += (_, _) =>
        {
            timer.Stop(); timer.Tick -= Tick; InputManager.Current.PreProcessInput -= InputReceived;
            SystemEvents.SessionSwitch -= SessionChanged; vm.EditRequested -= OpenEditor; vm.SettingsRequested -= OpenSettings;
        };
    }
    private void OpenEditor(AccountEditViewModel editor) => new AccountEditWindow(editor) { Owner = this }.ShowDialog();
    private void OpenSettings(VaultSettingsViewModel settings) => new VaultSettingsWindow(settings) { Owner = this }.ShowDialog();
    private void Tick(object? sender, EventArgs e) => vm.CheckIdle();
    private void InputReceived(object sender, PreProcessInputEventArgs e)
    {
        if (Application.Current.Windows.Cast<Window>().Any(w => w.IsActive) &&
            (e.StagingItem.Input is KeyEventArgs or MouseButtonEventArgs or MouseWheelEventArgs ||
             e.StagingItem.Input.RoutedEvent == Mouse.MouseMoveEvent)) vm.RecordInput();
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) Dispatcher.Invoke(() => vm.RequestLock(true));
    }
}
