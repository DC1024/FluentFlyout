// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Sessions;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 会话选择策略的测试。
///
/// 这里的优先级必须与 FluentFlyout 的 <c>MainWindow.GetActiveMediaSession()</c>
/// 完全一致——两边行为一旦分叉，用户会看到「歌词显示的是另一个播放器」。
/// </summary>
public sealed class SessionSelectionPolicyTests
{
    [Fact]
    public void EmptyCandidateListReturnsNull()
    {
        Assert.Null(SessionSelectionPolicy.Select([], null, out var stale));
        Assert.False(stale);
    }

    [Fact]
    public void AllCandidatesFilteredReturnsNull()
    {
        SessionCandidate[] candidates =
        [
            new("a", IsAllowed: false, IsFocused: true, IsPlaying: true),
            new("b", IsAllowed: false, IsFocused: false, IsPlaying: false)
        ];

        Assert.Null(SessionSelectionPolicy.Select(candidates, null, out var stale));
        Assert.False(stale);
    }

    [Fact]
    public void FocusedCandidateWinsOverTheFirstAllowed()
    {
        SessionCandidate[] candidates =
        [
            new("first", IsAllowed: true, IsFocused: false, IsPlaying: true),
            new("focused", IsAllowed: true, IsFocused: true, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, null, out var stale);

        Assert.Equal("focused", selected!.Value.Id);
        Assert.False(stale);
    }

    [Fact]
    public void FirstAllowedIsUsedWhenNothingIsFocused()
    {
        SessionCandidate[] candidates =
        [
            new("first", IsAllowed: true, IsFocused: false, IsPlaying: false),
            new("second", IsAllowed: true, IsFocused: false, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, null, out _);

        // 注意：播放态不参与选择，保持与 FF 现有行为一致。
        Assert.Equal("first", selected!.Value.Id);
    }

    [Fact]
    public void PinnedCandidateWinsOverFocused()
    {
        SessionCandidate[] candidates =
        [
            new("focused", IsAllowed: true, IsFocused: true, IsPlaying: true),
            new("pinned", IsAllowed: true, IsFocused: false, IsPlaying: false)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, "pinned", out var stale);

        Assert.Equal("pinned", selected!.Value.Id);
        Assert.False(stale);
    }

    [Fact]
    public void PinnedCandidateThatIsNoLongerAllowedIsReportedStale()
    {
        // 用户钉住的会话被应用过滤规则排除了，也要当作失效处理，
        // 否则会一直尝试匹配一个永远选不中的会话。
        SessionCandidate[] candidates =
        [
            new("pinned", IsAllowed: false, IsFocused: true, IsPlaying: true),
            new("other", IsAllowed: true, IsFocused: false, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, "pinned", out var stale);

        Assert.Equal("other", selected!.Value.Id);
        Assert.True(stale);
    }

    [Fact]
    public void MissingPinnedCandidateFallsBackToFocusedAndReportsStale()
    {
        SessionCandidate[] candidates =
        [
            new("first", IsAllowed: true, IsFocused: false, IsPlaying: true),
            new("focused", IsAllowed: true, IsFocused: true, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, "gone", out var stale);

        Assert.Equal("focused", selected!.Value.Id);
        Assert.True(stale);
    }

    [Fact]
    public void MissingPinnedCandidateFallsBackToFirstAllowedWhenNothingIsFocused()
    {
        SessionCandidate[] candidates =
        [
            new("first", IsAllowed: true, IsFocused: false, IsPlaying: false),
            new("second", IsAllowed: true, IsFocused: false, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, "gone", out var stale);

        Assert.Equal("first", selected!.Value.Id);
        Assert.True(stale);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyPinIsIgnored(string? pin)
    {
        SessionCandidate[] candidates =
        [
            new("focused", IsAllowed: true, IsFocused: true, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, pin, out var stale);

        Assert.Equal("focused", selected!.Value.Id);
        Assert.False(stale);
    }

    [Fact]
    public void PinMatchIsOrdinalNotCaseInsensitive()
    {
        // 会话 Id 由系统提供，大小写是标识的一部分，不能模糊匹配。
        SessionCandidate[] candidates =
        [
            new("Pinned", IsAllowed: true, IsFocused: false, IsPlaying: true),
            new("other", IsAllowed: true, IsFocused: true, IsPlaying: true)
        ];

        var selected = SessionSelectionPolicy.Select(candidates, "pinned", out var stale);

        Assert.Equal("other", selected!.Value.Id);
        Assert.True(stale);
    }

    [Fact]
    public void SelectionIsStableAcrossRepeatedCalls()
    {
        SessionCandidate[] candidates =
        [
            new("a", IsAllowed: true, IsFocused: false, IsPlaying: true),
            new("b", IsAllowed: true, IsFocused: false, IsPlaying: true),
            new("c", IsAllowed: true, IsFocused: false, IsPlaying: true)
        ];

        var first = SessionSelectionPolicy.Select(candidates, null, out _);
        var second = SessionSelectionPolicy.Select(candidates, null, out _);

        Assert.Equal(first!.Value.Id, second!.Value.Id);
    }

    [Fact]
    public void NullCandidatesThrows()
    {
        Assert.Throws<ArgumentNullException>(
            () => SessionSelectionPolicy.Select(null!, null, out _));
    }
}
