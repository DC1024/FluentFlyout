// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Services;

public sealed class LyricProviderTrustPolicy
{
    public LyricProviderTrustPolicy(
        IEnumerable<LyricProviderId> trustOrder,
        IEnumerable<LyricProviderId> registeredProviders)
    {
        ArgumentNullException.ThrowIfNull(trustOrder);
        ArgumentNullException.ThrowIfNull(registeredProviders);

        var order = trustOrder.ToArray();
        var registered = registeredProviders.ToArray();
        var duplicate = order
            .GroupBy(provider => provider.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Trust order contains duplicate provider '{duplicate.Key}'.", nameof(trustOrder));
        }

        var registeredNames = registered
            .Select(provider => provider.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orderNames = order
            .Select(provider => provider.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = orderNames.Except(registeredNames, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (unknown is not null)
        {
            throw new ArgumentException($"Trust order contains unregistered provider '{unknown}'.", nameof(trustOrder));
        }

        var missing = registeredNames.Except(orderNames, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (missing is not null)
        {
            throw new ArgumentException($"Trust order omits registered provider '{missing}'.", nameof(trustOrder));
        }

        Order = order;
    }

    public IReadOnlyList<LyricProviderId> Order { get; }

    public static LyricProviderTrustPolicy CreateDefault(IEnumerable<LyricProviderId> registeredProviders) =>
        new(KnownLyricProviders.OnlineTrustOrder, registeredProviders);
}
