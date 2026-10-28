using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using SoleKey.Infrastructure;
using SoleKey.Models;

namespace SoleKey.ViewModels;

public sealed class MainViewModel : ScreenModel
{
    private readonly VaultManager vault;
    private readonly SettingsManager settings;
    private readonly ClipboardManager clipboard;
    private string search = "";
    private AccountEntry? selected;
    private long lastInput = Environment.TickCount64;
    private bool inTray;
    private AccountEditViewModel? editor;
    private VaultSettingsViewModel? settingsScreen;
    public ObservableCollection<AccountEntry> Entries { get; } = [];
    public ICollectionView FilteredEntries { get; }
    public string Search { get => search; set { if (Set(ref search, value)) { FilteredEntries.Refresh(); if (Selected is not null && !Matches(Selected)) Selected = null; Notify(nameof(CountText)); Notify(nameof(EmptyMessage)); } } }
    public AccountEntry? Selected { get => selected; set { Set(ref selected, value); Notify(nameof(HasSelection)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool HasSelection => Selected is not null;
    public string CountText => $"계정 {Entries.Count}개 · 검색 결과 {FilteredEntries.Cast<object>().Count()}개";
    public string EmptyMessage => Entries.Count == 0 ? "등록된 계정이 없습니다.\n계정을 추가하세요." : "검색 결과가 없습니다.";
    public string FilePath => vault.FilePath;
    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand AppendPasswordCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public RelayCommand CopyIdCommand { get; }
    public RelayCommand CopyPasswordCommand { get; }
    public RelayCommand LockCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public event Action<AccountEditViewModel>? EditRequested;
    public event Action<VaultSettingsViewModel>? SettingsRequested;
    public event Action? LockRequested;
    public MainViewModel(VaultManager vault, SettingsManager settings, ClipboardManager clipboard, VaultData snapshot)
    {
        this.vault = vault; this.settings = settings; this.clipboard = clipboard;
        foreach (var e in snapshot.Entries) Entries.Add(e);
        FilteredEntries = CollectionViewSource.GetDefaultView(Entries); FilteredEntries.Filter = o => o is AccountEntry e && Matches(e);
        Status = "저장 완료";
        AddCommand = new(_ => Edit(new(), false), _ => Ready);
        EditCommand = new(_ => Edit(Selected!, false), _ => Ready && HasSelection);
        AppendPasswordCommand = new(_ => Edit(Selected!, true), _ => Ready && HasSelection && Selected!.Passwords.Count < 100);
        DeleteCommand = new(_ => Work(Delete), Report, _ => Ready && HasSelection);
        CopyIdCommand = new(_ => Copy(Selected!.LoginId, false), _ => Ready && Selected?.UseLoginId == true);
        CopyPasswordCommand = new(p => { if (p is PasswordEntry row && Selected?.Passwords.Contains(row) == true) Copy(row.Value, true); }, _ => Ready);
        LockCommand = new(_ => RequestLock(false));
        SettingsCommand = new(_ =>
        {
            settingsScreen = new(vault, settings); settingsScreen.LockRequested += () => RequestLock(true);
            try { SettingsRequested?.Invoke(settingsScreen); }
            finally { settingsScreen.Deactivate(); settingsScreen = null; }
        }, _ => Ready);
    }
    private bool Matches(AccountEntry e) => string.IsNullOrWhiteSpace(Search) ||
        e.ServiceName.Contains(Search, StringComparison.OrdinalIgnoreCase) || (e.UseLoginId && e.LoginId.Contains(Search, StringComparison.OrdinalIgnoreCase)) || e.Memo.Contains(Search, StringComparison.OrdinalIgnoreCase);
    private void Copy(string value, bool sensitive)
    {
        try { clipboard.Copy(value, settings.Current.ClipboardSeconds, sensitive); Status = sensitive ? "PW 복사 완료 · 시간이 지나면 클립보드를 정리합니다." : "ID 복사 완료"; }
        catch (System.Runtime.InteropServices.ExternalException) { Status = "클립보드를 사용할 수 없습니다. 다시 시도하세요."; }
    }
    private void Edit(AccountEntry source, bool append)
    {
        editor = new(source, async next =>
        {
            if (!Active) throw new InvalidOperationException();
            var data = Snapshot(); var index = data.Entries.FindIndex(e => e.Id == next.Id);
            if (index < 0) data.Entries.Add(next); else data.Entries[index] = next;
            var saved = await vault.SaveAsync(data);
            if (Active) Refresh(saved, next.Id);
        }, append);
        try { EditRequested?.Invoke(editor); }
        finally { editor.Deactivate(); editor = null; }
    }
    private async Task Delete()
    {
        var entry = Selected!;
        if (!Confirm($"‘{entry.ServiceName}’ ({entry.DisplayId}) 항목을 삭제할까요?")) return;
        if (!Active) return;
        var next = Snapshot(); next.Entries.RemoveAll(e => e.Id == entry.Id); Status = "저장 중…";
        var saved = await vault.SaveAsync(next);
        if (Active) Refresh(saved, null);
    }
    private VaultData Snapshot() => VaultData.Clone(new VaultData { Entries = Entries.ToList() });
    private void Refresh(VaultData data, Guid? id)
    {
        Selected = null; Entries.Clear(); foreach (var e in data.Entries) Entries.Add(e);
        Selected = Entries.FirstOrDefault(e => e.Id == id && Matches(e)); Notify(nameof(CountText)); Notify(nameof(EmptyMessage)); Status = "저장 완료";
    }
    public void RecordInput() => lastInput = Environment.TickCount64;
    public void SetTrayState(bool hidden) { inTray = hidden; if (!hidden) RecordInput(); }
    public void CheckIdle() { if (Active && !inTray && Environment.TickCount64 - lastInput >= settings.Current.AutoLockMinutes * 60000L) RequestLock(true); }
    public void RequestLock(bool forced)
    {
        if (!Active) return;
        if (!forced && editor is not null && !editor.CanClose()) return;
        var lostDraft = editor is not null && !editor.Saved;
        Deactivate();
        LockNotice = lostDraft ? "자동 잠금으로 미저장 편집 내용을 폐기했습니다." : "금고가 잠겼습니다.";
        LockRequested?.Invoke();
    }
    public string LockNotice { get; private set; } = "";
    public override void Deactivate()
    {
        base.Deactivate(); editor?.Deactivate(); settingsScreen?.Deactivate(); clipboard.ClearOwned();
        Selected = null; Search = "";
        foreach (var e in Entries) { e.ServiceName = e.LoginId = e.Memo = ""; foreach (var p in e.Passwords) p.Value = ""; }
        Entries.Clear(); Notify(nameof(CountText));
    }
}
