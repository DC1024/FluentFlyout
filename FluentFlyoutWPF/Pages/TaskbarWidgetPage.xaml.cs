// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarWidgetPage : Page
{
    /// <summary>
    /// 填充下拉框时抑制 <see cref="MonitorSelectionChanged"/>。
    /// 否则每次进页面都会把「当前解析出来的目标」当成用户的新选择写回设置——
    /// 目标屏被拔掉时，这一下就把用户的选择永久改成了主屏。
    /// </summary>
    private bool _suppressMonitorSelectionChange;

    /// <summary>
    /// 下拉框里各项对应的显示器快照。
    /// </summary>
    /// <remarks>
    /// 不能在 <see cref="MonitorSelectionChanged"/> 里重新枚举：那是第二次快照，
    /// 两次之间正好插拔显示器，下标就会指向另一块屏。
    /// </remarks>
    private IReadOnlyList<MonitorUtil.MonitorInfo> _monitorsInComboBox = [];

    public TaskbarWidgetPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;

        RefreshMonitorList();

        // 显示器可能在设置窗口关着的时候被插拔，每次进页面重新枚举一遍。
        Loaded += (_, _) => RefreshMonitorList();
    }

    /// <summary>
    /// 按「设备路径」填充目标显示器下拉框。
    /// </summary>
    /// <remarks>
    /// 只负责展示，**不回写设置**。保存的目标当前不在线时下拉框会落到主屏上，
    /// 但设置里仍然留着原来那块屏的路径——显示器插回来自动恢复。
    /// （代价：此时用户想「就选主屏」的话得先切到别的项再切回来，因为选中项没变化
    /// 不会触发 <see cref="MonitorSelectionChanged"/>。这是刻意选的方向：
    /// 宁可少一次「确认」，也不要悄悄抹掉用户的选择。）
    /// </remarks>
    private void RefreshMonitorList()
    {
        var monitors = MonitorUtil.GetMonitors();
        _monitorsInComboBox = monitors;

        var settings = SettingsManager.Current;
        var resolution = MonitorTargetResolver.Resolve(
            MonitorUtil.ToMonitorRefs(monitors),
            settings.TaskbarWidgetTargetMonitorId,
            settings.TaskbarWidgetTargetMonitorSignature,
            settings.TaskbarWidgetSelectedMonitor);

        _suppressMonitorSelectionChange = true;
        try
        {
            MonitorUtil.FillMonitorComboBox(
                TaskbarWidgetSelectedMonitorComboBox,
                monitors,
                resolution.Target?.DevicePath);
        }
        finally
        {
            _suppressMonitorSelectionChange = false;
        }
    }

    /// <summary>
    /// 用户选了另一块屏：写设备路径（主键）+ 签名（模糊重连），并同步旧的序号字段。
    /// </summary>
    /// <remarks>
    /// 写 <c>TaskbarWidgetTargetMonitorId</c> 会触发
    /// <c>UserSettings.OnTaskbarWidgetTargetMonitorIdChanged → ApplyTargetChange</c>，
    /// 所以小组件立刻就换屏，不用等 1.5s 定时器。
    /// </remarks>
    private void MonitorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonitorSelectionChange)
            return;

        int index = TaskbarWidgetSelectedMonitorComboBox.SelectedIndex;
        if (index < 0 || index >= _monitorsInComboBox.Count)
            return;

        var target = MonitorUtil.ToMonitorRef(_monitorsInComboBox[index]);
        var settings = SettingsManager.Current;

        if (string.Equals(settings.TaskbarWidgetTargetMonitorId, target.DevicePath, StringComparison.OrdinalIgnoreCase))
            return;

        // 先写签名再写主键：主键的变化会同步触发重排，
        // 那一刻签名必须已经是新目标的，否则模糊重连会读到上一条记录。
        settings.TaskbarWidgetTargetMonitorSignature = target.Signature;
        settings.TaskbarWidgetTargetMonitorId = target.DevicePath;

        // 兼容字段：老版本回读只认序号，跟着同步一份，免得两个字段互相矛盾。
        settings.TaskbarWidgetSelectedMonitor = index;
    }

    private void AppFiltering_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(AppFilteringPage));
    }
}
