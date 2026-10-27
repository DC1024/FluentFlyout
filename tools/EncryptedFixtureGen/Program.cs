// Copyright (c) 2026 DC1024
// SPDX-License-Identifier: GPL-3.0-or-later
//
// 加密 fixture 生成器。
//
// 背景：TaskbarLyrics 上游的 TestData/Lyrics/*.qrc|*.krc|*.yrc 全部是明文，
// 走的是 Parser 而非 Decrypter。也就是说上游 318 个用例里没有一个覆盖解密路径。
// 本工具按 Decrypter 的逆过程生成真实加密样本，供 LyricsDecryptionTests 回归。
//
// 用法：dotnet run --project tools/EncryptedFixtureGen -- <输出目录>

using System.Security.Cryptography;
using System.Text;
using Lyricify.Lyrics.Decrypter.Qrc;

var outDir = args.Length > 0 ? args[0] : "TestData/Lyrics";
Directory.CreateDirectory(outDir);

// ─────────────────────────────────────────────
// KRC：明文 → zlib → XOR(16B key) → base64（前置 4 字节 "krc1"）
// ─────────────────────────────────────────────
var krcPlain =
    "[1000,1800]<0,300,0>encrypted<300,0,0>krc<300,1500,0>roundtrip\n" +
    "[3000,1000]<0,500,0>second<500,500,0>line";

byte[] krcKey = { 0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69 };

// Decrypter 内部做 res[1..]（跳过首字符），所以压缩前要多加一个占位字符
var krcPayload = "\u0001" + krcPlain;
byte[] krcRaw = Encoding.UTF8.GetBytes(krcPayload);

byte[] krcCompressed;
using (var ms = new MemoryStream())
{
    // 必须用 ZLibStream（zlib 容器：2 字节头 + Adler32），
    // 不能用 DeflateStream（裸 deflate）——SharpZipLib 的 InflaterInputStream
    // 会抛 SharpZipBaseException: Header checksum illegal。
    using (var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
        zs.Write(krcRaw, 0, krcRaw.Length);
    krcCompressed = ms.ToArray();
}

for (int i = 0; i < krcCompressed.Length; i++)
    krcCompressed[i] ^= krcKey[i % krcKey.Length];

var krcWithHeader = new byte[4 + krcCompressed.Length];
Encoding.ASCII.GetBytes("krc1").CopyTo(krcWithHeader, 0);
krcCompressed.CopyTo(krcWithHeader, 4);

var krcEncrypted = Convert.ToBase64String(krcWithHeader);
File.WriteAllText(Path.Combine(outDir, "krc-encrypted.krc"), krcEncrypted, new UTF8Encoding(false));
File.WriteAllText(Path.Combine(outDir, "krc-encrypted.expected.txt"), krcPlain, new UTF8Encoding(false));
Console.WriteLine($"KRC  明文 {krcPlain.Length} 字符 → 密文 {krcEncrypted.Length} 字符");

// ─────────────────────────────────────────────
// QRC：明文 → zlib → TripleDES(8B 块) → hex
// ─────────────────────────────────────────────
var qrcPlain =
    "[1000,1800]encrypted(1000,300)qrc(1300,1500)roundtrip\n" +
    "[3000,1000]second(3000,500)line";

byte[] qqKey = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL");
byte[] qrcRaw = Encoding.UTF8.GetBytes(qrcPlain);

byte[] qrcCompressed;
using (var ms = new MemoryStream())
{
    using (var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
        zs.Write(qrcRaw, 0, qrcRaw.Length);
    qrcCompressed = ms.ToArray();
}

// TripleDESCrypt 按 8 字节块处理，补齐到块边界
int padded = (qrcCompressed.Length + 7) / 8 * 8;
var qrcBuf = new byte[padded];
qrcCompressed.CopyTo(qrcBuf, 0);

var schedule = new byte[3][][];
for (int i = 0; i < 3; i++)
{
    schedule[i] = new byte[16][];
    for (int j = 0; j < 16; j++) schedule[i][j] = new byte[6];
}
DESHelper.TripleDESKeySetup(qqKey, schedule, DESHelper.ENCRYPT);

var qrcCipher = new byte[padded];
for (int i = 0; i < qrcBuf.Length; i += 8)
{
    var block = new byte[8];
    Array.Copy(qrcBuf, i, block, 0, 8);
    var outBlock = new byte[8];
    DESHelper.TripleDESCrypt(block, outBlock, schedule);
    outBlock.CopyTo(qrcCipher, i);
}

var qrcEncrypted = Convert.ToHexString(qrcCipher).ToLowerInvariant();
File.WriteAllText(Path.Combine(outDir, "qrc-encrypted.qrc"), qrcEncrypted, new UTF8Encoding(false));
File.WriteAllText(Path.Combine(outDir, "qrc-encrypted.expected.txt"), qrcPlain, new UTF8Encoding(false));
Console.WriteLine($"QRC  明文 {qrcPlain.Length} 字符 → 密文 {qrcEncrypted.Length} 字符");

Console.WriteLine();
Console.WriteLine($"已写入 {Path.GetFullPath(outDir)}");
Console.WriteLine("提示：这些是自造样本，用于回归解密路径，不代表真实音源的实际密文格式细节。");
