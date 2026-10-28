using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SoleKey.Infrastructure;

namespace SoleKey.Views;

internal static class WindowDialogs
{
    public static void Connect(Window window, ScreenModel vm)
    {
        window.SourceInitialized += (_, _) =>
        {
            const int cornerPreference = 33, round = 2;
            var preference = round;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
                DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, cornerPreference, ref preference, sizeof(int)) >= 0) return;
            window.Resources["WindowCornerRadius"] = new CornerRadius(0);
            window.Resources["WindowHeaderCornerRadius"] = new CornerRadius(0);
        };
        window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, e) => { e.Handled = true; window.Close(); }));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, e) => { e.Handled = true; window.WindowState = WindowState.Minimized; }));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, e) => { e.Handled = true; window.WindowState = WindowState.Maximized; }));
        window.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (_, e) => { e.Handled = true; window.WindowState = WindowState.Normal; }));
        vm.Confirm = message => MessageBox.Show(window, message, "SoleKey", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        vm.PickFile = save =>
        {
            const string filter = "SoleKey 암호화 금고|*.solekey;*.bak|모든 파일|*.*";
            if (save)
            {
                var dialog = new SaveFileDialog { Filter = filter, DefaultExt = ".solekey", FileName = "SoleKey.solekey", OverwritePrompt = true };
                return dialog.ShowDialog(window) == true ? dialog.FileName : null;
            }
            else
            {
                var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
                return dialog.ShowDialog(window) == true ? dialog.FileName : null;
            }
        };
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
