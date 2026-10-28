using System.Security.Cryptography;

namespace SoleKey.Models;

public sealed class VaultManager : IDisposable
{
    private readonly object gate = new();
    private byte[]? key;
    private VaultEnvelope? header;
    private VaultData? data;
    private FileStream? lease;
    private byte[]? fingerprint;
    private int epoch;
    public string FilePath { get; private set; } = "";
    public Task<VaultData> OpenAsync(string path, string password, bool create, CancellationToken cancellationToken = default)
    {
        var session = Interlocked.Increment(ref epoch);
        return Task.Run(() =>
        {
            lock (gate)
            {
                EnsureSession(session);
                cancellationToken.ThrowIfCancellationRequested();
                Close();
                var full = Path.GetFullPath(path);
                if (create) CryptoManager.ValidateMaster(password);
                FileStream? newLease = null; byte[]? newKey = null;
                try
                {
                    // A persistent sidecar allows atomic replacement while excluding other SoleKey writers.
                    newLease = new FileStream(full + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var bytes = create ? null : ReadBounded(full);
                    var newHeader = create ? new VaultEnvelope() : CryptoManager.Parse(bytes!);
                    newKey = CryptoManager.Derive(password, newHeader);
                    cancellationToken.ThrowIfCancellationRequested();
                    var newData = create ? new VaultData() : CryptoManager.Decrypt(newHeader, newKey);
                    if (create)
                    {
                        bytes = CryptoManager.Encrypt(newData, newKey, newHeader);
                        WriteAtomic(full, bytes, newKey, false);
                    }
                    FilePath = full; lease = newLease; key = newKey; header = newHeader; data = newData;
                    fingerprint = SHA256.HashData(bytes!);
                    return VaultData.Clone(newData);
                }
                catch
                {
                    newLease?.Dispose(); if (newKey is not null) CryptographicOperations.ZeroMemory(newKey);
                    throw;
                }
            }
        });
    }
    public Task<VaultData> SaveAsync(VaultData next)
    {
        var session = Volatile.Read(ref epoch);
        return Task.Run(() =>
        {
            lock (gate)
            {
                EnsureSession(session); EnsureOpen(); next.Validate(); CheckUnchanged();
                var snapshot = VaultData.Clone(next);
                var bytes = CryptoManager.Encrypt(snapshot, key!, header!);
                WriteAtomic(FilePath, bytes, key!, true);
                fingerprint = SHA256.HashData(bytes); data?.Clear(); data = snapshot;
                return VaultData.Clone(snapshot);
            }
        });
    }
    public Task BackupAsync(string destination)
    {
        var session = Volatile.Read(ref epoch);
        return Task.Run(() => { lock (gate) { EnsureSession(session); EnsureOpen(); CheckUnchanged(); File.Copy(FilePath, destination, false); } });
    }
    public Task ChangePasswordAsync(string current, string next)
    {
        var session = Volatile.Read(ref epoch);
        return Task.Run(() =>
        {
            lock (gate)
            {
                EnsureSession(session); EnsureOpen(); CheckUnchanged(); CryptoManager.ValidateMaster(next);
                var check = CryptoManager.Derive(current, header!);
                try { if (!CryptographicOperations.FixedTimeEquals(key!, check)) throw new CryptographicException(); }
                finally { CryptographicOperations.ZeroMemory(check); }
                var nextHeader = new VaultEnvelope(); var nextKey = CryptoManager.Derive(next, nextHeader);
                try
                {
                    var bytes = CryptoManager.Encrypt(data!, nextKey, nextHeader);
                    WriteAtomic(FilePath, bytes, nextKey, true);
                    CryptographicOperations.ZeroMemory(key!); key = nextKey; header = nextHeader; fingerprint = SHA256.HashData(bytes);
                }
                catch { CryptographicOperations.ZeroMemory(nextKey); throw; }
            }
        });
    }
    public Task RestoreAsync(string source, string password)
    {
        var session = Volatile.Read(ref epoch);
        return Task.Run(() =>
        {
            lock (gate)
            {
                EnsureSession(session); EnsureOpen(); CheckUnchanged();
                var bytes = ReadBounded(source); var restoredHeader = CryptoManager.Parse(bytes);
                var restoredKey = CryptoManager.Derive(password, restoredHeader);
                try
                {
                    CryptoManager.Decrypt(restoredHeader, restoredKey).Clear();
                    File.Copy(FilePath, FilePath + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + Guid.NewGuid().ToString("N") + ".bak", false);
                    WriteAtomic(FilePath, bytes, restoredKey, true);
                    Close();
                }
                finally { CryptographicOperations.ZeroMemory(restoredKey); }
            }
        });
    }
    private void EnsureSession(int session) { if (session != Volatile.Read(ref epoch)) throw new InvalidOperationException("금고 세션이 종료되었습니다."); }
    private void EnsureOpen() { if (key is null) throw new InvalidOperationException("금고가 잠겨 있습니다."); }
    private void CheckUnchanged()
    {
        if (!CryptographicOperations.FixedTimeEquals(fingerprint!, SHA256.HashData(ReadBounded(FilePath))))
            throw new IOException("금고가 외부에서 변경되었습니다.");
    }
    private static byte[] ReadBounded(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is < 1 or > CryptoManager.MaxFileBytes) throw new InvalidDataException();
        var bytes = new byte[(int)file.Length]; file.ReadExactly(bytes); return bytes;
    }
    private static void WriteAtomic(string path, byte[] bytes, byte[] key, bool replace)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            CryptoManager.Decrypt(CryptoManager.Parse(ReadBounded(temp)), key).Clear();
            if (replace) File.Replace(temp, path, null);
            else File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public Task LockAsync()
    {
        var session = Interlocked.Increment(ref epoch);
        return Task.Run(() => { lock (gate) { if (session == Volatile.Read(ref epoch)) Close(); } });
    }
    private void Close()
    {
        if (key is not null) CryptographicOperations.ZeroMemory(key);
        data?.Clear();
        key = null; header = null; data = null; fingerprint = null; lease?.Dispose(); lease = null;
    }
    public void Dispose() { Interlocked.Increment(ref epoch); lock (gate) Close(); }
}
