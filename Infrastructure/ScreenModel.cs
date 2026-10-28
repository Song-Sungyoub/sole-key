using System.Security.Cryptography;

namespace SoleKey.Infrastructure;

public class ScreenModel : ObservableObject
{
    private string status = "";
    private bool busy;
    private bool hasError;
    public bool Active { get; private set; } = true;
    public string Status { get => status; set { Set(ref status, value); HasError = false; } }
    public bool HasError { get => hasError; private set => Set(ref hasError, value); }
    public bool Busy { get => busy; private set { Set(ref busy, value); Notify(nameof(Ready)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
    public bool Ready => Active && !Busy;
    public Func<string, bool> Confirm { get; set; } = _ => false;
    public Func<bool, string?> PickFile { get; set; } = _ => null;
    public virtual void Deactivate() { Active = false; Status = ""; Notify(nameof(Ready)); }
    public void Report(Exception ex)
    {
        if (!Active) return;
        Status = ex switch
        {
            CryptographicException => "비밀번호가 잘못되었거나 금고가 손상되었습니다.",
            System.Text.Json.JsonException or FormatException => "금고 파일 형식이 올바르지 않습니다.",
            InvalidDataException => "금고 데이터 또는 파일 버전을 확인하세요.",
            UnauthorizedAccessException => "파일 접근 권한이 없습니다. 저장 위치를 확인하세요.",
            IOException => "파일을 읽거나 저장할 수 없습니다. 사용 중 여부와 디스크 공간을 확인하세요.",
            ArgumentException => ex.Message,
            _ => "작업을 완료하지 못했습니다. 다시 시도하세요."
        };
        HasError = true;
    }
    protected async Task Work(Func<Task> action)
    {
        if (!Ready) return;
        Busy = true;
        try { await action(); }
        catch (Exception ex) { Report(ex); }
        finally { Busy = false; }
    }
}
