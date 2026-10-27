// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。
// 选择优先级的语义对齐 FluentFlyout 的 MainWindow.GetActiveMediaSession()。

namespace FluentFlyout.Lyrics.Sessions;

/// <summary>
/// 一个媒体会话在选择策略眼里的最小信息。
/// 主工程负责从 Dubya 的 <c>MediaSession</c> 映射成这个结构，
/// 这样策略本身不依赖 Dubya，可以单测。
/// </summary>
/// <param name="Id">会话标识，与 <c>MediaSession.Id</c> 对应。</param>
/// <param name="IsAllowed">是否通过应用过滤（黑白名单）。被过滤的会话不参与选择。</param>
/// <param name="IsFocused">是否为系统当前聚焦的会话。</param>
/// <param name="IsPlaying">是否正在播放。</param>
public readonly record struct SessionCandidate(
    string Id,
    bool IsAllowed,
    bool IsFocused,
    bool IsPlaying);

/// <summary>
/// 会话选择策略。
///
/// 优先级（与 FluentFlyout 现有行为一致，改动必须同步两边）：
/// 1. 用户钉住的会话（若仍在且被允许）
/// 2. 系统聚焦的会话
/// 3. 第一个被允许的会话
///
/// 关于「正在播放」：FF 现有实现不把播放态纳入选择，因为多播放器场景下
/// 系统聚焦的通常就是用户在用的那个。这里保持同样行为，但把
/// <see cref="SessionCandidate.IsPlaying"/> 暴露出来，供上层在需要时
/// （比如纯音乐判定）自行判断，避免策略层擅自改变既定语义。
/// </summary>
public static class SessionSelectionPolicy
{
    /// <summary>
    /// 选出会话。返回 null 表示没有可用会话。
    /// </summary>
    /// <param name="candidates">候选会话，顺序应稳定（按会话发现顺序）。</param>
    /// <param name="pinnedSessionId">用户钉住的会话 Id，可为空。</param>
    /// <param name="pinnedSessionStale">
    /// 输出：钉住的会话是否已失效。为 true 时调用方应清空保存的 pinnedId，
    /// 与 FF 现有行为一致（否则会一直尝试匹配一个不存在的会话）。
    /// </param>
    public static SessionCandidate? Select(
        IEnumerable<SessionCandidate> candidates,
        string? pinnedSessionId,
        out bool pinnedSessionStale)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        pinnedSessionStale = false;

        var allowed = new List<SessionCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.IsAllowed)
            {
                allowed.Add(candidate);
            }
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(pinnedSessionId))
        {
            foreach (var candidate in allowed)
            {
                if (string.Equals(candidate.Id, pinnedSessionId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            // 钉住的会话不在了：标记失效，让调用方清掉，然后按默认优先级继续。
            pinnedSessionStale = true;
        }

        foreach (var candidate in allowed)
        {
            if (candidate.IsFocused)
            {
                return candidate;
            }
        }

        return allowed[0];
    }
}
