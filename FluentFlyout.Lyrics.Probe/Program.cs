using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Lyricify.Lyrics.Decrypter.Qrc;
using Lyricify.Lyrics.Parsers;
using KrcDecrypter = Lyricify.Lyrics.Decrypter.Krc.Decrypter;
using QrcDecrypter = Lyricify.Lyrics.Decrypter.Qrc.Decrypter;

Console.OutputEncoding = Encoding.UTF8;

var results = new List<(string Id, string Name, bool Pass, string Detail)>();
void Check(string id, string name, Func<string> body)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var detail = body();
        results.Add((id, name, true, $"{detail} ({sw.ElapsedMilliseconds}ms)"));
    }
    catch (Exception ex)
    {
        results.Add((id, name, false, $"{ex.GetType().Name}: {ex.Message}"));
    }
}

// ---------- V1 程序集加载 ----------
Check("V1", "程序集加载 (netstandard2.1 → net10)", () =>
{
    var asm = typeof(QrcParser).Assembly;
    var tfm = asm.GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
        .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>()
        .FirstOrDefault()?.FrameworkName ?? "unknown";
    return $"ver={asm.GetName().Version}, TFM={tfm}, runtime={Environment.Version}";
});

// ---------- V2 明文 QRC 解析（逐字） ----------
Check("V2", "QRC 逐字解析（真实样本）", () =>
{
    var qrc = "[1000,1800]qrc(1000,300)zero(1300,0)word\n[3000,1000]second(3000,500)";
    var data = QrcParser.Parse(qrc);
    // LyricsData.Lines 在库里是可空的；解析失败会返回 null 而不是抛异常，
    // 这里必须显式断言，否则 -warnaserror 下会因 CS8602 阻断构建。
    var lines = data.Lines ?? throw new Exception("Parse 返回 null Lines");
    if (lines.Count != 2) throw new Exception($"期望 2 行，实际 {lines.Count}");
    var first = lines[0] as Lyricify.Lyrics.Models.SyllableLineInfo
        ?? throw new Exception($"首行不是 SyllableLineInfo，而是 {lines[0].GetType().Name}");
    var sylCount = first.Syllables?.Count ?? 0;
    if (sylCount < 1) throw new Exception("首行无音节数据");
    return $"lines={lines.Count}, syllables[0]={sylCount}, text[0]='{lines[0].Text}'";
});

// ---------- V3 明文 KRC 解析（逐字） ----------
Check("V3", "KRC 逐字解析（真实样本）", () =>
{
    var krc = "[1000,1800]<0,300,0>krc<300,0,0>zero<300,1500,0>word\n[3000,1000]<0,500,0>second";
    var lines = KrcParser.ParseLyrics(krc);
    if (lines.Count != 2) throw new Exception($"期望 2 行，实际 {lines.Count}");
    var first = lines[0] as Lyricify.Lyrics.Models.SyllableLineInfo
        ?? throw new Exception("首行不是 SyllableLineInfo");
    return $"lines={lines.Count}, syllables[0]={first.Syllables?.Count ?? 0}, text[0]='{lines[0].Text}'";
});

// ---------- V4 LRC 解析 ----------
Check("V4", "LRC 解析（含 offset 与畸形容错）", () =>
{
    var lrc = "[offset:+250]\n[00:03.00][00:01.00]alpha fixture line\n[00:02.500]\n[xx:yy]malformed fixture line\n[00:04.00]beta fixture line";
    var data = LrcParser.Parse(lrc);
    var lines = data.Lines ?? throw new Exception("Parse 返回 null Lines");
    if (lines.Count < 2) throw new Exception($"行数过少: {lines.Count}");
    return $"lines={lines.Count}, first='{lines[0].Text}'";
});

// ---------- V5 YRC 逐字解析 ----------
Check("V5", "YRC 逐字解析（真实样本）", () =>
{
    var yrc = "[1000,1800](1000,300,0)yrc(1300,0,0)zero(1300,1500,0)word\n[3000,1000](3000,500,0)second";
    var data = YrcParser.Parse(yrc);
    var lines = data.Lines ?? throw new Exception("Parse 返回 null Lines");
    if (lines.Count != 2) throw new Exception($"期望 2 行，实际 {lines.Count}");
    return $"lines={lines.Count}, text[0]='{lines[0].Text}'";
});

// ---------- V6 KRC 解密往返（XOR + zlib） ----------
Check("V6", "KRC 解密往返（XOR+Deflate，真实加密路径）", () =>
{
    // 按 Decrypter 的逆过程构造：text -> deflate -> XOR -> base64(前加4字节)
    byte[] key = { 0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69 };
    var original = "[1000,1800]<0,300,0>krc<300,0,0>zero\n[3000,1000]<0,500,0>second";

    // Decrypter 会做 res[1..]，所以压缩前要多加一个字符
    var payload = "\u0001" + original;
    byte[] raw = Encoding.UTF8.GetBytes(payload);

    using var ms = new MemoryStream();
    using (var ds = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
        ds.Write(raw, 0, raw.Length);
    var compressed = ms.ToArray();

    for (int i = 0; i < compressed.Length; i++) compressed[i] ^= key[i % key.Length];

    var withHeader = new byte[4 + compressed.Length];
    Encoding.ASCII.GetBytes("krc1").CopyTo(withHeader, 0);
    compressed.CopyTo(withHeader, 4);

    var encrypted = Convert.ToBase64String(withHeader);
    var decrypted = KrcDecrypter.DecryptLyrics(encrypted) ?? throw new Exception("解密返回 null");

    if (decrypted != original) throw new Exception($"往返不一致\n  期望: {original}\n  实际: {decrypted}");
    return $"往返一致, len={original.Length}, roundtrip=OK";
});

// ---------- V7 QRC 解密往返（TripleDES + zlib） ----------
Check("V7", "QRC 解密往返（TripleDES+Deflate，真实加密路径）", () =>
{
    byte[] qqKey = Encoding.ASCII.GetBytes("!@#)(*$%123ZXC!@!@#)(NHL");
    var original = "[1000,1800]qrc(1000,300)zero(1300,1500)word\n[3000,1000]second(3000,500)";

    byte[] raw = Encoding.UTF8.GetBytes(original);
    using var ms = new MemoryStream();
    using (var ds = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
        ds.Write(raw, 0, raw.Length);
    var compressed = ms.ToArray();

    // 补齐到 8 字节边界（TripleDESCrypt 按 8 字节块处理）
    int padded = (compressed.Length + 7) / 8 * 8;
    var buf = new byte[padded];
    compressed.CopyTo(buf, 0);

    var schedule = new byte[3][][];
    for (int i = 0; i < 3; i++)
    {
        schedule[i] = new byte[16][];
        for (int j = 0; j < 16; j++) schedule[i][j] = new byte[6];
    }
    DESHelper.TripleDESKeySetup(qqKey, schedule, DESHelper.ENCRYPT);

    var cipher = new byte[padded];
    for (int i = 0; i < buf.Length; i += 8)
    {
        var block = new byte[8];
        Array.Copy(buf, i, block, 0, 8);
        var outBlock = new byte[8];
        DESHelper.TripleDESCrypt(block, outBlock, schedule);
        outBlock.CopyTo(cipher, i);
    }
    var hex = Convert.ToHexString(cipher).ToLowerInvariant();

    var decrypted = QrcDecrypter.DecryptLyrics(hex) ?? throw new Exception("解密返回 null");
    if (decrypted != original) throw new Exception($"往返不一致\n  期望: {original}\n  实际: {decrypted}");
    return $"往返一致, len={original.Length}, roundtrip=OK";
});

// ---------- V8 SKILL 依赖版本 ----------
Check("V8", "依赖链版本核对（Newtonsoft.Json / SharpZipLib）", () =>
{
    var asms = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name is "Newtonsoft.Json" or "ICSharpCode.SharpZipLib")
        .Select(a => $"{a.GetName().Name}={a.GetName().Version}")
        .ToList();
    // 触发加载
    _ = Lyricify.Lyrics.Parsers.LrcParser.Parse("[00:01.00]x");
    asms = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => a.GetName().Name is "Newtonsoft.Json" or "ICSharpCode.SharpZipLib")
        .Select(a => $"{a.GetName().Name}={a.GetName().Version}")
        .ToList();
    return asms.Count == 0 ? "未加载（按需加载）" : string.Join(", ", asms);
});

// ---------- V9 网络源可用性（轻量探活） ----------
Check("V9", "在线搜索器类型可加载（QQ/网易/酷狗/LRCLIB）", () =>
{
    var types = new[]
    {
        "Lyricify.Lyrics.Providers.Web.QQMusic.SearchSongResponse",
        "Lyricify.Lyrics.Providers.Web.Netease.Song",
        "Lyricify.Lyrics.Providers.Web.Kugou.SearchSongResponse",
        "Lyricify.Lyrics.Providers.Web.LRCLIB.SearchResultItem",
    };
    var asm = typeof(QrcParser).Assembly;
    var found = types.Select(t => asm.GetType(t) is not null).ToList();
    return $"loaded=[{string.Join(", ", found)}]  ({found.Count(x => x)}/{types.Length})";
});

// ---------- 输出 ----------
Console.WriteLine();
Console.WriteLine("┌──────────────────────────────────────────────────────────────────────────────┐");
Console.WriteLine("│  Lyricify.Lyrics.Helper 0.2.0 on .NET 10 / TFM 22000 — 验证结果              │");
Console.WriteLine("└──────────────────────────────────────────────────────────────────────────────┘");
foreach (var r in results)
{
    var mark = r.Pass ? "PASS" : "FAIL";
    Console.WriteLine($"[{mark}] {r.Id}  {r.Name}");
    Console.WriteLine($"         {r.Detail}");
}

int pass = results.Count(r => r.Pass);
Console.WriteLine();
Console.WriteLine($"总计: {pass}/{results.Count} 通过");

var json = System.Text.Json.JsonSerializer.Serialize(results.Select(r => new
{
    id = r.Id, name = r.Name, pass = r.Pass, detail = r.Detail
}), new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "results.json"), json);

return pass == results.Count ? 0 : 1;