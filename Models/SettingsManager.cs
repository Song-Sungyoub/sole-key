using System.Text.Json;

namespace SoleKey.Models;

public sealed class AppSettings
{
    public string RecentFile { get; set; } = "";
    public int AutoLockMinutes { get; set; } = 5;
    public int ClipboardSeconds { get; set; } = 30;
    public void Validate()
    {
        if (AutoLockMinutes is < 1 or > 60 || ClipboardSeconds is < 5 or > 120)
            throw new ArgumentException("자동 잠금은 1~60분, 클립보드 정리는 5~120초로 설정하세요.");
    }
}

public sealed class SettingsManager
{
    private readonly string path;
    public AppSettings Current { get; private set; } = new();
    public string LoadWarning { get; private set; } = "";
    public SettingsManager(string? settingsPath = null)
    {
        path = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoleKey", "settings.json");
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException();
            Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), VaultData.Json) ?? new();
            Current.Validate();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Current = new(); LoadWarning = "설정을 읽지 못해 기본값을 사용합니다."; }
    }
    public void Save(AppSettings value)
    {
        value.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, VaultData.Json)); File.Move(temp, path, true); Current = VaultData.Clone(value); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
