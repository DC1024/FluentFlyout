// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using System.Text;
using System.Text.RegularExpressions;
using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Utilities;

/// <summary>
/// 识别「混在歌词时间轴里、但不是歌词」的元信息行：开片标题卡（<c>歌名 - 歌手</c>）
/// 与片头/片尾的制作名单。
/// </summary>
/// <remarks>
/// <para>
/// 这些行在 QQ 音乐 QRC 里是带逐字时间轴的正式行，会像歌词一样被逐行显示：
/// 开片那张标题卡能占掉二三十秒，片尾名单有二三十行。它们还会把「整首定宽」撑到
/// 任务栏可用上限——只要有一行那种中英双语的录音名单，同一首歌里每一句的胶囊
/// 宽度都会跟着一起变宽，看起来就像「明明还有空间却显示不全」。
/// </para>
/// <para>
/// 判据分三层，逐层收紧：
/// <list type="number">
/// <item><b>强判据</b>——已知的制作标签（<c>词：</c>、<c>混音工程师 …：</c>、
/// <c>Mastering …：</c>）、整行括号的版权声明，以及开片 5 秒内与曲目信息匹配的标题卡。</item>
/// <item><b>弱判据</b>——只用来「吸收」落在元信息块内部的异形行（<c>Handclaps：…</c>、
/// <c>英文歌词翻译 …：</c>），必须左右相邻已经是元信息才生效。</item>
/// <item><b>区块判据</b>——只有<b>连续</b>、<b>触及文档首或尾</b>、且块内至少命中一条强判据
/// 的区块才会被剔除。歌曲中间偶然带冒号的一句歌词因此不会被误删。</item>
/// </list>
/// </para>
/// <para>
/// 判据在作者本机 68 首真实缓存（3120 行）上做过回归：剔除约 10% 的行，每一次
/// 剔除/保留的交界都落在真歌词上。判据只读 <see cref="ParsedLyricLine"/>，
/// 不改动持久化缓存，因此不影响既有歌词库的命中。
/// </para>
/// </remarks>
public static class LyricInformationLineFilter
{
    /// <summary>开片标题卡只在文档的前几条非空行里认。</summary>
    private const int OpeningLineLimit = 3;

    /// <summary>开片标题卡的起始时刻上限，与 <c>LocalLyricProvider.OpeningCreditWindow</c> 对齐。</summary>
    private static readonly TimeSpan OpeningWindow = TimeSpan.FromSeconds(5);

    /// <summary>超长行不再当元信息处理。中英双语的演员名单本身就能到 200 字以上。</summary>
    private const int MaxLineLength = 400;

    /// <summary>歌名/歌手允许比标题卡多出的字符数，用来容忍 <c>(Live)</c>、<c>（旧版）</c> 这类后缀。</summary>
    private const int TitleSlack = 8;

    private static readonly string[] CreditLabels =
    [
        "作词", "作曲", "编曲", "词曲", "词", "曲",
        "制作人", "制作总监", "制作助理", "制作协力", "制作统筹", "监制", "出品", "发行",
        "录音师", "录音室", "录音棚", "录音助理", "录音工程师",
        "混音师", "混音工程师", "混音工作室", "混音录音室",
        "母带后期工程师", "母带后期工作室", "母带后期处理工程师", "母带后期处理录音室",
        "母带工程师", "母带",
        "鼓技师", "和声编写", "和声编曲", "和声", "管乐编写", "管乐编程", "弦乐编写", "打击乐器",
        "小号", "长号", "次中音萨克斯风", "萨克斯风",
        "低音吉他", "电吉他", "木吉他", "滑音吉他", "吉他", "贝斯", "鼓", "键盘", "人声", "配唱",
        "版权声明", "版权", "著作权", "特别感谢",
    ];

    /// <summary>
    /// 中文（或「中文 + 英文」）制作标签。冒号前的部分允许是括号补充、英文名，
    /// 或者干脆什么都没有，覆盖 <c>作词：X</c>、<c>词 Lyricist：X</c>、
    /// <c>录音室 (铜管)：X</c> 三种写法。
    /// </summary>
    private static readonly Regex LabeledCreditRegex = new(
        @"^\s*(?:" + string.Join('|', CreditLabels) + @")\s*(?:"
        + @"(?:[（(][^）)]{0,40}[）)])\s*[^：:\r\n]{0,160}"
        + @"|[A-Za-z0-9][^：:\r\n]{0,160}"
        + @")?[：:]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>整行都是英文的制作标签。</summary>
    private static readonly Regex EnglishCreditRegex = new(
        @"^\s*(?:Lyricist|Composer|Arranger|Producer|Recording|Mixing|Mastering|Engineer|"
        + @"Studio|Conductor|Vocal|Chorus|Drums|Bass|Guitar|Keyboard|Handclaps|Copyright)"
        + @"\b[^：:\r\n]{0,120}[：:]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>整行被括号包住的版权声明，例如 <c>（未经著作人许可，不得翻唱、翻录或使用）</c>。</summary>
    private static readonly Regex ParenthesizedLineRegex = new(
        @"^[（(][^（）()]{1,200}[）)]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NoticeKeywordRegex = new(
        @"未经|著作权|版权|许可",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>歌名与歌手之间的分隔符。<c>-</c> 前后带空格，或横线后带空格。</summary>
    private static readonly Regex DashSeparatorRegex = new(
        @"\s[-–—－]\s?|[-–—－]\s",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>句读。带句读的行一律不当元信息，避免把正常歌词吃进来。</summary>
    private static readonly Regex SentencePunctuationRegex = new(
        @"[。！？!?]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>「左半段：右半段」的通用形状，只在元信息块内部才会被采信。</summary>
    private static readonly Regex GenericColonRegex = new(
        @"^\s*([^：:\r\n]{1,60})\s*[：:]\s*(\S.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 算出哪些行不是歌词。<c>true</c> 表示该行应当被剔除。
    /// </summary>
    /// <param name="lines">解析后的歌词行，顺序即显示顺序。</param>
    /// <param name="title">当前曲目的歌名，用于匹配开片标题卡；未知时传 <c>null</c>。</param>
    /// <param name="artist">当前曲目的歌手，用于匹配开片标题卡；未知时传 <c>null</c>。</param>
    public static bool[] BuildMask(
        IReadOnlyList<ParsedLyricLine> lines,
        string? title,
        string? artist)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var mask = new bool[lines.Count];

        // 空行不参与判据，也不剔除——它们既不是歌词也不是元信息，保持原样最省事，
        // 同时避免「末行是空行」把片尾区块判成「没触及文档末尾」。
        // ParsedLyricLine 本身已经拒收空白文本，这里是防御性的。
        var indices = new List<int>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i].Text))
            {
                indices.Add(i);
            }
        }

        var count = indices.Count;
        if (count == 0)
        {
            return mask;
        }

        var metadata = new bool[count];
        var strong = new bool[count];
        for (var k = 0; k < count; k++)
        {
            var line = lines[indices[k]];
            var text = line.Text.Trim();
            if (k < OpeningLineLimit &&
                line.StartTime >= TimeSpan.Zero &&
                line.StartTime <= OpeningWindow &&
                IsOpeningTitleCard(text, title, artist))
            {
                metadata[k] = true;
                strong[k] = true;
            }
            else if (IsCreditLine(text))
            {
                metadata[k] = true;
                strong[k] = true;
            }
        }

        AbsorbWeakLines(lines, indices, metadata);

        MarkHeadAndTailRuns(indices, metadata, strong, mask);
        return mask;
    }

    /// <summary>
    /// 把落在元信息块内部的异形行吸收进来。吸收是迭代的：吸收一行之后，
    /// 它旁边原本「不相邻」的行可能就变成相邻了。
    /// </summary>
    private static void AbsorbWeakLines(
        IReadOnlyList<ParsedLyricLine> lines,
        List<int> indices,
        bool[] metadata)
    {
        var count = indices.Count;
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var k = 0; k < count; k++)
            {
                if (metadata[k] || !IsWeakMetadataShape(lines[indices[k]].Text.Trim()))
                {
                    continue;
                }

                if ((k > 0 && metadata[k - 1]) || (k + 1 < count && metadata[k + 1]))
                {
                    metadata[k] = true;
                    changed = true;
                }
            }
        }
    }

    /// <summary>
    /// 只剔除「连续 + 触及首/尾 + 含强判据」的区块。这是全部误杀风险的兜底：
    /// 歌曲中间一句带冒号的歌词前后都不是元信息，构不成区块，因此一定被保留。
    /// </summary>
    private static void MarkHeadAndTailRuns(
        List<int> indices,
        bool[] metadata,
        bool[] strong,
        bool[] mask)
    {
        var count = indices.Count;
        var start = 0;
        while (start < count)
        {
            if (!metadata[start])
            {
                start++;
                continue;
            }

            var end = start;
            while (end + 1 < count && metadata[end + 1])
            {
                end++;
            }

            if (start == 0 || end == count - 1)
            {
                var hasStrongEvidence = false;
                for (var k = start; k <= end; k++)
                {
                    if (strong[k])
                    {
                        hasStrongEvidence = true;
                        break;
                    }
                }

                if (hasStrongEvidence)
                {
                    for (var k = start; k <= end; k++)
                    {
                        mask[indices[k]] = true;
                    }
                }
            }

            start = end + 1;
        }
    }

    /// <summary>
    /// 判断一行是不是已知的制作名单 / 版权声明。
    /// </summary>
    private static bool IsCreditLine(string text)
    {
        if (text.Length > MaxLineLength || SentencePunctuationRegex.IsMatch(text))
        {
            return false;
        }

        if (ParenthesizedLineRegex.IsMatch(text) && NoticeKeywordRegex.IsMatch(text))
        {
            return true;
        }

        return LabeledCreditRegex.IsMatch(text) || EnglishCreditRegex.IsMatch(text);
    }

    /// <summary>
    /// 判断一行是不是「开片标题卡」——曲名与歌手的拼贴。
    /// </summary>
    private static bool IsOpeningTitleCard(string text, string? title, string? artist)
    {
        var normalizedTitle = Normalize(title);
        if (normalizedTitle.Length == 0)
        {
            return false;
        }

        var normalizedArtist = Normalize(artist);
        var separator = DashSeparatorRegex.Match(text);
        if (separator.Success)
        {
            var left = Normalize(text[..separator.Index]);
            var right = Normalize(text[(separator.Index + separator.Length)..]);
            if (MatchesTitlePart(left, normalizedTitle) ||
                MatchesTitlePart(right, normalizedTitle) ||
                MatchesTitlePart(left, normalizedArtist) ||
                MatchesTitlePart(right, normalizedArtist))
            {
                return true;
            }
        }

        var normalizedLine = Normalize(text);
        if (normalizedArtist.Length > 0 &&
            (string.Equals(normalizedLine, normalizedTitle + normalizedArtist, StringComparison.Ordinal) ||
             string.Equals(normalizedLine, normalizedArtist + normalizedTitle, StringComparison.Ordinal)))
        {
            return true;
        }

        return MatchesTitlePart(normalizedLine, normalizedTitle);
    }

    /// <summary>
    /// 比较标题卡的一侧与歌名/歌手。完全相等即可；要放宽到「包含」则要求目标至少两个字，
    /// 否则单字歌名（<c>烧</c>）会把一句「燃烧 - …」判成标题卡。
    /// </summary>
    private static bool MatchesTitlePart(string candidate, string target)
    {
        if (candidate.Length == 0 || target.Length == 0)
        {
            return false;
        }

        if (string.Equals(candidate, target, StringComparison.Ordinal))
        {
            return true;
        }

        return target.Length >= 2 &&
               candidate.Length - target.Length <= TitleSlack &&
               candidate.Contains(target, StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断一行是不是「块内才可信」的元信息形状：短的左半段加冒号加非空右半段。
    /// </summary>
    private static bool IsWeakMetadataShape(string text)
    {
        if (text.Length > MaxLineLength || SentencePunctuationRegex.IsMatch(text))
        {
            return false;
        }

        var match = GenericColonRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var left = match.Groups[1].Value.Trim();
        return left.Length is >= 1 and <= 60;
    }

    /// <summary>只保留字母与数字并转小写，用来抵消空格、全半角标点与省略号的差异。</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
