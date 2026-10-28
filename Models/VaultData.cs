using System.Collections.ObjectModel;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using SoleKey.Infrastructure;

namespace SoleKey.Models;

public sealed class PasswordEntry : ObservableObject
{
    private string label = "비밀번호", value = "";
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired] public string Label { get => label; set => Set(ref label, value); }
    [JsonRequired] public string Value { get => value; set => Set(ref this.value, value); }
}

public sealed class AccountEntry : ObservableObject
{
    private string serviceName = "", loginId = "", memo = "";
    private bool useLoginId = true;
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired] public string ServiceName { get => serviceName; set => Set(ref serviceName, value); }
    [JsonRequired] public bool UseLoginId { get => useLoginId; set { Set(ref useLoginId, value); Notify(nameof(DisplayId)); } }
    [JsonRequired] public string LoginId { get => loginId; set { Set(ref loginId, value); Notify(nameof(DisplayId)); } }
    [JsonRequired] public string Memo { get => memo; set => Set(ref memo, value); }
    [JsonRequired] public ObservableCollection<PasswordEntry> Passwords { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayId => UseLoginId ? LoginId : "ID 없음";
}

public sealed class VaultData
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public List<AccountEntry> Entries { get; set; } = [];
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16 };
    public static T Clone<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        try { return JsonSerializer.Deserialize<T>(bytes, Json)!; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Clear()
    {
        foreach (var e in Entries)
        {
            e.ServiceName = e.LoginId = e.Memo = "";
            foreach (var p in e.Passwords) p.Value = "";
        }
        Entries.Clear();
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || Entries is null || Entries.Count > 10000) throw new InvalidDataException();
        var ids = new HashSet<Guid>();
        foreach (var entry in Entries)
        {
            if (entry is null || entry.Id == Guid.Empty || !ids.Add(entry.Id)) throw new InvalidDataException();
            ValidateEntry(entry);
        }
    }

    public static void ValidateEntry(AccountEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ServiceName) || entry.ServiceName.Length > 200)
            throw new ArgumentException("사용처를 1~200자로 입력하세요.");
        if (entry.LoginId is null || entry.LoginId.Length > 1000 || (entry.UseLoginId && string.IsNullOrWhiteSpace(entry.LoginId)))
            throw new ArgumentException("ID를 입력하거나 ID 사용을 해제하세요. ID는 최대 1,000자입니다.");
        if (entry.Memo is null || entry.Memo.Length > 50000) throw new ArgumentException("메모는 최대 50,000자입니다.");
        if (entry.Passwords is null || entry.Passwords.Count is < 1 or > 100) throw new ArgumentException("비밀번호는 1~100개 등록할 수 있습니다.");
        var ids = new HashSet<Guid>();
        foreach (var pw in entry.Passwords)
        {
            if (pw is null || pw.Id == Guid.Empty || !ids.Add(pw.Id)) throw new InvalidDataException();
            if (string.IsNullOrWhiteSpace(pw.Label) || pw.Label.Length > 100 || string.IsNullOrEmpty(pw.Value) || pw.Value.Length > 4096)
                throw new ArgumentException("각 비밀번호의 이름(1~100자)과 값(1~4,096자)을 입력하세요.");
        }
        if (!entry.UseLoginId && entry.LoginId.Length != 0) throw new InvalidDataException();
    }
}
