using System.Windows;
using SoleKey.Models;
using SoleKey.ViewModels;
using SoleKey.Views;
using SoleKey.Infrastructure;

namespace SoleKey;

public partial class App : Application
{
    private readonly VaultManager vault = new();
    private readonly SettingsManager settings = new();
    private readonly ClipboardManager clipboard = new();
    private TrayController? tray;
    private InstanceActivation? instance;
    private MainViewModel? mainModel;
    private bool transitioning, restorePending;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        instance = new InstanceActivation("Local\\SoleKey." + user, () =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Restore);
        });
        if (!instance.IsPrimary) { Shutdown(); return; }
        tray = new TrayController(() => mainModel?.RequestLock(false), ExitFromTray);
        tray.Hidden += () => mainModel?.SetTrayState(true);
        tray.Restored += () => mainModel?.SetTrayState(false);
        ShowUnlock("");
    }
    private void Restore()
    {
        if (transitioning) { restorePending = true; return; }
        tray?.Restore();
    }
    private void Present(Window window, bool visible)
    {
        transitioning = false;
        visible |= restorePending; restorePending = false;
        if (visible) window.Show();
    }
    private void ShowUnlock(string notice, bool visible = true)
    {
        mainModel = null;
        var vm = new UnlockViewModel(vault, settings);
        if (notice.Length > 0) vm.Status = notice;
        var window = new UnlockWindow(vm); MainWindow = window;
        var switching = false;
        vm.Opened += snapshot =>
        {
            var show = window.IsVisible;
            switching = transitioning = true; tray?.Detach(); vm.Deactivate(); window.Close(); ShowMain(snapshot, show);
        };
        window.Closed += (_, _) => { vm.Deactivate(); if (!switching) Shutdown(); };
        tray?.Attach(window, false, () => !vm.Busy);
        Present(window, visible);
    }
    private void ShowMain(VaultData snapshot, bool visible = true)
    {
        var vm = new MainViewModel(vault, settings, clipboard, snapshot);
        mainModel = vm;
        var window = new MainWindow(vm); MainWindow = window;
        var locking = false;
        vm.LockRequested += async () =>
        {
            if (locking) return;
            var show = window.IsVisible;
            locking = transitioning = true; tray?.Detach(); window.Hide();
            foreach (Window child in window.OwnedWindows.Cast<Window>().ToArray()) child.Close();
            await vault.LockAsync(); window.Close(); ShowUnlock(vm.LockNotice, show);
        };
        window.Closed += (_, _) => { vm.Deactivate(); if (!locking) Shutdown(); };
        tray?.Attach(window, true);
        Present(window, visible);
        vm.SetTrayState(!window.IsVisible);
    }
    private void ExitFromTray()
    {
        if (transitioning) return;
        if (MainWindow is { } window)
        {
            foreach (var child in window.OwnedWindows.Cast<Window>().ToArray())
            {
                child.Close();
                if (child.IsVisible) return;
            }
            tray?.Detach(); window.Close();
        }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose(); clipboard.Dispose(); vault.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
