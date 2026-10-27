// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace TaskbarLyrics.Core.Services;

internal static class LyricTransientFailurePolicy
{
    public static bool IsTransient(Exception exception) => exception switch
    {
        QqMusicApiException qq => qq.IsTransient,
        TimeoutException => true,
        SocketException socket => socket.SocketErrorCode is
            SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData or
            SocketError.TimedOut or SocketError.ConnectionReset or SocketError.ConnectionAborted or
            SocketError.ConnectionRefused or SocketError.NetworkDown or SocketError.NetworkUnreachable or
            SocketError.HostDown or SocketError.HostUnreachable,
        HttpRequestException { StatusCode: not null } http => http.StatusCode is
            HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        HttpRequestException { InnerException: SocketException socket } => IsTransient(socket),
        HttpRequestException { InnerException: AuthenticationException } => false,
        HttpRequestException http => http.HttpRequestError is
            HttpRequestError.Unknown or HttpRequestError.NameResolutionError or
            HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded,
        _ => false
    };
}
