using SoleKey.Infrastructure;
using SoleKey.Models;

namespace SoleKey.ViewModels;

public sealed class VaultSettingsViewModel : ScreenModel
{
    private readonly VaultManager vault;
    private readonly SettingsManager settings;
    private string current = "", next = "", confirmation = "", restorePassword = "";
    private string autoLock, clipboardSeconds;
    private bool autoLockEnabled;
    public bool AutoLockEnabled { get => autoLockEnabled; set => Set(ref autoLockEnabled, value); }
    public string FilePath => vault.FilePath;
    public string AutoLockMinutes { get => autoLock; set => Set(ref autoLock, value); }
    public string ClipboardSeconds { get => clipboardSeconds; set => Set(ref clipboardSeconds, value); }
    public string CurrentPassword { get => current; set => Set(ref current, value); }
    public string NewPassword { get => next; set => Set(ref next, value); }
    public string Confirmation { get => confirmation; set => Set(ref confirmation, value); }
    public string RestorePassword { get => restorePassword; set => Set(ref restorePassword, value); }
    public AsyncRelayCommand BackupCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public AsyncRelayCommand ChangePasswordCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public event Action? LockRequested;
    public VaultSettingsViewModel(VaultManager vault, SettingsManager settings)
    {
        this.vault = vault; this.settings = settings;
        autoLockEnabled = settings.Current.AutoLockEnabled;
        autoLock = settings.Current.AutoLockMinutes.ToString(); clipboardSeconds = settings.Current.ClipboardSeconds.ToString();
        BackupCommand = new(_ => Work(async () =>
        {
            var path = PickFile(true); if (path is null) return;
            await vault.BackupAsync(path); if (Active) Status = "암호화 백업 완료";
        }), Report, _ => Ready);
        RestoreCommand = new(_ => Work(async () =>
        {
            var path = PickFile(false); if (path is null) return;
            if (!Confirm("현재 금고를 자동 백업한 뒤 선택한 백업으로 교체합니다. 복원할까요?")) return;
            if (!Active) return;
            var secret = RestorePassword; RestorePassword = ""; Status = "복원을 검증하는 중…";
            await vault.RestoreAsync(path, secret); if (Active) LockRequested?.Invoke();
        }), Report, _ => Ready);
        ChangePasswordCommand = new(_ => Work(async () =>
        {
            if (NewPassword != Confirmation) throw new ArgumentException("새 비밀번호 확인이 일치하지 않습니다.");
            CryptoManager.ValidateMaster(NewPassword);
            if (!Confirm("마스터 비밀번호를 변경할까요? 이전 백업은 당시 비밀번호로 열립니다.")) return;
            if (!Active) return;
            var oldSecret = CurrentPassword; var newSecret = NewPassword; CurrentPassword = NewPassword = Confirmation = "";
            Status = "마스터 비밀번호 변경 중…"; await vault.ChangePasswordAsync(oldSecret, newSecret);
            if (Active) Status = "비밀번호 변경 완료 · 이전 백업은 기존 비밀번호를 사용합니다.";
        }), Report, _ => Ready);
        SaveSettingsCommand = new(_ =>
        {
            try
            {
                if (!int.TryParse(AutoLockMinutes, out var minutes) || !int.TryParse(ClipboardSeconds, out var seconds))
                    throw new ArgumentException("시간을 정수로 입력하세요.");
                var value = VaultData.Clone(settings.Current); value.AutoLockEnabled = AutoLockEnabled; value.AutoLockMinutes = minutes; value.ClipboardSeconds = seconds;
                settings.Save(value); Status = "설정 저장 완료";
            }
            catch (Exception ex) { Report(ex); }
        }, _ => Ready);
    }
    public override void Deactivate() { base.Deactivate(); CurrentPassword = NewPassword = Confirmation = RestorePassword = ""; }
}
