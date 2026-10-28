using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SoleKey;
using SoleKey.Models;
using SoleKey.ViewModels;
using SoleKey.Views;
using SoleKey.Infrastructure;

internal static class Program
{
    private static int count;
    private const string Master = "SoleKey-example-master-2026";
    private const string NextMaster = "SoleKey-replacement-master-2026";
    [STAThread]
    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "SoleKey-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ModelChecks(root);
            UiChecks(root);
            Console.WriteLine($"PASS {count} checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Application.Current?.Shutdown();
            // Only delete the exact test directory created above, never user vaults.
            Directory.Delete(root, true);
        }
    }
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL " + name); count++; Console.WriteLine("PASS " + name); }
    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { Check(true, name); return; }
        throw new Exception("FAIL " + name);
    }
    private static AccountEntry Example(bool useId = true) => new()
    {
        ServiceName = "검증용 사이트", UseLoginId = useId, LoginId = useId ? "example-id" : "", Memo = "검증용 메모",
        Passwords = [new() { Label = "비밀번호", Value = "  EXAMPLE-ONLY-한글🔑  " }, new() { Label = "2차 비밀번호", Value = "EXAMPLE-ONLY-SECOND" }]
    };
    private static void ModelChecks(string root)
    {
        var path = Path.Combine(root, "test.solekey"); var backup = Path.Combine(root, "backup.solekey");
        using var vault = new VaultManager();
        var watch = Stopwatch.StartNew();
        var data = vault.OpenAsync(path, Master, true).GetAwaiter().GetResult();
        Console.WriteLine($"Argon2id + create: {watch.ElapsedMilliseconds} ms");
        Check(data.Entries.Count == 0, "create empty vault");
        data.Entries.Add(Example()); data.Entries.Add(Example(false));
        data = vault.SaveAsync(data).GetAwaiter().GetResult();
        var bytes = File.ReadAllBytes(path); var text = File.ReadAllText(path);
        Check(!text.Contains("EXAMPLE-ONLY") && !text.Contains("example-id") && !text.Contains("검증용"), "no plaintext on disk");
        using (var second = new VaultManager()) Throws<IOException>(() => second.OpenAsync(path, Master, false).GetAwaiter().GetResult(), "exclusive vault writer");
        vault.BackupAsync(backup).GetAwaiter().GetResult();
        Throws<IOException>(() => vault.BackupAsync(backup).GetAwaiter().GetResult(), "backup never overwrites");
        var invalid = VaultData.Clone(data); invalid.Entries[0].Passwords.Add(new() { Value = "" });
        Throws<ArgumentException>(() => vault.SaveAsync(invalid).GetAwaiter().GetResult(), "reject empty added password");
        Check(File.ReadAllBytes(path).SequenceEqual(bytes), "validation failure preserves original");
        using (var blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Throws<IOException>(() => vault.SaveAsync(data).GetAwaiter().GetResult(), "atomic replace blocked by reader fails safely");
        Check(File.ReadAllBytes(path).SequenceEqual(bytes), "failed atomic replacement preserves old bytes");
        var invalidId = VaultData.Clone(data); invalidId.Entries[0].LoginId = "";
        Throws<ArgumentException>(() => vault.SaveAsync(invalidId).GetAwaiter().GetResult(), "ID required when enabled");
        vault.LockAsync().GetAwaiter().GetResult();
        Throws<InvalidOperationException>(() => vault.SaveAsync(data).GetAwaiter().GetResult(), "cannot save locked vault");
        Throws<CryptographicException>(() => vault.OpenAsync(path, "wrong password", false).GetAwaiter().GetResult(), "wrong master password rejected");
        data = vault.OpenAsync(path, Master, false).GetAwaiter().GetResult();
        Check(data.Entries[0].Passwords.Count == 2 && data.Entries[0].Passwords[0].Value == "  EXAMPLE-ONLY-한글🔑  ", "multiple passwords and whitespace unicode roundtrip");
        Check(!data.Entries[1].UseLoginId && data.Entries[1].LoginId == "", "password-only account roundtrip");
        vault.SaveAsync(data).GetAwaiter().GetResult();
        var secondBytes = File.ReadAllBytes(path);
        var firstHeader = CryptoManager.Parse(bytes); var secondHeader = CryptoManager.Parse(secondBytes);
        Check(firstHeader.Nonce != secondHeader.Nonce && !bytes.SequenceEqual(secondBytes), "fresh nonce on every save");
        var key = CryptoManager.Derive(Master, secondHeader);
        try
        {
            var altered = VaultData.Clone(secondHeader); var ct = Convert.FromBase64String(altered.Ciphertext); ct[0] ^= 1; altered.Ciphertext = Convert.ToBase64String(ct);
            Throws<CryptographicException>(() => CryptoManager.Decrypt(altered, key), "ciphertext tamper rejected");
            altered = VaultData.Clone(secondHeader); altered.Iterations++;
            Throws<CryptographicException>(() => CryptoManager.Decrypt(altered, key), "AAD header tamper rejected");
            altered = VaultData.Clone(secondHeader); altered.Tag = Convert.ToBase64String(new byte[16]);
            Throws<CryptographicException>(() => CryptoManager.Decrypt(altered, key), "tag tamper rejected");
            var node = JsonNode.Parse(secondBytes)!.AsObject(); node.Remove("kdf");
            Throws<JsonException>(() => CryptoManager.Parse(JsonSerializer.SerializeToUtf8Bytes(node)), "missing required header rejected");
            node = JsonNode.Parse(secondBytes)!.AsObject(); node["memoryKiB"] = int.MaxValue;
            Throws<InvalidDataException>(() => CryptoManager.Parse(JsonSerializer.SerializeToUtf8Bytes(node)), "KDF allocation bound before derivation");
            node = JsonNode.Parse(secondBytes)!.AsObject(); node["nonce"] = "not base64";
            Throws<FormatException>(() => CryptoManager.Parse(JsonSerializer.SerializeToUtf8Bytes(node)), "malformed base64 rejected");
            Throws<InvalidDataException>(() => CryptoManager.Parse(new byte[CryptoManager.MaxFileBytes + 1]), "oversized vault rejected");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        Throws<CryptographicException>(() => vault.ChangePasswordAsync("wrong", NextMaster).GetAwaiter().GetResult(), "password change authenticates current password");
        Check(File.ReadAllBytes(path).SequenceEqual(secondBytes), "failed password change preserves file");
        vault.ChangePasswordAsync(Master, NextMaster).GetAwaiter().GetResult();
        vault.LockAsync().GetAwaiter().GetResult();
        Throws<CryptographicException>(() => vault.OpenAsync(path, Master, false).GetAwaiter().GetResult(), "old master no longer opens current vault");
        data = vault.OpenAsync(path, NextMaster, false).GetAwaiter().GetResult();
        Check(data.Entries.Count == 2, "new master opens preserved data");
        var beforeRestore = File.ReadAllBytes(path);
        Throws<CryptographicException>(() => vault.RestoreAsync(backup, "wrong").GetAwaiter().GetResult(), "invalid restore password rejected");
        Check(File.ReadAllBytes(path).SequenceEqual(beforeRestore), "failed restore preserves original");
        vault.RestoreAsync(backup, Master).GetAwaiter().GetResult();
        Check(Directory.GetFiles(root, "*.bak").Length == 1, "restore creates pre-restore encrypted backup");
        data = vault.OpenAsync(path, Master, false).GetAwaiter().GetResult();
        Check(data.Entries.Count == 2, "restore reopens with backup master");
        var external = File.ReadAllBytes(path); external[^2] ^= 1; File.WriteAllBytes(path, external);
        Throws<IOException>(() => vault.SaveAsync(data).GetAwaiter().GetResult(), "external file changes cannot be overwritten");
        Check(File.ReadAllBytes(path).SequenceEqual(external), "external modification preserved on save failure");
        vault.LockAsync().GetAwaiter().GetResult();
        File.WriteAllBytes(path, bytes);
        Throws<IOException>(() => vault.OpenAsync(path, Master, true).GetAwaiter().GetResult(), "create never overwrites existing vault");
        Check(File.ReadAllBytes(path).SequenceEqual(bytes), "existing vault unchanged on create failure");
        Check(Directory.GetFiles(root, "*.tmp").Length == 0, "no temporary files after operations");
        data = vault.OpenAsync(path, Master, false).GetAwaiter().GetResult();
        var gate = typeof(VaultManager).GetField("gate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(vault)!;
        Task<VaultData> queuedSave; Task queuedLock;
        lock (gate) { queuedSave = vault.SaveAsync(data); queuedLock = vault.LockAsync(); }
        queuedLock.GetAwaiter().GetResult();
        vault.OpenAsync(Path.Combine(root, "new-session.solekey"), Master, true).GetAwaiter().GetResult();
        Throws<InvalidOperationException>(() => queuedSave.GetAwaiter().GetResult(), "queued old-session save cancelled by lock");
        data = vault.OpenAsync(Path.Combine(root, "new-session.solekey"), Master, false).GetAwaiter().GetResult();
        Check(data.Entries.Count == 0, "stale save cannot overwrite new vault session");
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
    private static void WaitUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done()) { if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("UI operation timed out"); Pump(); Thread.Sleep(10); }
        Pump();
    }
    private static void Render(Window window, string name)
    {
        var directory = PreviewDirectory;
        Directory.CreateDirectory(directory);
        var content = (FrameworkElement?)window.Template?.FindName("Frame", window) ?? (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            context.DrawRectangle(window.Background ?? Brushes.White, null, bounds);
            context.DrawRectangle(new VisualBrush(content), null, bounds);
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
    private static string PreviewDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SoleKey.csproj"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("SoleKey source root"), "artifacts", "preview");
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void UiChecks(string root)
    {
        var app = new App(); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var settings = new SettingsManager(Path.Combine(root, "settings.json"));
        using var vault = new VaultManager(); using var clipboard = new ClipboardManager();
        var data = vault.OpenAsync(Path.Combine(root, "ui.solekey"), Master, true).GetAwaiter().GetResult();
        data.Entries.Add(Example()); data.Entries.Add(Example(false));
        data = vault.SaveAsync(data).GetAwaiter().GetResult();
        var vm = new MainViewModel(vault, settings, clipboard, data);
        var main = new MainWindow(vm); main.Show(); Pump();
        Check(!main.AllowsTransparency && main.Background is SolidColorBrush solid && solid.Color.A == 255 &&
            System.Windows.Shell.WindowChrome.GetWindowChrome(main).CornerRadius == new CornerRadius(0), "opaque solid native background avoids black software-rounded corners");
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            Check(DwmGetWindowAttribute(new WindowInteropHelper(main).Handle, 33, out var preference, sizeof(int)) >= 0 && preference == 2,
                "Windows compositor owns rounded window corners");
        SystemCommands.MaximizeWindowCommand.Execute(null, main); WaitUntil(() => main.WindowState == WindowState.Maximized);
        Check(main.WindowState == WindowState.Maximized, "custom titlebar maximizes window");
        SystemCommands.RestoreWindowCommand.Execute(null, main); WaitUntil(() => main.WindowState == WindowState.Normal);
        Check(main.WindowState == WindowState.Normal, "custom titlebar restores maximized window");
        SystemCommands.MinimizeWindowCommand.Execute(null, main); WaitUntil(() => main.WindowState == WindowState.Minimized);
        Check(main.WindowState == WindowState.Minimized, "custom titlebar minimizes window");
        SystemCommands.RestoreWindowCommand.Execute(null, main); WaitUntil(() => main.WindowState == WindowState.Normal);
        Check(main.WindowState == WindowState.Normal, "custom titlebar restores minimized window");
        vm.Selected = vm.Entries[0]; Pump();
        Check(Descendants<SecretInput>(main).Count() == 2, "main renders two secret rows");
        var secret = Descendants<SecretInput>(main).First();
        Check(Descendants<PasswordBox>(secret).Single().Password == vm.Selected.Passwords[0].Value, "secret binding populates masked editor");
        Check(Descendants<TextBlock>(secret).Any(t => t.Text == "••••••••" && t.IsVisible), "detail masks fixed length");
        Descendants<System.Windows.Controls.Primitives.ToggleButton>(secret).Single().IsChecked = true; Pump();
        Check(Descendants<TextBox>(secret).Single().IsVisible, "detail reveals requested password");
        Check(Descendants<TextBox>(secret).Single().Text == vm.Selected.Passwords[0].Value, "revealed value matches selected password");
        vm.Selected = vm.Entries[1]; Pump();
        Check(Descendants<SecretInput>(main).All(s => Descendants<System.Windows.Controls.Primitives.ToggleButton>(s).Single().IsChecked == false), "selection change resets revealed passwords");
        Check(!vm.CopyIdCommand.CanExecute(null), "password-only ID copy disabled");
        vm.Search = "검증용 메모"; Check(vm.FilteredEntries.Cast<object>().Count() == 2, "memo search");
        vm.Search = "example-id"; Check(vm.FilteredEntries.Cast<object>().Count() == 1, "ID search ignores unused ID");
        vm.Search = "EXAMPLE-ONLY"; Check(vm.FilteredEntries.IsEmpty && vm.Selected is null, "PW excluded from search and hidden selection cleared");
        vm.Search = "";
        vm.Selected = vm.Entries[0]; Pump();
        WaitUntil(() => ((FrameworkElement)main.FindName("MainContent")).Opacity >= 0.99);
        Render(main, "main");
        var source = Example();
        var editor = new AccountEditViewModel(source, _ => Task.CompletedTask) { Confirm = _ => true };
        var editWindow = new AccountEditWindow(editor); editor.Confirm = _ => true; editWindow.Show(); Pump();
        editor.AddPasswordCommand.Execute(null); Pump();
        Check(editor.Draft.Passwords.Count == 3 && source.Passwords.Count == 2, "add PW edits draft only");
        Check(Descendants<SecretInput>(editWindow).Count() == 3, "editor renders added password row");
        Render(editWindow, "editor");
        var added = editor.Draft.Passwords[^1]; editor.RemovePasswordCommand.Execute(added); editor.RemovePasswordCommand.Execute(editor.Draft.Passwords[^1]);
        Check(!editor.RemovePasswordCommand.CanExecute(editor.Draft.Passwords[0]), "last password removal disabled");
        editor.Draft.UseLoginId = false; editor.Draft.UseLoginId = true;
        Check(editor.Draft.LoginId == source.LoginId, "ID toggle retains draft");
        Check(editor.CanClose(), "dirty editor discard confirmation");
        editor.Confirm = _ => false;
        SystemCommands.CloseWindowCommand.Execute(null, editWindow); Pump();
        Check(editWindow.IsVisible && editor.Active, "custom close respects unsaved edit cancellation");
        editor.Confirm = _ => true;
        var settingsVM = new VaultSettingsViewModel(vault, settings);
        var settingsWindow = new VaultSettingsWindow(settingsVM); settingsWindow.Show(); Pump();
        Check(Descendants<SecretInput>(settingsWindow).Count() == 4, "settings screen loads secret inputs");
        settingsVM.AutoLockMinutes = "invalid"; settingsVM.SaveSettingsCommand.Execute(null);
        Check(settingsVM.Status.Contains("정수"), "invalid settings text rejected without saving prior value");
        Render(settingsWindow, "settings");
        settingsWindow.Close();
        var unlockVM = new UnlockViewModel(vault, settings); var unlockWindow = new UnlockWindow(unlockVM); unlockWindow.Show(); Pump();
        Check(Descendants<SecretInput>(unlockWindow).Count() == 2, "unlock and create screen load");
        Render(unlockWindow, "unlock");
        unlockVM.Password = "temporary-test-value"; unlockVM.Create = true;
        Check(unlockVM.Password == "", "unlock mode changes clear master input");
        unlockWindow.Close();
        vm.RequestLock(true); Pump();
        Check(!vm.Active && vm.Entries.Count == 0 && vm.Selected is null && vm.Search == "", "forced lock clears main state");
        editor.Deactivate(); editWindow.Close(); main.Close();
        Check(editor.Draft.Passwords.Count == 0, "editor deactivation removes draft secrets");
        CommandChecks(vault, settings, clipboard, root);
        TrayChecks(vault, settings, clipboard, root);
        CreationInputChecks(vault, settings, root);
        UnlockMotionChecks(vault, settings, root);
        vault.LockAsync().GetAwaiter().GetResult();
        app.Shutdown();
    }
    private static void CommandChecks(VaultManager vault, SettingsManager settings, ClipboardManager clipboard, string root)
    {
        var data = vault.OpenAsync(Path.Combine(root, "commands.solekey"), Master, true).GetAwaiter().GetResult();
        var vm = new MainViewModel(vault, settings, clipboard, data) { Confirm = _ => true };
        vm.EditRequested += editor =>
        {
            editor.Confirm = _ => true;
            if (editor.Draft.ServiceName == "")
            {
                editor.Draft.ServiceName = "Command created"; editor.Draft.UseLoginId = false;
                editor.Draft.Passwords[0].Value = "EXAMPLE-ONLY-command";
                editor.AddPasswordCommand.Execute(null); editor.Draft.Passwords[1].Value = "EXAMPLE-ONLY-secondary";
            }
            else { editor.Draft.Memo = "edited memo"; editor.Draft.Passwords[^1].Value = "EXAMPLE-ONLY-new"; }
            editor.SaveCommand.Execute(null); WaitUntil(() => !editor.Busy);
            Check(editor.Saved, "editor SaveCommand completes");
        };
        vm.AddCommand.Execute(null);
        Check(vm.Entries.Count == 1 && vm.Entries[0].Passwords.Count == 2 && !vm.Entries[0].UseLoginId, "add command persists optional ID and multiple PW");
        vm.Selected = vm.Entries[0]; vm.AppendPasswordCommand.Execute(null);
        Check(vm.Entries[0].Passwords.Count == 3 && vm.Entries[0].Memo == "edited memo", "main plus command appends PW through saved edit");
        vm.DeleteCommand.Execute(null); WaitUntil(() => !vm.Busy);
        Check(vm.Entries.Count == 0, "delete command persists removal");
        vm.Deactivate();
        var reopened = vault.OpenAsync(Path.Combine(root, "commands.solekey"), Master, false).GetAwaiter().GetResult();
        Check(reopened.Entries.Count == 0, "command changes survive reopen");
        var failed = new AccountEditViewModel(Example(), _ => Task.FromException(new IOException())) { Confirm = _ => true };
        failed.SaveCommand.Execute(null); WaitUntil(() => !failed.Busy);
        Check(!failed.Saved && failed.Draft.Passwords.Count == 2 && failed.Status.Contains("저장"), "save failure retains editable draft and reports error");
        failed.Deactivate();
        var invalid = new AccountEditViewModel(Example(), _ => throw new Exception("should not save"));
        invalid.AddPasswordCommand.Execute(null); invalid.SaveCommand.Execute(null); WaitUntil(() => !invalid.Busy);
        Check(!invalid.Saved && invalid.Status.Contains("비밀번호"), "empty new PW row stops save command"); invalid.Deactivate();
        var pending = new TaskCompletionSource();
        var late = new AccountEditViewModel(Example(), _ => pending.Task);
        var closedAfterLock = false; late.CloseRequested += _ => closedAfterLock = true;
        late.SaveCommand.Execute(null); Check(late.Busy, "pending save enters busy state");
        late.Deactivate(); pending.SetResult(); WaitUntil(() => !late.Busy);
        Check(!closedAfterLock && !late.Saved && late.Draft.Passwords.Count == 0, "late save completion cannot reopen locked editor");
    }
    private static void Invoke(Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke(); Pump();
    }
    private static void CreationInputChecks(VaultManager vault, SettingsManager settings, string root)
    {
        var path = Path.Combine(root, "created-from-input.solekey");
        var vm = new UnlockViewModel(vault, settings) { Create = true, FilePath = path };
        var window = new UnlockWindow(vm); var prompts = 0;
        vm.Confirm = _ => { prompts++; return true; }; window.Show(); Pump();
        var inputs = Descendants<SecretInput>(window).ToList();
        var master = (PasswordBox)inputs[0].FindName("Masked");
        var confirmation = (PasswordBox)inputs[1].FindName("Masked");
        var create = Descendants<Button>(window).Single(b => ReferenceEquals(b.Command, vm.OpenCommand));
        master.Password = "short"; confirmation.Password = "short"; Pump();
        Check(vm.Password == "short" && vm.Confirmation == "short", "password input events update create model");
        Invoke(create); WaitUntil(() => !vm.Busy);
        Check(vm.Phase == UnlockPhase.Idle && vm.HasError && vm.Status.Contains("15~128") && prompts == 0 && !File.Exists(path), "short master rejected before animation prompt and file write");
        Check(master.Password == "short" && confirmation.Password == "short", "invalid creation retains both password inputs");
        master.Password = Master; confirmation.Password = NextMaster; Pump();
        Invoke(create); WaitUntil(() => !vm.Busy);
        Check(vm.Status.Contains("일치") && master.Password == Master && !File.Exists(path), "confirmation mismatch retains input and creates nothing");
        var face = new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Check(face.TryGetGlyphTypeface(out var glyph) && glyph.FamilyNames.Values.Any(n => n.Contains("Pretendard", StringComparison.OrdinalIgnoreCase)), "actual window resolves bundled Pretendard glyphs");
        Check(!window.AllowsTransparency && TextOptions.GetTextRenderingMode(window) == TextRenderingMode.ClearType, "opaque window enables ClearType text");
        var eye = (System.Windows.Controls.Primitives.ToggleButton)inputs[0].FindName("Reveal");
        Check(((Border)inputs[0].FindName("Field")).IsAncestorOf(eye) && eye.Content is null, "eye icon is inside password field");
        eye.IsChecked = true; Pump();
        Check(((TextBox)inputs[0].FindName("Plain")).Text == Master, "eye reveals exactly typed input");
        eye.IsChecked = false;
        confirmation.Password = Master; var opened = false;
        vm.Opened += snapshot => { opened = true; snapshot.Clear(); };
        Invoke(create); WaitUntil(() => !vm.Busy);
        Check(opened && !vm.HasError && File.Exists(path), "actual create button creates vault from typed passwords");
        Render(window, "create-success");
        var close = (Button)window.Template.FindName("CloseButton", window);
        Check(ReferenceEquals(close.CommandTarget, window) && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(close) && close.ActualWidth >= 24, "close button targets owner and is hit-testable in caption");
        Invoke(close); Check(!window.IsVisible && !vm.Active, "actual close button closes unlock window");
        awaitLock();
        var reopened = vault.OpenAsync(path, Master, false).GetAwaiter().GetResult();
        Check(reopened.Entries.Count == 0, "created UI vault reopens with chosen master"); awaitLock();

        var cancelPath = Path.Combine(root, "cancelled-create.solekey");
        var cancel = new UnlockViewModel(vault, settings) { Create = true, FilePath = cancelPath };
        var cancelWindow = new UnlockWindow(cancel); cancel.Confirm = _ => true; cancelWindow.Show(); Pump();
        var fields = Descendants<SecretInput>(cancelWindow).ToList();
        ((PasswordBox)fields[0].FindName("Masked")).Password = Master;
        ((PasswordBox)fields[1].FindName("Masked")).Password = Master;
        Invoke(Descendants<Button>(cancelWindow).Single(b => ReferenceEquals(b.Command, cancel.OpenCommand)));
        Check(cancel.Busy, "creation is in progress before cancellation");
        Invoke((Button)cancelWindow.Template.FindName("CloseButton", cancelWindow));
        WaitUntil(() => !cancel.Busy);
        Check(!cancelWindow.IsVisible && !File.Exists(cancelPath), "close during creation cancels before file creation");
        awaitLock();
        void awaitLock() => vault.LockAsync().GetAwaiter().GetResult();
    }
    private static void TrayChecks(VaultManager vault, SettingsManager settings, ClipboardManager clipboard, string root)
    {
        var data = vault.OpenAsync(Path.Combine(root, "tray.solekey"), Master, true).GetAwaiter().GetResult();
        data.Entries.Add(Example()); data = vault.SaveAsync(data).GetAwaiter().GetResult();
        var vm = new MainViewModel(vault, settings, clipboard, data);
        var window = new MainWindow(vm); window.Show(); Pump();
        vm.Selected = vm.Entries[0]; var entry = vm.Selected;
        using var tray = new TrayController(() => vm.RequestLock(false), () => { });
        tray.Hidden += () => vm.SetTrayState(true);
        tray.Restored += () => vm.SetTrayState(false);
        tray.Attach(window, true);
        Invoke((Button)window.Template.FindName("CloseButton", window));
        Check(!window.IsVisible && vm.Active && ReferenceEquals(vm.Selected, entry), "close hides to tray without discarding unlocked session");
        tray.Restore(); Pump();
        Check(window.IsVisible && vm.Active && ReferenceEquals(vm.Selected, entry), "tray restore keeps account selection without master prompt");
        SystemCommands.MinimizeWindowCommand.Execute(null, window); Pump();
        Check(!window.IsVisible, "minimize hides main window to tray");
        tray.Restore(); Pump();
        Check(window.IsVisible && window.WindowState == WindowState.Normal, "tray restores minimized window normally");
        tray.Hide();
        typeof(MainViewModel).GetField("lastInput", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, Environment.TickCount64 - settings.Current.AutoLockMinutes * 60000L - 1000);
        vm.CheckIdle();
        Check(vm.Active && !window.IsVisible, "tray session ignores elapsed application idle timeout");
        tray.Restore(); vm.CheckIdle();
        Check(vm.Active && window.IsVisible, "tray return restarts idle timeout without master prompt");

        var name = "Local\\SoleKey.Test." + Guid.NewGuid().ToString("N");
        using (var primary = new InstanceActivation(name, () => window.Dispatcher.BeginInvoke(tray.Restore)))
        {
            Check(primary.IsPrimary, "first instance owns application session");
            tray.Hide();
            var secondIsPrimary = Task.Run(() =>
            {
                using var second = new InstanceActivation(name, () => { }); return second.IsPrimary;
            }).GetAwaiter().GetResult();
            WaitUntil(() => window.IsVisible);
            Check(!secondIsPrimary && vm.Active && ReferenceEquals(vm.Selected, entry), "second launch restores primary unlocked session");
        }
        using (var replacement = new InstanceActivation(name, () => { })) Check(replacement.IsPrimary, "full instance exit releases ownership");

        UnlockWindow? lockedWindow = null; UnlockViewModel? locked = null;
        vm.LockRequested += () =>
        {
            tray.Detach(); window.Close(); vault.LockAsync().GetAwaiter().GetResult();
            locked = new UnlockViewModel(vault, settings);
            lockedWindow = new UnlockWindow(locked); tray.Attach(lockedWindow, false, () => !locked.Busy);
        };
        tray.Hide(); vm.RequestLock(true); tray.Restore(); Pump();
        Check(!vm.Active && vm.Entries.Count == 0 && lockedWindow!.IsVisible && locked!.Password == "", "explicit lock in tray restores password screen instead of old data");
        tray.Detach(); lockedWindow!.Close();
        var defaults = new UnlockViewModel(vault, settings) { FilePath = "", Create = true };
        Check(Path.GetFileName(defaults.FilePath) == "SoleKey.solekey", "new vault defaults to SoleKey filename"); defaults.Deactivate();
    }
    private static void UnlockMotionChecks(VaultManager vault, SettingsManager settings, string root)
    {
        var path = Path.Combine(root, "commands.solekey");
        var vm = new UnlockViewModel(vault, settings) { FilePath = path, Password = "wrong password" };
        var window = new UnlockWindow(vm); window.Show(); Pump();
        var motion = Descendants<UnlockAnimation>(window).Single();
        var phases = new List<UnlockPhase>();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Phase)) phases.Add(vm.Phase); };
        var opened = 0;
        var openedAfterMotion = false;
        vm.Opened += snapshot =>
        {
            opened++;
            openedAfterMotion = ((ScaleTransform)motion.FindName("DoorSwing")).ScaleX < 0.2 && ((Grid)motion.FindName("OpenMark")).Opacity >= 0.99;
            snapshot.Clear();
        };
        vm.OpenCommand.Execute(null);
        Check(vm.Busy && vm.Phase == UnlockPhase.Checking && !vm.OpenCommand.CanExecute(null), "checking animation disables duplicate unlock");
        WaitUntil(() => !vm.Busy);
        Check(opened == 0 && vm.Phase == UnlockPhase.Failed && !phases.Contains(UnlockPhase.Opening), "wrong password never plays opening or enters main");
        Check(((ScaleTransform)motion.FindName("DoorSwing")).ScaleX == 1, "failed authentication keeps vault door closed");
        Render(window, "unlock-failed");
        WaitUntil(() => ((TranslateTransform)motion.FindName("KeyMove")).X <= 0.1);
        vm.Password = Master; phases.Clear();
        var frames = new List<BitmapFrame>();
        void Capture(ushort delay)
        {
            var bitmap = new RenderTargetBitmap(420, 180, 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawRectangle((Brush)window.FindResource("CanvasBrush"), null, new Rect(0, 0, 420, 180));
                var brush = new VisualBrush(motion) { Stretch = Stretch.Uniform, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, motion.ActualWidth, motion.ActualHeight) };
                context.DrawRectangle(brush, null, new Rect(0, 0, 420, 180));
            }
            bitmap.Render(drawing);
            var metadata = new BitmapMetadata("gif"); metadata.SetQuery("/grctlext/Delay", delay);
            frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        timer.Tick += (_, _) => Capture(6);
        try
        {
            vm.OpenCommand.Execute(null); Pump(); Capture(16); timer.Start();
            WaitUntil(() => !vm.Busy);
        }
        finally { timer.Stop(); }
        Capture(90);
        Check(opened == 1 && phases.Contains(UnlockPhase.Checking) && phases.Contains(UnlockPhase.Opening), "correct password retries and completes one unlock");
        Check(openedAfterMotion, "main transition waits until door opening completes");
        Check(vm.Password == "" && vm.Confirmation == "", "master inputs cleared during animated unlock");
        Check(settings.Current.RecentFile == path, "animated unlock remembers only vault path");
        var preview = Path.Combine(PreviewDirectory, "unlock-motion.gif");
        var gif = new GifBitmapEncoder(); foreach (var frame in frames) gif.Frames.Add(frame);
        using (var output = File.Create(preview)) gif.Save(output);
        Render(window, "unlock-opened");
        vm.Deactivate(); window.Close();

        var pending = new TaskCompletionSource();
        var cancelled = new UnlockViewModel(vault, settings) { FilePath = path, Password = Master };
        cancelled.UnlockAnimationRequested += () => pending.Task;
        var lateOpened = false; cancelled.Opened += _ => lateOpened = true;
        cancelled.OpenCommand.Execute(null); WaitUntil(() => cancelled.Phase == UnlockPhase.Opening);
        Check(cancelled.Busy && !cancelled.OpenCommand.CanExecute(null), "success animation remains busy until presentation completes");
        cancelled.Deactivate(); pending.SetResult(); WaitUntil(() => !cancelled.Busy);
        Check(!lateOpened && cancelled.Phase == UnlockPhase.Idle, "deactivation during animation blocks late main transition");
        Throws<InvalidOperationException>(() => vault.SaveAsync(new()).GetAwaiter().GetResult(), "interrupted animated unlock releases vault key");
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
