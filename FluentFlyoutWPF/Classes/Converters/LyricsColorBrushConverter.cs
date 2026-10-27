// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Lyrics.Layout;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Converters;

/// <summary>
/// 把歌词颜色的十六进制字符串转成 <see cref="SolidColorBrush"/>，供设置页预览块使用。
///
/// 解析逻辑复用 Core 的 <see cref="LyricsColorScheme.Parse"/>，
/// 不在这里另写一套——两处解析规则必须完全一致，否则会出现
/// 「预览块显示了颜色但任务栏没变」这种最难查的不一致。
///
/// 非法输入 / 空字符串返回 <see cref="Brushes.Transparent"/>：
/// 空字符串的语义是「跟随系统主题」，而主题色在设置页里无从得知，
/// 显示透明块（配合边框）比猜一个颜色更诚实。
/// </summary>
public class LyricsColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var scheme = LyricsColorScheme.Parse(value as string);
        if (!scheme.IsExplicit)
        {
            return Brushes.Transparent;
        }

        var argb = scheme.Argb;
        return new SolidColorBrush(Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF)));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}