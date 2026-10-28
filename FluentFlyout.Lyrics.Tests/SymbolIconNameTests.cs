// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Wpf.Ui.Controls;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 门禁：XAML 与 C# 里引用的 WPF-UI 图标名，必须是 <see cref="SymbolRegular"/> 里真实存在的值。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要这条：<c>{ui:SymbolIcon AlignCenter24}</c> 里的名字在编译期只是**一个字符串**，
/// 被原样写进 BAML；要等到运行时 <c>LoadComponent</c> 走 <c>EnumConverter</c> 才解析，
/// 名字不存在就抛 <c>FormatException</c>。如果这个控件在页面构造期创建，异常会以 FATAL
/// 打崩整个进程——用户点开设置页直接闪退，而编译、单测、出包检查**全都碰不到**。
/// </para>
/// <para>
/// 这不是假设，已经发生过两次：<c>ecc272a</c>（对齐卡片，我自己写的）与
/// <c>SystemPage.xaml</c> 的 <c>EyeDropper24</c>（上游 2.16.1 带进来的，
/// 正确拼法是 <c>Eyedropper24</c>——只有一个字母的大小写之差）。
/// </para>
/// <para>
/// 为什么写成测试而不是 CI 脚本：<c>verify.yml</c> 已经会跑测试，写成 xunit 用例
/// 就自动进了现有的必需检查，不需要新增 workflow、也不需要额外的门禁 job。
/// 反过来说，**它必须能真的扫到文件、且三种写法都扫到**——扫到 0 个文件的“通过”是假绿，
/// 所以下面每种写法都有独立的数量下限，任何一种写法被漏掉都会当场失败。
/// </para>
/// </remarks>
public sealed partial class SymbolIconNameTests
{
    /// <summary>
    /// 写法一：标记扩展 <c>{ui:SymbolIcon …}</c>，参数部分单独再解析。
    ///
    /// <para>
    /// 不直接抓「<c>SymbolIcon</c> 后面的第一个名字」：这里图标有两种写法——
    /// 位置参数 <c>{ui:SymbolIcon Previous20, Filled=True}</c> 与
    /// 命名参数 <c>{ui:SymbolIcon Symbol=Speaker224}</c>。后者若按「第一个名字」取，
    /// 取到的是 <c>Symbol</c> 这个**参数名**，会报出根本不存在的图标。
    /// </para>
    /// </summary>
    [GeneratedRegex(@"\{ui:SymbolIcon(?![.\w])([^}]*)\}")]
    private static partial Regex XamlMarkupExtension();

    /// <summary>
    /// 写法二：元素形式 <c>&lt;ui:SymbolIcon Symbol="Add24"/&gt;</c>。
    ///
    /// <para>
    /// 这是仓库里更常见的写法（42 处 vs 标记扩展 106 处），但**不会被写法一的正则匹配到**——
    /// 少了这一条，绝大多数图标名等于没校验。排除 <c>&lt;ui:SymbolIcon.Style&gt;</c>
    /// 这类属性元素（它们不是图标实例）。
    /// </para>
    /// </summary>
    [GeneratedRegex(@"<ui:SymbolIcon(?!\.)([^>]*)>")]
    private static partial Regex XamlElement();

    /// <summary>
    /// 写法三：样式里设图标 <c>&lt;Setter Property="Symbol" Value="ArrowDownload24"/&gt;</c>。
    ///
    /// <para>
    /// 同样会在解析 XAML 时走 <c>EnumConverter</c>，同样能崩进程，但既不是标记扩展
    /// 也不是 <c>&lt;ui:SymbolIcon&gt;</c> 元素，前两条正则都抓不到。
    /// </para>
    /// </summary>
    [GeneratedRegex(@"<Setter\b[^>]*Property\s*=\s*""Symbol""[^>]*>")]
    private static partial Regex XamlStyleSetter();

    /// <summary>命名参数形式：<c>Symbol=Speaker224</c>。</summary>
    [GeneratedRegex(@"Symbol\s*=\s*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex NamedSymbolArgument();

    /// <summary>位置参数形式：参数串开头的裸名字。</summary>
    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex PositionalSymbolArgument();

    /// <summary>XML 属性形式：<c>Symbol="Add24"</c>。</summary>
    [GeneratedRegex(@"Symbol\s*=\s*""([^""]*)""")]
    private static partial Regex QuotedSymbolAttribute();

    /// <summary>Setter 的属性形式：<c>Value="Add24"</c>。</summary>
    [GeneratedRegex(@"Value\s*=\s*""([^""]*)""")]
    private static partial Regex QuotedValueAttribute();

    /// <summary>C# 里的强类型引用：<c>SymbolRegular.Play24</c>。</summary>
    [GeneratedRegex(@"SymbolRegular\.([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex CodeSymbolIconPattern();

    /// <summary>仓库里 XAML 文件数量的下限。低于这个数说明根目录找错了，门禁等于没跑。</summary>
    private const int MinimumXamlFiles = 30;

    /// <summary>
    /// 每种写法各自的解析数量下限。
    ///
    /// <para>
    /// 三个数字对应重构前的实际数量（106 / 42 / 2），留了余量。它们的作用不是「数得准」，
    /// 而是**一旦某条正则失效就立刻失败**——只有总数下限的话，一种写法归零、
    /// 另一种写法变多，总数照样过关，门禁就成了摆设。
    /// </para>
    /// </summary>
    private const int MinimumMarkupExtensions = 60;
    private const int MinimumElements = 20;
    private const int MinimumStyleSetters = 1;

    [Fact]
    public void ScansWholeRepository()
    {
        var root = FindRepositoryRoot();

        var xamlFiles = EnumerateSourceFiles(root, "*.xaml");
        var codeFiles = EnumerateSourceFiles(root, "*.cs");

        Assert.True(
            xamlFiles.Count >= MinimumXamlFiles,
            $"只扫到 {xamlFiles.Count} 个 XAML 文件（预期至少 {MinimumXamlFiles} 个），"
            + $"根目录判定可能错了：{root}。门禁扫不到文件就等于没有门禁。");

        Assert.NotEmpty(codeFiles);
    }

    [Fact]
    public void EveryXamlSymbolIconNameExists()
    {
        var root = FindRepositoryRoot();
        var known = KnownSymbolNames();
        var offenders = new List<string>();
        var markupCount = 0;
        var elementCount = 0;
        var setterCount = 0;

        foreach (var file in EnumerateSourceFiles(root, "*.xaml"))
        {
            var text = File.ReadAllText(file);
            var relative = Path.GetRelativePath(root, file);

            // 写法一：{ui:SymbolIcon ...}
            foreach (Match match in XamlMarkupExtension().Matches(text))
            {
                foreach (var name in ExtractIconNames(match.Groups[1].Value))
                {
                    markupCount++;
                    if (!known.Contains(name))
                    {
                        offenders.Add($"{relative}:{LineOf(text, match.Index)} → {name}");
                    }
                }
            }

            // 写法二：<ui:SymbolIcon Symbol="..."/>
            foreach (Match match in XamlElement().Matches(text))
            {
                var name = ReadSymbolAttribute(match.Groups[1].Value);
                if (name is null)
                {
                    continue;
                }

                elementCount++;
                if (!known.Contains(name))
                {
                    offenders.Add($"{relative}:{LineOf(text, match.Index)} → {name}");
                }
            }

            // 写法三：<Setter Property="Symbol" Value="..."/>
            foreach (Match match in XamlStyleSetter().Matches(text))
            {
                var name = ReadQuotedValue(QuotedValueAttribute(), match.Value);
                if (name is null)
                {
                    continue;
                }

                setterCount++;
                if (!known.Contains(name))
                {
                    offenders.Add($"{relative}:{LineOf(text, match.Index)} → {name}");
                }
            }
        }

        Assert.True(
            markupCount >= MinimumMarkupExtensions,
            $"标记扩展写法只解析出 {markupCount} 个图标名（下限 {MinimumMarkupExtensions}），"
            + "正则可能已经跟不上 XAML 写法。");

        Assert.True(
            elementCount >= MinimumElements,
            $"元素写法只解析出 {elementCount} 个图标名（下限 {MinimumElements}），"
            + "元素形式的正则可能失效了——这是仓库里最常见的写法，漏掉它门禁形同虚设。");

        Assert.True(
            setterCount >= MinimumStyleSetters,
            $"Setter 写法只解析出 {setterCount} 个图标名（下限 {MinimumStyleSetters}），"
            + "Setter 形式的正则可能失效了。");

        Assert.True(
            offenders.Count == 0,
            "以下 XAML 里的图标名不在 SymbolRegular 中。编译期不会报错，"
            + "但运行时 LoadComponent 会抛 FormatException；若发生在页面构造期会直接崩进程：\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryCodeSymbolRegularNameExists()
    {
        var root = FindRepositoryRoot();
        var known = KnownSymbolNames();
        var offenders = new List<string>();

        foreach (var file in EnumerateSourceFiles(root, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in CodeSymbolIconPattern().Matches(lines[i]))
                {
                    var name = match.Groups[1].Value;
                    if (!known.Contains(name))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1} → {name}");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "以下 C# 里的 SymbolRegular 成员不存在：\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// 从一个标记扩展的参数串里取出**要校验的图标名**。
    ///
    /// <para>
    /// 三种情况：命名参数（只校验 <c>Symbol=</c> 的值）、位置参数（校验开头的裸名字）、
    /// 以及 <c>Symbol={StaticResource X}</c> 这类动态绑定——最后一种无从静态校验，直接跳过。
    /// </para>
    /// </summary>
    private static IEnumerable<string> ExtractIconNames(string arguments)
    {
        var named = NamedSymbolArgument().Matches(arguments);
        if (named.Count > 0)
        {
            foreach (Match match in named)
            {
                yield return match.Groups[1].Value;
            }

            yield break;
        }

        // 带等号却没有合法的 Symbol= 值，说明值本身是嵌套表达式，静态校验不了。
        if (arguments.Contains('=', StringComparison.Ordinal))
        {
            yield break;
        }

        var positional = PositionalSymbolArgument().Match(arguments);
        if (positional.Success)
        {
            yield return positional.Groups[1].Value;
        }
    }

    /// <summary>
    /// 从元素属性串里读 <c>Symbol="…"</c>。
    /// 值形如 <c>{Binding …}</c> / <c>{DynamicResource …}</c> 时返回 <see langword="null"/>——
    /// 运行时才知道是哪个图标，静态校验不了（<c>VolumeMixerWindow</c> 的音量图标就是这种）。
    /// </summary>
    private static string? ReadSymbolAttribute(string attributes) =>
        ReadQuotedValue(QuotedSymbolAttribute(), attributes);

    private static string? ReadQuotedValue(Regex pattern, string source)
    {
        var match = pattern.Match(source);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim();
        if (value.Length == 0 || value.Contains('{', StringComparison.Ordinal))
        {
            return null;
        }

        return value;
    }

    /// <summary>把字符下标换算成从 1 开始的行号，报错信息才能直接跳转。</summary>
    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>
    /// 枚举的全部成员名。
    ///
    /// <para>
    /// 用反射而不是 grep 程序集：<c>SymbolRegular</c> 有九千多个值，
    /// 在 DLL 上 grep 字符串会误判（名字与字段名并不总是一一对应），
    /// 而 <c>Enum.TryParse</c> 走的正是运行时 <c>EnumConverter</c> 的同一条判定。
    /// </para>
    /// </summary>
    private static HashSet<string> KnownSymbolNames()
    {
        var names = Enum.GetNames<SymbolRegular>();
        Assert.True(names.Length > 1000, $"SymbolRegular 只有 {names.Length} 个值，枚举加载可能不对。");
        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    /// <summary>
    /// 从测试程序集所在目录向上找到仓库根。
    ///
    /// <para>
    /// 判据是同时存在 <c>FluentFlyoutWPF</c> 与 <c>.github</c>：只看前者的话，
    /// 万一哪天有人把工程目录挪到别处，向上找会停在错误层级却仍然“成立”。
    /// </para>
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "FluentFlyoutWPF")) &&
                Directory.Exists(Path.Combine(dir.FullName, ".github")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"从 {AppContext.BaseDirectory} 向上找不到仓库根（判据：同时存在 FluentFlyoutWPF 与 .github）。"
            + "测试工程的输出目录结构变了就要同步改这里，否则门禁会静默失效。");
    }

    /// <summary>
    /// 枚举仓库里的源文件，跳过 <c>bin</c> / <c>obj</c>。
    ///
    /// <para>
    /// 必须跳过：<c>obj</c> 里有生成的 XAML（<c>*.g.cs</c>、<c>*.baml</c> 的中间产物），
    /// 扫进去会把生成代码当成用户代码，报一堆无从修起的错。
    /// </para>
    /// </summary>
    private static List<string> EnumerateSourceFiles(string root, string pattern) =>
        Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutput(path))
            .ToList();

    private static bool IsUnderBuildOutput(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }
}
