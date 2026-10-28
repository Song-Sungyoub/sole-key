using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SoleKey.Views;

public sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly Forms.ToolStripItem lockItem;
    private readonly Drawing.Icon image;
    private Window? window;
    private Func<bool> canHide = () => true;
    public event Action? Restored;
    public event Action? Hidden;
    public TrayController(Action lockVault, Action exit)
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/SoleKey;component/Assets/Icon/SoleKey.ico"))!.Stream;
        using var source = new Drawing.Icon(stream);
        image = (Drawing.Icon)source.Clone();
        menu.Items.Add("SoleKey 열기", null, (_, _) => Restore());
        lockItem = menu.Items.Add("금고 잠금", null, (_, _) => lockVault());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = image, Text = "SoleKey", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => Restore();
    }
    public void Attach(Window value, bool unlocked, Func<bool>? allowHide = null)
    {
        Detach(); window = value; canHide = allowHide ?? (() => true);
        lockItem.Enabled = unlocked;
        window.Closing += Closing; window.StateChanged += StateChanged;
    }
    public void Detach()
    {
        if (window is null) return;
        window.Closing -= Closing; window.StateChanged -= StateChanged; window = null;
    }
    private void Closing(object? sender, CancelEventArgs e)
    {
        if (e.Cancel || !canHide()) return;
        e.Cancel = true; Hide();
    }
    private void StateChanged(object? sender, EventArgs e)
    {
        if (window?.WindowState == WindowState.Minimized && canHide()) Hide();
    }
    public void Hide()
    {
        if (window is null || !canHide()) return;
        var child = window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible);
        if (child is not null) { child.Activate(); return; }
        window.Hide();
        Hidden?.Invoke();
    }
    public void Restore()
    {
        if (window is null) return;
        Restored?.Invoke();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show(); window.Activate();
        window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible)?.Activate();
    }
    public void Dispose()
    {
        Detach(); icon.Visible = false; icon.Dispose(); menu.Dispose(); image.Dispose();
    }
}
