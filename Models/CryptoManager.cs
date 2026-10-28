using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Konscious.Security.Cryptography;

namespace SoleKey.Models;

public sealed class VaultEnvelope
{
    [JsonRequired] public int FormatVersion { get; set; } = 1;
    [JsonRequired] public string Kdf { get; set; } = "Argon2id";
    [JsonRequired] public int MemoryKiB { get; set; } = 65536;
    [JsonRequired] public int Iterations { get; set; } = 3;
    [JsonRequired] public int Parallelism { get; set; } = 1;
    [JsonRequired] public string Salt { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    [JsonRequired] public string Cipher { get; set; } = "AES-256-GCM";
    [JsonRequired] public string Nonce { get; set; } = "";
    [JsonRequired] public string Tag { get; set; } = "";
    [JsonRequired] public string Ciphertext { get; set; } = "";
}

public static class CryptoManager
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    public static void ValidateMaster(string password)
    {
        if (password.Length is < 15 or > 128) throw new ArgumentException("마스터 비밀번호는 15~128자로 입력하세요.");
    }
    public static VaultEnvelope Parse(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException();
        var e = JsonSerializer.Deserialize<VaultEnvelope>(bytes, VaultData.Json) ?? throw new InvalidDataException();
        if (e.FormatVersion != 1 || e.Kdf != "Argon2id" || e.Cipher != "AES-256-GCM" ||
            e.MemoryKiB is < 19456 or > 131072 || e.Iterations is < 2 or > 6 || e.Parallelism is < 1 or > 4)
            throw new InvalidDataException();
        if (e.Salt != Convert.ToBase64String(Decode(e.Salt)) || Decode(e.Salt).Length != 16 || Decode(e.Nonce).Length != 12 || Decode(e.Tag).Length != 16 ||
            Decode(e.Ciphertext).Length is < 1 or > 10 * 1024 * 1024) throw new InvalidDataException();
        return e;
    }
    private static byte[] Decode(string? value) => value is null ? throw new InvalidDataException() : Convert.FromBase64String(value);
    public static byte[] Derive(string password, VaultEnvelope e)
    {
        if (password.Length > 128) throw new ArgumentException("마스터 비밀번호가 너무 깁니다.");
        var input = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon = new Argon2id(input) { Salt = Decode(e.Salt), MemorySize = e.MemoryKiB, Iterations = e.Iterations, DegreeOfParallelism = e.Parallelism };
            return argon.GetBytes(32);
        }
        finally { CryptographicOperations.ZeroMemory(input); }
    }
    // Fixed-order UTF-8 header; numeric fields use invariant culture and salt is canonical Base64.
    private static byte[] Aad(VaultEnvelope e) => Encoding.UTF8.GetBytes(FormattableString.Invariant(
        $"SoleKey\n{e.FormatVersion}\n{e.Kdf}\n{e.MemoryKiB}\n{e.Iterations}\n{e.Parallelism}\n{e.Salt}\n{e.Cipher}"));
    public static byte[] Encrypt(VaultData data, byte[] key, VaultEnvelope header)
    {
        data.Validate();
        var plain = JsonSerializer.SerializeToUtf8Bytes(data, VaultData.Json);
        try
        {
            if (plain.Length > 10 * 1024 * 1024) throw new ArgumentException("금고 데이터가 최대 크기 10MiB를 초과했습니다.");
            var e = VaultData.Clone(header);
            var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var cipher = new byte[plain.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, Aad(e));
            e.Nonce = Convert.ToBase64String(nonce); e.Tag = Convert.ToBase64String(tag); e.Ciphertext = Convert.ToBase64String(cipher);
            return JsonSerializer.SerializeToUtf8Bytes(e, VaultData.Json);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static VaultData Decrypt(VaultEnvelope e, byte[] key)
    {
        var cipher = Decode(e.Ciphertext); var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Decode(e.Nonce), cipher, Decode(e.Tag), plain, Aad(e));
            var data = JsonSerializer.Deserialize<VaultData>(plain, VaultData.Json) ?? throw new InvalidDataException();
            data.Validate(); return data;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
