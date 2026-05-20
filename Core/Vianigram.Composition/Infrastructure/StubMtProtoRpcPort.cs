// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// StubMtProtoRpcPort.cs
//
// Non-connected placeholder. Implements every per-context outbound
// IMtProtoRpcPort interface (Account, Chats, Messages) so the App can boot
// end-to-end with a fully wired composition graph even though no real
// MTProto channel is attached yet. Every CallAsync resolves to a typed
// "NotConnected" failure — handlers translate that into the appropriate
// per-context error so the UI surfaces "network unavailable" rather than
// crashing.
//
// The real adapter sits on top of Vianigram.Core.MTProto.MtProtoChannel.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Non-connected stand-in for the real MTProto adapter. The same instance
    /// is registered against every per-context IMtProtoRpcPort interface — it
    /// is not a member of any one bounded context.
    /// </summary>
    public sealed partial class StubMtProtoRpcPort
        : Vianigram.Account.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Search.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Media.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort
    {
        private const string NotConnectedMessage =
            "No MtProtoChannel wired yet; a later build will connect to the test DC.";

        private readonly IComponentLogger _log;

        public StubMtProtoRpcPort(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            _log = new TimestampedLogger(logger, "Composition.StubMtProtoRpcPort");
        }

        // ---------- Account port ----------

        Task<Result<byte[], Vianigram.Account.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Account.CallAsync stubbed (no DC).");
            var err = new Vianigram.Account.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Account.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Chats port (throw-style) ----------

        Task<byte[]> Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.CallAsync(
            byte[] payload, CancellationToken ct)
        {
            _log.Debug("Chats.CallAsync stubbed (no DC).");
            var tcs = new TaskCompletionSource<byte[]>();
            tcs.SetException(new InvalidOperationException(NotConnectedMessage));
            return tcs.Task;
        }

        // ---------- Messages port ----------

        Task<Result<byte[], Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] tlRequest, CancellationToken ct)
        {
            _log.Debug("Messages.CallAsync stubbed (no DC).");
            var err = Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage);
            return FromResult(Result<byte[], Vianigram.Messages.Domain.MessageError>.Fail(err));
        }

        // ---------- Sync port ----------

        Task<Result<byte[], Vianigram.Sync.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort.InvokeAsync(
                byte[] requestBody, string methodName, CancellationToken ct)
        {
            _log.Debug("Sync.InvokeAsync stubbed (no DC) method=" + (methodName ?? "?"));
            var err = new Vianigram.Sync.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Sync.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // WP8.1's TPL surface lacks Task.FromResult on every overload — local shim
        // keeps the call sites uniform without depending on platform-specific
        // helpers.
        private static Task<TResult> FromResult<TResult>(TResult value)
        {
            var tcs = new TaskCompletionSource<TResult>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
