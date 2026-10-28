using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NewToys.Core.Services;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 主密码密码库：包密码与投递凭据的**唯一保存处**（<c>config\backup\vault.json</c>）。
/// <para>
/// 主密码本身⛔ 不落盘：只存 PBKDF2-SHA256（30 万次）派生的盐，以及用派生密钥做的
/// AES-GCM 校验块；每条目各自一个 nonce，密钥只在解锁后存在于内存。
/// </para>
/// <para>
/// 连续解锁失败 5 次后本会话锁库（重启程序才重试），避免在线暴力猜主密码。
/// </para>
/// </summary>
public sealed class VaultService
{
    private const int DefaultIterations = 300_000;
    private const int MaxFailures = 5;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly byte[] VerifierPlain = Encoding.UTF8.GetBytes("newtoys-backup-vault");

    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public string Salt { get; set; } = "";
        public int Iterations { get; set; } = DefaultIterations;
        public string Verifier { get; set; } = "";
        public Dictionary<string, string> Entries { get; set; } = new();
    }

    private readonly BackupSettingsService _paths;
    private VaultFile _file = new();
    private byte[]? _key;
    private int _failures;

    public VaultService(BackupSettingsService paths)
    {
        _paths = paths;
        Load();
    }

    /// <summary>密码库文件是否已经建过（= 用户设过主密码）。</summary>
    public bool IsInitialized => _file.Salt.Length > 0 && _file.Verifier.Length > 0;

    /// <summary>本次会话是否已解锁（内存里有派生密钥）。</summary>
    public bool IsUnlocked => _key != null;

    /// <summary>失败次数已用尽 ⇒ 本会话不再接受解锁尝试。</summary>
    public bool IsLockedOut => _failures >= MaxFailures;

    public int RemainingAttempts => Math.Max(0, MaxFailures - _failures);

    public string FilePath => _paths.VaultFile;

    /// <summary>条目数（界面显示用）。</summary>
    public int EntryCount => _file.Entries.Count;

    /// <summary>首次设置主密码（已存在 ⇒ 返回 false，改用 <see cref="ChangeMaster"/>）。</summary>
    public bool Initialize(string master)
    {
        if (IsInitialized || string.IsNullOrEmpty(master)) return false;
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Derive(master, salt, DefaultIterations);
        _file = new VaultFile
        {
            Salt = Convert.ToBase64String(salt),
            Iterations = DefaultIterations,
            Verifier = Seal(key, VerifierPlain)
        };
        _key = key;
        _failures = 0;
        Save();
        return true;
    }

    /// <summary>解锁（失败累计到 5 次即锁库）。</summary>
    public bool Unlock(string master)
    {
        if (!IsInitialized || IsLockedOut || string.IsNullOrEmpty(master)) return false;
        var key = Derive(master, Convert.FromBase64String(_file.Salt), _file.Iterations);
        var plain = Open(key, _file.Verifier);
        if (plain == null || !plain.AsSpan().SequenceEqual(VerifierPlain))
        {
            _failures++;
            _key = null;
            return false;
        }
        _key = key;
        _failures = 0;
        return true;
    }

    /// <summary>锁库（清掉内存里的密钥；已保存的条目不受影响）。</summary>
    public void Lock() => _key = null;

    /// <summary>改主密码：用旧主密码解出全部条目，再用新主密码重新封装。</summary>
    public bool ChangeMaster(string oldMaster, string newMaster)
    {
        if (!IsInitialized || string.IsNullOrEmpty(newMaster)) return false;
        if (!Unlock(oldMaster)) return false;

        var plain = new Dictionary<string, byte[]>();
        foreach (var (id, blob) in _file.Entries)
        {
            var raw = Open(_key!, blob);
            if (raw != null) plain[id] = raw;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Derive(newMaster, salt, DefaultIterations);
        _file.Salt = Convert.ToBase64String(salt);
        _file.Iterations = DefaultIterations;
        _file.Verifier = Seal(key, VerifierPlain);
        _file.Entries.Clear();
        foreach (var (id, raw) in plain) _file.Entries[id] = Seal(key, raw);

        _key = key;
        _failures = 0;
        Save();
        return true;
    }

    /// <summary>取一个条目的明文（未解锁 / 不存在 ⇒ null）。</summary>
    public string? Get(string id)
    {
        if (_key == null || string.IsNullOrEmpty(id)) return null;
        if (!_file.Entries.TryGetValue(id, out var blob)) return null;
        var plain = Open(_key, blob);
        return plain == null ? null : Encoding.UTF8.GetString(plain);
    }

    /// <summary>写入 / 覆盖一个条目（未解锁 ⇒ false）。</summary>
    public bool Set(string id, string value)
    {
        if (_key == null || string.IsNullOrEmpty(id)) return false;
        _file.Entries[id] = Seal(_key, Encoding.UTF8.GetBytes(value));
        Save();
        return true;
    }

    /// <summary>删除一个条目（未解锁 / 不存在 ⇒ false）。</summary>
    public bool Remove(string id)
    {
        if (_key == null || string.IsNullOrEmpty(id)) return false;
        if (!_file.Entries.Remove(id)) return false;
        Save();
        return true;
    }

    /// <summary>密码库文件是否存在（页面提示用；不读内容）。</summary>
    public bool FileExists => File.Exists(FilePath);

    /// <summary>某个标识在库里是否已有条目（不需要解锁：只查密文索引）。</summary>
    public bool HasEntry(string id) => !string.IsNullOrEmpty(id) && _file.Entries.ContainsKey(id);

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var loaded = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(FilePath));
            if (loaded != null) _file = loaded;
        }
        catch { /* 文件损坏 ⇒ 当成"没建过"，界面提示重新设置主密码 */ }
    }

    private void Save()
    {
        Directory.CreateDirectory(_paths.DataDir);
        AtomicJsonFile.Write(FilePath, JsonSerializer.Serialize(_file, JsonOpts));
    }

    private static byte[] Derive(string master, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(master, salt, iterations, HashAlgorithmName.SHA256, 32);

    private static string Seal(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var all = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, all, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, all, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, all, nonce.Length + cipher.Length, tag.Length);
        return Convert.ToBase64String(all);
    }

    private static byte[]? Open(byte[] key, string blob)
    {
        try
        {
            var raw = Convert.FromBase64String(blob);
            if (raw.Length < 28) return null;
            var nonce = raw.AsSpan(0, 12).ToArray();
            var cipher = raw.AsSpan(12, raw.Length - 28).ToArray();
            var tag = raw.AsSpan(raw.Length - 16, 16).ToArray();
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }
        catch { return null; }
    }
}
