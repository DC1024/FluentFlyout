// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using System.Globalization;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using FluentFlyout.Lyrics.Utilities;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// <see cref="LyricInformationLineFilter"/> 与投影层的用例。
///
/// <para>
/// 夹具全部取自作者本机真实缓存（QQ 音乐 QRC）里出现过的行，包括：
/// 单字歌名「烧」、带 <c>(Live)</c> 后缀的标题卡、翻唱歌手与原唱不一致的标题卡、
/// 中英双语的录音名单、以及块内异形行 <c>Handclaps：…</c>。
/// </para>
/// <para>
/// 最重要的一组断言是「不该删的没被删」：歌曲中间一句带冒号的歌词、单字歌名的
/// 近似行、以及不知道曲目信息时的标题卡。
/// </para>
/// </summary>
public class LyricInformationLineFilterTests
{
    private static ParsedLyricLine Line(double seconds, string text) =>
        new(TimeSpan.FromSeconds(seconds), null, text);

    private static bool[] Mask(
        IReadOnlyList<ParsedLyricLine> lines,
        string? title = null,
        string? artist = null) =>
        LyricInformationLineFilter.BuildMask(lines, title, artist);

    // ── 开片标题卡 ────────────────────────────────────────────────────

    [Fact]
    public void RemovesOpeningTitleCardForMatchingTitleAndArtist()
    {
        var lines = new[]
        {
            Line(0, "浪人的… - 张震岳"),
            Line(27.898, "沙滩黄昏的色温 即将入夜的时分"),
            Line(33, "原来是我太认真"),
        };

        Assert.Equal([true, false, false], Mask(lines, "浪人的…", "张震岳"));
    }

    /// <summary>
    /// 翻唱版本的标题卡写的是翻唱歌手，与曲目元数据里的歌手对不上，
    /// 但冒号左边与歌名完全一致，仍然要认出来。
    /// </summary>
    [Fact]
    public void RemovesOpeningTitleCardWhenArtistDiffersFromMetadata()
    {
        var lines = new[]
        {
            Line(0, "一直很安静 - 笨老猫"),
            Line(6.8, "空荡的街景想找个人放感情"),
        };

        Assert.Equal([true, false], Mask(lines, "一直很安静", "那只猫"));
    }

    /// <summary>单字歌名（「烧」）不能因为「太短」被最短长度门槛挡在门外。</summary>
    [Fact]
    public void RemovesOpeningTitleCardForSingleCharacterTitle()
    {
        var lines = new[]
        {
            Line(0, "烧 - 林忆莲 (Sandy Lam)"),
            Line(33.053, "我像困于火圈中"),
        };

        Assert.Equal([true, false], Mask(lines, "烧", "林忆莲"));
    }

    /// <summary>标题卡带 <c>(Live)</c> / <c>（旧版）</c> 这类后缀时仍应命中。</summary>
    [Fact]
    public void RemovesOpeningTitleCardWithVersionSuffix()
    {
        var lines = new[]
        {
            Line(0.633, "今天你要嫁给我 (Live) - 蔡依林/陶喆"),
            Line(3.481, "词：陶喆/娃娃"),
            Line(5.779, "编曲：陶喆"),
            Line(23.141, "春暖的花开带走冬天的感伤"),
        };

        Assert.Equal([true, true, true, false], Mask(lines, "今天你要嫁给我", "蔡依林"));
    }

    /// <summary>
    /// 不知道曲目信息时不能凭形状猜标题卡——「A - B」也是正常歌词的一种写法。
    /// </summary>
    [Fact]
    public void KeepsTitleCardWhenTrackMetadataIsUnknown()
    {
        var lines = new[]
        {
            Line(0, "浪人的… - 张震岳"),
            Line(27.898, "沙滩黄昏的色温 即将入夜的时分"),
        };

        Assert.Equal([false, false], Mask(lines));
    }

    /// <summary>开片窗口之外（首句在 33 秒才开口）不按标题卡处理。</summary>
    [Fact]
    public void KeepsDashLineOutsideOpeningWindow()
    {
        var lines = new[]
        {
            Line(33, "烧 - 林忆莲"),
            Line(40, "我像困于火圈中"),
        };

        Assert.Equal([false, false], Mask(lines, "烧", "林忆莲"));
    }

    /// <summary>
    /// 单字歌名只允许「完全相等」，不允许「包含」。否则一句「燃烧 - 说出口」
    /// 会被当成「烧」的标题卡删掉。
    /// </summary>
    [Fact]
    public void KeepsDashLineThatMerelyContainsASingleCharacterTitle()
    {
        var lines = new[]
        {
            Line(0, "燃烧 - 说出口"),
            Line(10, "我像困于火圈中"),
        };

        Assert.Equal([false, false], Mask(lines, "烧", "林忆莲"));
    }

    // ── 制作名单 ──────────────────────────────────────────────────────

    [Fact]
    public void RemovesOpeningCreditBlock()
    {
        var lines = new[]
        {
            Line(0, "坏女孩 - 徐良"),
            Line(0.5, "词：徐良"),
            Line(1.2, "曲：徐良"),
            Line(10, "被爱的深夜我在想念"),
        };

        Assert.Equal([true, true, true, false], Mask(lines, "坏女孩", "徐良"));
    }

    [Fact]
    public void RemovesTrailingCreditBlock()
    {
        var lines = new[]
        {
            Line(10, "沙滩黄昏的色温 即将入夜的时分"),
            Line(240, "原来是我太认真"),
            Line(245, "词 Lyricist：张震岳 Ayal Komod"),
            Line(250, "母带后期工作室 Mastering Studio：全区通行录音室 All Access Studio"),
        };

        Assert.Equal([false, false, true, true], Mask(lines, "浪人的…", "张震岳"));
    }

    /// <summary>
    /// 中英双语的演员名单能到 130 字以上，正是把「整首定宽」顶到任务栏上限的那一行。
    /// </summary>
    [Fact]
    public void RemovesLongBilingualCreditLine()
    {
        var lines = new[]
        {
            Line(10, "原来是我太认真"),
            Line(230, "录音助理 (鼓/低音吉他/吉他) Recording Assistants "
                + "(Drums/Bass Guitar/Electric Guitar)：于世政 Shih Cheng Yu/陈佳颖 Chia Ying Chen/相云 Camille Chen"),
        };

        Assert.Equal([false, true], Mask(lines, "浪人的…", "张震岳"));
    }

    /// <summary>
    /// 块内的异形行（不在标签表里）要跟着整块一起走，否则它会成为唯一的残留，
    /// 也会把连续块从中间切断。
    /// </summary>
    [Fact]
    public void AbsorbsIrregularLineInsideCreditBlock()
    {
        var lines = new[]
        {
            Line(0, "一点点 - 陶喆"),
            Line(0.337, "词 Lyricist：陶喆 David Tao"),
            Line(2.865, "Handclaps：DT/Brandy/Evan"),
            Line(3.09, "低音吉他 Bass：Paul Bushnell"),
            Line(10.954, "Just a Little Bit Just a Little Bit"),
        };

        Assert.Equal([true, true, true, true, false], Mask(lines, "一点点", "陶喆"));
    }

    /// <summary>整行被括号包住的版权声明，出现在曲末最后一行。</summary>
    [Fact]
    public void RemovesParenthesizedCopyrightNoticeAtTail()
    {
        var lines = new[]
        {
            Line(10, "我像困于火圈中"),
            Line(240, "（未经著作人许可，不得翻唱、翻录或使用）"),
        };

        Assert.Equal([false, true], Mask(lines, "烧", "林忆莲"));
    }

    // ── 不该删的 ──────────────────────────────────────────────────────

    /// <summary>
    /// 歌曲中间一句带冒号的歌词前后都不是元信息，构不成「触及首尾的连续块」，
    /// 所以一定保留。这是整个判据最重要的兜底。
    /// </summary>
    [Fact]
    public void KeepsColonLineInTheMiddleOfLyrics()
    {
        var lines = new[]
        {
            Line(0, "我像困于火圈中"),
            Line(10, "我说：别走"),
            Line(20, "离不开 留不低"),
        };

        Assert.Equal([false, false, false], Mask(lines, "烧", "林忆莲"));
    }

    /// <summary>
    /// 整份文档都是元信息时由投影层回退，判据本身照样全标 <c>true</c>。
    /// </summary>
    [Fact]
    public void MarksEveryLineWhenDocumentOnlyHoldsMetadata()
    {
        var lines = new[] { Line(0, "作词：某人") };

        Assert.Equal([true], Mask(lines, "纯音乐", "某人"));
    }

    // ── 真实数据回归 ──────────────────────────────────────────────────

    /// <summary>
    /// 夹具是真实 QQ 音乐 QRC《浪人的…》的全部 69 行：第 0 行是开片标题卡，
    /// 第 41 行起是 28 行制作名单，中间 40 行是真歌词。
    ///
    /// <para>
    /// 这一条锁的是「判据在真实数据上的整体表现」，而不是某个单点规则：
    /// 判据改动只要多删或少删一行，这里就会响。
    /// </para>
    /// </summary>
    [Fact]
    public void RemovesTitleCardAndCreditBlockFromRealTrack()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Lyrics", "qq-credit-block.txt");
        var lines = File.ReadAllLines(path)
            .Where(row => row.Length > 0 && !row.StartsWith('#'))
            .Select(row =>
            {
                var separator = row.IndexOf('|');
                return new ParsedLyricLine(
                    TimeSpan.Parse(row[..separator], CultureInfo.InvariantCulture),
                    null,
                    row[(separator + 1)..]);
            })
            .ToArray();

        Assert.Equal(69, lines.Length);

        var mask = Mask(lines, "浪人的…", "张震岳");
        var kept = lines.Where((_, index) => !mask[index]).ToArray();

        Assert.Equal(40, kept.Length);
        Assert.Equal("沙滩黄昏的色温 即将入夜的时分", kept[0].Text);
        Assert.Equal("原来是我太认真", kept[^1].Text);
    }

    // ── 投影层 ────────────────────────────────────────────────────────

    private static ResolvedLyrics CreateResolved(params ParsedLyricLine[] lines) =>
        new(
            new ParsedLyrics(
                lines,
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.Qrc),
            new LyricProviderId("QQMusic"),
            "candidate",
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>());

    private static readonly TrackInfo Track = new(
        "track-id",
        "浪人的…",
        "张震岳",
        string.Empty,
        "cloudmusic.exe",
        TimeSpan.FromMinutes(4));

    [Fact]
    public void ProjectorRemovesInformationLinesWhenTrackIsKnown()
    {
        var resolved = CreateResolved(
            Line(0, "浪人的… - 张震岳"),
            Line(27.898, "沙滩黄昏的色温 即将入夜的时分"),
            Line(240, "母带后期工作室 Mastering Studio：全区通行录音室 All Access Studio"));

        var filtered = ResolvedLyricsCompatibilityProjector.ToLyricDocument(
            resolved,
            Track,
            includeInformationLines: false);

        var line = Assert.Single(filtered.Lines);
        Assert.Equal("沙滩黄昏的色温 即将入夜的时分", line.Text);
    }

    [Fact]
    public void ProjectorKeepsInformationLinesByDefault()
    {
        var resolved = CreateResolved(
            Line(0, "浪人的… - 张震岳"),
            Line(27.898, "沙滩黄昏的色温 即将入夜的时分"));

        Assert.Equal(2, ResolvedLyricsCompatibilityProjector.ToLyricDocument(resolved, Track).Lines.Count);
        Assert.Equal(2, ResolvedLyricsCompatibilityProjector.ToLyricDocument(resolved).Lines.Count);
    }

    /// <summary>
    /// 剔干净会让歌词面板变空，所以必须退回原始行——纯音乐曲目只有一句
    /// 「纯音乐，请欣赏」时不能把整首吞掉。
    /// </summary>
    [Fact]
    public void ProjectorFallsBackWhenEveryLineLooksInformational()
    {
        var resolved = CreateResolved(Line(0, "作词：某人"));

        var filtered = ResolvedLyricsCompatibilityProjector.ToLyricDocument(
            resolved,
            Track,
            includeInformationLines: false);

        Assert.Single(filtered.Lines);
    }
}
