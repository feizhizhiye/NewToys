using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace NewToys.BackupCenter.Services;

/// <summary>
/// 包级密码生成器：默认 24 位，四类字符各至少一个，剔除易混淆的 <c>0 O o I l 1 |</c>。
/// <para>用 <see cref="RandomNumberGenerator"/> 取值（⛔ 不用 <c>Random</c>：可预测）。</para>
/// </summary>
public static class PasswordGenerator
{
    /// <summary>默认长度。</summary>
    public const int DefaultLength = 24;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";     // 无 I / O
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";    // 无 l / o
    private const string Digits = "23456789";                    // 无 0 / 1
    private const string Symbols = "!@#$%^&*-_=+?";

    /// <summary>生成一个密码（长度下限 8）。</summary>
    public static string Generate(int length = DefaultLength)
    {
        length = Math.Max(8, length);
        var all = Upper + Lower + Digits + Symbols;
        var chars = new List<char>(length)
        {
            Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols)
        };
        while (chars.Count < length) chars.Add(Pick(all));

        // Fisher–Yates：让上面四个"必含字符"不固定落在前四位
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    /// <summary>密码指纹（SHA256 前 8 位十六进制）：界面只显示它，⛔ 不回显密码原文。</summary>
    public static string Fingerprint(string? password)
    {
        if (string.IsNullOrEmpty(password)) return "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
