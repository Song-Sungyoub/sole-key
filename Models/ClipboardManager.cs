using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace SoleKey.Models;

public sealed class ClipboardManager : IDisposable
{
    private readonly DispatcherTimer timer = new();
    private byte[]? digest;
    private uint sequence;
    private int retries;
    public ClipboardManager() => timer.Tick += Tick;
    public void Copy(string value, int seconds, bool sensitive)
    {
        Clipboard.SetText(value);
        timer.Stop(); ClearDigest();
        if (!sensitive) return;
        digest = Hash(value); sequence = GetClipboardSequenceNumber(); retries = 0;
        timer.Interval = TimeSpan.FromSeconds(seconds); timer.Start();
    }
    public void ClearOwned()
    {
        timer.Stop();
        try
        {
            if (digest is not null && sequence == GetClipboardSequenceNumber() && Clipboard.ContainsText())
            {
                var current = Hash(Clipboard.GetText());
                try { if (CryptographicOperations.FixedTimeEquals(digest, current)) Clipboard.Clear(); }
                finally { CryptographicOperations.ZeroMemory(current); }
            }
            ClearDigest();
        }
        catch (ExternalException)
        {
            if (++retries <= 5) { timer.Interval = TimeSpan.FromSeconds(1); timer.Start(); }
            else ClearDigest();
        }
    }
    private static byte[] Hash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try { return SHA256.HashData(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void ClearDigest() { if (digest is not null) CryptographicOperations.ZeroMemory(digest); digest = null; }
    private void Tick(object? sender, EventArgs e) => ClearOwned();
    public void Dispose() { ClearOwned(); timer.Stop(); timer.Tick -= Tick; ClearDigest(); }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
