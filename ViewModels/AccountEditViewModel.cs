using System.Text.Json;
using SoleKey.Infrastructure;
using SoleKey.Models;

namespace SoleKey.ViewModels;

public sealed class AccountEditViewModel : ScreenModel
{
    private readonly Func<AccountEntry, Task> save;
    private readonly string initial;
    private readonly bool hadId;
    public AccountEntry Draft { get; }
    public RelayCommand AddPasswordCommand { get; }
    public RelayCommand RemovePasswordCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public event Action<bool>? CloseRequested;
    public event Action<PasswordEntry>? FocusPasswordRequested;
    public bool Saved { get; private set; }
    public AccountEditViewModel(AccountEntry source, Func<AccountEntry, Task> save, bool appendPassword = false)
    {
        this.save = save; Draft = VaultData.Clone(source); hadId = source.UseLoginId && source.LoginId.Length > 0;
        initial = JsonSerializer.Serialize(Draft, VaultData.Json);
        if (Draft.Passwords.Count == 0 || appendPassword) AddPassword();
        AddPasswordCommand = new(_ => AddPassword(), _ => Ready && Draft.Passwords.Count < 100);
        RemovePasswordCommand = new(p =>
        {
            if (p is PasswordEntry row && (row.Value.Length == 0 || Confirm($"‘{row.Label}’ 비밀번호 행을 삭제할까요?")) && Active) Draft.Passwords.Remove(row);
        }, _ => Ready && Draft.Passwords.Count > 1);
        SaveCommand = new(_ => Work(Save), Report, _ => Ready);
        CancelCommand = new(_ => { if (CanClose()) CloseRequested?.Invoke(false); }, _ => Ready);
    }
    private void AddPassword()
    {
        var row = new PasswordEntry { Label = Draft.Passwords.Count == 0 ? "비밀번호" : $"{Draft.Passwords.Count + 1}차 비밀번호" };
        Draft.Passwords.Add(row); FocusPasswordRequested?.Invoke(row);
    }
    private async Task Save()
    {
        var next = VaultData.Clone(Draft); next.ServiceName = next.ServiceName.Trim();
        if (!next.UseLoginId) next.LoginId = "";
        VaultData.ValidateEntry(next);
        if (!next.UseLoginId && hadId && !Confirm("ID 사용 해제 시 기존 ID가 삭제됩니다. 저장할까요?")) return;
        if (!Active) return;
        Status = "암호화하여 저장 중…";
        await save(next);
        if (!Active) return;
        Saved = true; CloseRequested?.Invoke(true);
    }
    public bool CanClose() => !Active || Saved || (!Busy &&
        (initial == JsonSerializer.Serialize(Draft, VaultData.Json) || Confirm("편집 중인 변경 사항을 버릴까요?")));
    public override void Deactivate()
    {
        base.Deactivate(); Draft.ServiceName = Draft.LoginId = Draft.Memo = "";
        foreach (var p in Draft.Passwords) p.Value = "";
        Draft.Passwords.Clear();
    }
}
