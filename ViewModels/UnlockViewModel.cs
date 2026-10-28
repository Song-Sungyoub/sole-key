using SoleKey.Infrastructure;
using SoleKey.Models;

namespace SoleKey.ViewModels;

public enum UnlockPhase { Idle, Checking, Opening, Failed }

public sealed class UnlockViewModel : ScreenModel
{
    private readonly VaultManager vault;
    private readonly SettingsManager settings;
    private string path, password = "", confirmation = "";
    private bool create;
    private UnlockPhase phase;
    private readonly CancellationTokenSource lifetime = new();
    public UnlockPhase Phase { get => phase; private set => Set(ref phase, value); }
    public string FilePath { get => path; set => Set(ref path, value); }
    public string Password { get => password; set => Set(ref password, value); }
    public string Confirmation { get => confirmation; set => Set(ref confirmation, value); }
    public bool Create
    {
        get => create;
        set
        {
            Set(ref create, value); Password = Confirmation = ""; Status = ""; Phase = UnlockPhase.Idle;
            if (value && string.IsNullOrWhiteSpace(FilePath)) FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SoleKey.solekey");
        }
    }
    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public event Action<VaultData>? Opened;
    public event Func<Task>? UnlockAnimationRequested;
    public UnlockViewModel(VaultManager vault, SettingsManager settings)
    {
        this.vault = vault; this.settings = settings; path = settings.Current.RecentFile;
        Status = settings.LoadWarning;
        BrowseCommand = new(_ => { var picked = PickFile(Create); if (picked is not null) FilePath = picked; }, _ => Ready);
        OpenCommand = new(_ => Work(Open), Report, _ => Ready);
    }
    private async Task Open()
    {
        if (string.IsNullOrWhiteSpace(FilePath)) throw new ArgumentException("금고 파일 위치를 선택하세요.");
        if (Create) CryptoManager.ValidateMaster(Password);
        if (Create && Password != Confirmation) throw new ArgumentException("비밀번호 확인이 일치하지 않습니다.");
        if (Create && File.Exists(FilePath)) throw new ArgumentException("같은 이름의 금고가 이미 있습니다. 새 파일 이름을 선택하거나 기존 금고를 열어주세요.");
        if (Create && !Confirm("마스터 비밀번호를 잊으면 복구할 수 없습니다. 새 금고를 만들까요?")) return;
        if (!Active) return;
        var secret = Password; Password = Confirmation = "";
        Phase = UnlockPhase.Checking; Status = "열쇠를 맞추는 중…";
        VaultData? snapshot = null;
        try
        {
            snapshot = await vault.OpenAsync(FilePath, secret, Create, lifetime.Token);
            secret = "";
            if (!Active) { snapshot.Clear(); await vault.LockAsync(); return; }
            Phase = UnlockPhase.Opening; Status = "잠금을 해제했습니다.";
            if (UnlockAnimationRequested is { } animate) await animate();
            if (!Active) { snapshot.Clear(); await vault.LockAsync(); return; }
            try { var next = VaultData.Clone(settings.Current); next.RecentFile = vault.FilePath; settings.Save(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = "최근 파일 위치를 저장하지 못했습니다."; }
            Opened?.Invoke(snapshot);
        }
        catch
        {
            if (snapshot is not null) { snapshot.Clear(); await vault.LockAsync(); }
            if (Active) Phase = UnlockPhase.Failed;
            throw;
        }
    }
    public override void Deactivate() { lifetime.Cancel(); base.Deactivate(); Password = Confirmation = ""; Phase = UnlockPhase.Idle; }
}
