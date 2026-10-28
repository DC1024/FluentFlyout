// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyoutWPF.ViewModels;
using System.Xml.Serialization;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 设置迁移测试：旧版 <c>settings.xml</c> 里没有歌词字段时，反序列化必须成功
/// 且新字段取到「关」的默认值。
///
/// 为什么值得单独测：<c>UserSettings</c> 的持久化完全依赖
/// <see cref="XmlSerializer"/> 的「缺失元素 → 静默取类型默认值」行为。
/// 这条行为在两种情况会失效：
/// <list type="number">
/// <item>新字段声明成非空引用类型且没有默认值 → 缺失时是 <c>null</c>，后续 NRE；</item>
/// <item>新字段的类型没有无参构造 → 反序列化直接抛异常，用户**整份设置丢失**
/// （<c>SettingsManager.RestoreSettings</c> 会 fallback 到全新默认值）。</item>
/// </list>
/// 这两点都不会被编译期检查发现，只能靠真的反序列化一份旧文件来证。
/// </summary>
public class UserSettingsMigrationTests
{
    /// <summary>
    /// 一份「P3 时代」的配置：所有歌词字段都不存在。
    /// 字段取自真实 settings.xml 的形状（属性名即元素名，顺序无关）。
    /// </summary>
    private const string LegacySettingsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <UserSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <AppLanguage>zh-CN</AppLanguage>
          <AppTheme>0</AppTheme>
          <TaskbarWidgetEnabled>true</TaskbarWidgetEnabled>
          <TaskbarWidgetPosition>2</TaskbarWidgetPosition>
          <TaskbarWidgetManualPadding>12</TaskbarWidgetManualPadding>
          <TaskbarWidgetScrollingTextSpeed>35</TaskbarWidgetScrollingTextSpeed>
          <TaskbarVisualizerEnabled>false</TaskbarVisualizerEnabled>
        </UserSettings>
        """;

    private static UserSettings Deserialize(string xml)
    {
        using var reader = new StringReader(xml);
        var serializer = new XmlSerializer(typeof(UserSettings));
        var settings = (UserSettings?)serializer.Deserialize(reader);
        Assert.NotNull(settings);
        return settings;
    }

    [Fact]
    public void LegacySettingsWithoutLyricsFieldsDeserializeSuccessfully()
    {
        // 关键断言只有一条：不抛异常。抛了就意味着老用户升级后设置全丢。
        var settings = Deserialize(LegacySettingsXml);

        // 顺带确认老字段没被新字段影响。
        Assert.True(settings.TaskbarWidgetEnabled);
        Assert.Equal(2, settings.TaskbarWidgetPosition);
        Assert.Equal(12, settings.TaskbarWidgetManualPadding);
        Assert.Equal(35, settings.TaskbarWidgetScrollingTextSpeed);
    }

    [Fact]
    public void LyricsFieldsDefaultToDisabledWhenAbsent()
    {
        var settings = Deserialize(LegacySettingsXml);

        // 默认必须是「关」：歌词会抢任务栏空间并引入网络请求，
        // 老用户升级后不该发现任务栏突然变了样。
        Assert.False(settings.LyricsEnabled);

        // 表现类默认值取「常见偏好」，不是 false——
        // 用户开了歌词却没逐字高亮会觉得「没生效」。
        Assert.True(settings.LyricsShowTranslation);
        Assert.True(settings.LyricsWordScanEnabled);
        Assert.Equal(13, settings.LyricsFontSize);
        Assert.Equal(0, settings.LyricsOffsetMilliseconds);

        // P4-4 新增的三项。
        // 单行默认关（= 自动，装不下时系统自己降级）。
        Assert.False(settings.LyricsSingleLine);
        // 空字符串 = 跟随系统主题。**不能是 null**：
        // 空引用会被 LyricsColorScheme.Parse 正确当成 FollowSystem，
        // 但设置页的 TextBox 绑 null 会走一遍转换，不如空串干净。
        Assert.Equal(string.Empty, settings.LyricsColor);
        // 0 = 左对齐，与 LyricsTextAlignment.Left 一致。
        Assert.Equal(0, settings.LyricsAlignment);
    }

    [Fact]
    public void LyricsFieldsSurviveRoundTrip()
    {
        var original = new UserSettings
        {
            LyricsEnabled = true,
            LyricsFontSize = 17,
            LyricsShowTranslation = false,
            LyricsWordScanEnabled = false,
            LyricsSingleLine = true,
            LyricsColor = "#FF5722",
            LyricsAlignment = 2,
            TaskbarWidgetEnabled = true,
        };

        var serializer = new XmlSerializer(typeof(UserSettings));
        using var writer = new StringWriter();
        serializer.Serialize(writer, original);
        var xml = writer.ToString();

        var restored = Deserialize(xml);

        Assert.True(restored.LyricsEnabled);
        Assert.Equal(17, restored.LyricsFontSize);
        Assert.False(restored.LyricsShowTranslation);
        Assert.False(restored.LyricsWordScanEnabled);

        // P4-4 三项：颜色与对齐是**真用户偏好**，必须持久化
        // （与 LyricsOffsetMilliseconds 相反，那个按曲目存在 SQLite）。
        Assert.True(restored.LyricsSingleLine);
        Assert.Equal("#FF5722", restored.LyricsColor);
        Assert.Equal(2, restored.LyricsAlignment);
    }

    [Fact]
    public void LyricsColorSurvivesAsRawString()
    {
        // 颜色必须以字符串原样存取，不能在中途被归一化成 #AARRGGBB：
        // 用户写 #FF5722，回填时看到 #FFFF5722 会以为设置被改了。
        var serializer = new XmlSerializer(typeof(UserSettings));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new UserSettings { LyricsColor = "f0a" });
        var xml = writer.ToString();

        Assert.Contains("<LyricsColor>f0a</LyricsColor>", xml, StringComparison.Ordinal);
        Assert.Equal("f0a", Deserialize(xml).LyricsColor);
    }

    [Fact]
    public void DerivedTextPropertiesAreNotSerialized()
    {
        // LyricsFontSizeText / LyricsOffsetText 是给 UI 的派生属性，
        // 带 setter 会被 XmlSerializer 当成可写属性一起存进去。
        // 存进去的后果：读回来时 setter 跑一遍校验与 clamp，
        // 与主字段竞争，出现「存的是 17，读回来是 20」这类怪事。
        var serializer = new XmlSerializer(typeof(UserSettings));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new UserSettings { LyricsFontSize = 17 });
        var xml = writer.ToString();

        Assert.DoesNotContain("LyricsFontSizeText", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("LyricsOffsetText", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// 目标显示器的两个新字段在旧配置里不存在时，必须是**空串**而不是 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 空串的语义是「跟随主屏」，<c>null</c> 的语义是「有人忘了初始化」。
    /// 后者不会当场炸，而是会在 <c>MonitorUtil.FillMonitorComboBox</c> 或
    /// <c>MonitorTargetResolver.Resolve</c> 的某条分支上炸——离这里很远，
    /// 排查成本全在「为什么设置页打不开」上。
    /// </remarks>
    [Fact]
    public void DisplayTargetFieldsDefaultToEmptyStringWhenAbsent()
    {
        var settings = Deserialize(LegacySettingsXml);

        Assert.Equal(string.Empty, settings.TaskbarWidgetTargetMonitorId);
        Assert.Equal(string.Empty, settings.TaskbarWidgetTargetMonitorSignature);
    }

    /// <summary>
    /// 新字段要能正常往返：写进去、序列化、再反序列化，设备路径不能变形。
    /// </summary>
    /// <remarks>
    /// 设备路径里有 <c>#</c>、<c>&amp;</c>、<c>?</c> 这些 XML 敏感字符
    /// （<c>\\?\DISPLAY#TCL2702#5&amp;2ec52e58&amp;0&amp;UID4355</c>），
    /// 一旦被当成标记/实体处理，读回来的就是另一块屏的路径——而它**看起来**仍然像个合法路径，
    /// 只会表现为「每次启动都要重新选显示器」。
    /// </remarks>
    [Fact]
    public void DisplayTargetDevicePathRoundTripsThroughXml()
    {
        const string devicePath = @"\\?\DISPLAY#TCL2702#5&2ec52e58&0&UID4355";
        const string signature = "R27Q71|2560x1440|0,0";

        var serializer = new XmlSerializer(typeof(UserSettings));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new UserSettings
        {
            TaskbarWidgetTargetMonitorId = devicePath,
            TaskbarWidgetTargetMonitorSignature = signature,
        });

        var restored = Deserialize(writer.ToString());

        Assert.Equal(devicePath, restored.TaskbarWidgetTargetMonitorId);
        Assert.Equal(signature, restored.TaskbarWidgetTargetMonitorSignature);
    }

    [Fact]
    public void OffsetIsNotPersistedToSettingsXml()
    {
        // 偏移真值在 TrackLyricOffsetStore 的 SQLite 里（按曲目 + 歌词源存）。
        // 如果它进了 settings.xml，重启后会在 CompleteInitialization 之前
        // 把上一首的偏移灌进内存，表现为「重启后歌词整体偏了，切一次歌才好」。
        var serializer = new XmlSerializer(typeof(UserSettings));
        using var writer = new StringWriter();
        serializer.Serialize(writer, new UserSettings { LyricsOffsetMilliseconds = 250 });
        var xml = writer.ToString();

        Assert.DoesNotContain("LyricsOffsetMilliseconds", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptedLyricsValuesFallBackInsteadOfThrowing()
    {        // 手改配置文件 / 跨版本回退都可能造出越界值。
        // 反序列化本身不会校验范围（XmlSerializer 只管类型），
        // 但读取后不能留下一个「会让面板算出负行高」的脏值——
        // 校验发生在 setter 与 LyricsPanelMetrics.Create 两处。
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UserSettings>
              <LyricsFontSize>999</LyricsFontSize>
            </UserSettings>
            """;

        var settings = Deserialize(xml);

        // XmlSerializer 直写 backing field（不经 setter），所以这里可能是 999。
        // 断言的是「下游能兜住」：LyricsPanelMetrics.Create 会把非法字号压回可用值。
        var metrics = FluentFlyout.Lyrics.Layout.LyricsPanelMetrics.Create(
            availableHeight: 40,
            preferredFontSize: settings.LyricsFontSize);

        Assert.True(metrics.FontSize >= 9);
        Assert.True(metrics.LineHeight > 0);
    }
}
