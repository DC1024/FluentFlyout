// Copyright (c) 2026 DC1024
// SPDX-License-Identifier: GPL-3.0-or-later
//
// 解密路径回归测试。
//
// 为什么需要这组测试：
//   TaskbarLyrics 上游 TestData/Lyrics/*.qrc|*.krc|*.yrc 全部是明文 fixture，
//   走的是 Parser 而不是 Decrypter。上游 318 个用例里没有一个覆盖解密路径。
//   而真实音源返回的 KRC/QRC 一定是加密的——这等于最关键的路径在 CI 里从未被执行。
//
// 样本来源：
//   TestData/Lyrics/*-encrypted.* 由 tools/EncryptedFixtureGen 按 Decrypter
//   的逆过程生成。它们覆盖三个 net10 下最脆弱的环节：
//     1. TripleDESCrypt 的位运算（QRC）
//     2. InflaterInputStream 的 zlib 容器解析（两者）
//     3. 首字符跳过与 BOM 处理（KRC）
//
// 注意：zlib 容器 vs 裸 deflate
//   SharpZipLib 的 InflaterInputStream 要求 zlib 容器格式（2 字节头 + Adler32）。
//   若样本用 DeflateStream 生成，解密端会抛
//   SharpZipBaseException: Header checksum illegal。
//   看起来像库不兼容，实际是样本生成方式错了。

using System.Text;
using Lyricify.Lyrics.Parsers;
using Xunit;
using KrcDecrypter = Lyricify.Lyrics.Decrypter.Krc.Decrypter;
using QrcDecrypter = Lyricify.Lyrics.Decrypter.Qrc.Decrypter;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricsDecryptionTests
{
    private static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Lyrics", fileName);
        Assert.True(File.Exists(path), $"测试样本不存在: {path}");
        return File.ReadAllText(path);
    }

    // ─────────────────────────────────────────────
    // KRC：XOR(16B key) + zlib
    // ─────────────────────────────────────────────

    [Fact]
    public void KrcDecryptLyrics_RecoversExactPlaintext()
    {
        var encrypted = ReadFixture("krc-encrypted.krc");
        var expected = ReadFixture("krc-encrypted.expected.txt");

        var decrypted = KrcDecrypter.DecryptLyrics(encrypted);

        Assert.NotNull(decrypted);
        Assert.Equal(expected, decrypted);
    }

    [Fact]
    public void KrcDecryptedOutput_IsParsableAsWordByWordLyrics()
    {
        var decrypted = KrcDecrypter.DecryptLyrics(ReadFixture("krc-encrypted.krc"));
        Assert.NotNull(decrypted);

        var lines = KrcParser.ParseLyrics(decrypted!);
        Assert.Equal(2, lines.Count);
        Assert.Contains("encrypted", lines[0].Text);
    }

    [Fact]
    public void KrcDecryptLyrics_ReturnsNullOrThrows_OnGarbageInput()
    {
        // 非法 base64：不应静默返回一段乱码
        var result = Record.Exception(() => KrcDecrypter.DecryptLyrics("not-a-valid-krc-payload"));
        var value = result is null ? KrcDecrypter.DecryptLyrics("not-a-valid-krc-payload") : null;

        Assert.True(result is not null || string.IsNullOrEmpty(value),
            "垃圾输入不应返回非空结果");
    }

    // ─────────────────────────────────────────────
    // QRC：TripleDES + zlib
    // ─────────────────────────────────────────────

    [Fact]
    public void QrcDecryptLyrics_RecoversExactPlaintext()
    {
        var encrypted = ReadFixture("qrc-encrypted.qrc");
        var expected = ReadFixture("qrc-encrypted.expected.txt");

        var decrypted = QrcDecrypter.DecryptLyrics(encrypted);

        Assert.NotNull(decrypted);
        Assert.Equal(expected, decrypted);
    }

    [Fact]
    public void QrcDecryptedOutput_IsParsableAsWordByWordLyrics()
    {
        var decrypted = QrcDecrypter.DecryptLyrics(ReadFixture("qrc-encrypted.qrc"));
        Assert.NotNull(decrypted);

        var data = QrcParser.Parse(decrypted!);
        var lines = data.Lines;
        Assert.NotNull(lines);
        Assert.Equal(2, lines!.Count);
        Assert.Contains("encrypted", lines[0].Text);
    }

    [Fact]
    public void QrcDecryptLyrics_ReturnsNullOrThrows_OnGarbageInput()
    {
        var result = Record.Exception(() => QrcDecrypter.DecryptLyrics("zzzz-not-hex"));
        var value = result is null ? QrcDecrypter.DecryptLyrics("zzzz-not-hex") : null;

        Assert.True(result is not null || string.IsNullOrEmpty(value),
            "垃圾输入不应返回非空结果");
    }

    // ─────────────────────────────────────────────
    // 往返一致性：解密后再加密应还原同一份密文
    // 这条用来锁死"加密实现被无意改动"的回归
    // ─────────────────────────────────────────────

    [Fact]
    public void KrcEncryption_IsDeterministic()
    {
        // zlib 在同一压缩级别下对同一输入是确定的，
        // 因此同一份明文重复加密必须得到同一份密文。
        var a = EncryptKrc(ReadFixture("krc-encrypted.expected.txt"));
        var b = EncryptKrc(ReadFixture("krc-encrypted.expected.txt"));
        Assert.Equal(a, b);
        Assert.Equal(ReadFixture("krc-encrypted.krc"), a);
    }

    [Fact]
    public void QrcEncryption_IsDeterministic()
    {
        var a = EncryptQrc(ReadFixture("qrc-encrypted.expected.txt"));
        var b = EncryptQrc(ReadFixture("qrc-encrypted.expected.txt"));
        Assert.Equal(a, b);
        Assert.Equal(ReadFixture("qrc-encrypted.qrc"), a);
    }

    // ─────────────────────────────────────────────
    // 加密实现（与 tools/EncryptedFixtureGen 保持一致）
    // ─────────────────────────────────────────────

    private static readonly byte[] KrcKey =
    {
        0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69
    };

    private static readonly byte[] QqKey = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL");

    private static string EncryptKrc(string plaintext)
    {
        // Decrypter 内部做 res[1..]，故压缩前补一个占位字符
        var payload = "\u0001" + plaintext;
        var raw = Encoding.UTF8.GetBytes(payload);

        var compressed = ZlibCompress(raw);
        for (var i = 0; i < compressed.Length; i++)
            compressed[i] ^= KrcKey[i % KrcKey.Length];

        var withHeader = new byte[4 + compressed.Length];
        Encoding.ASCII.GetBytes("krc1").CopyTo(withHeader, 0);
        compressed.CopyTo(withHeader, 4);

        return Convert.ToBase64String(withHeader);
    }

    private static string EncryptQrc(string plaintext)
    {
        var raw = Encoding.UTF8.GetBytes(plaintext);
        var compressed = ZlibCompress(raw);

        var padded = (compressed.Length + 7) / 8 * 8;
        var buf = new byte[padded];
        compressed.CopyTo(buf, 0);

        var schedule = new byte[3][][];
        for (var i = 0; i < 3; i++)
        {
            schedule[i] = new byte[16][];
            for (var j = 0; j < 16; j++)
                schedule[i][j] = new byte[6];
        }

        Lyricify.Lyrics.Decrypter.Qrc.DESHelper.TripleDESKeySetup(
            QqKey, schedule, Lyricify.Lyrics.Decrypter.Qrc.DESHelper.ENCRYPT);

        var cipher = new byte[padded];
        for (var i = 0; i < buf.Length; i += 8)
        {
            var block = new byte[8];
            Array.Copy(buf, i, block, 0, 8);
            var outBlock = new byte[8];
            Lyricify.Lyrics.Decrypter.Qrc.DESHelper.TripleDESCrypt(block, outBlock, schedule);
            outBlock.CopyTo(cipher, i);
        }

        return Convert.ToHexString(cipher).ToLowerInvariant();
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var ms = new MemoryStream();
        // 必须是 ZLibStream（zlib 容器），不是 DeflateStream（裸 deflate）
        using (var zs = new System.IO.Compression.ZLibStream(
                   ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zs.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }
}
