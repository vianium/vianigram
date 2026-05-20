// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Routes ICallsApi.SetMutedAsync to the IVoipMediaPort (no MTProto hop).

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Application.UseCases;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Application.Handlers
{
    /// <summary>
    /// Local-only mute toggle. Confirms the session exists and is Active
    /// (so the UI can't ask us to mute a discarded call), then forwards
    /// the command to <see cref="IVoipMediaPort.SetMutedAsync"/>. Does NOT
    /// dispatch any MTProto request — mute is a device-side concern that
    /// the peer infers from the absence of audio packets.
    ///
    /// Errors:
    ///   - <see cref="CallErrorKind.CallNotFound"/> when no session for the
    ///     supplied <see cref="CallId"/>.
    ///   - <see cref="CallErrorKind.NotInExpectedState"/> when the session
    ///     is not yet Active (Pending / Receiving / Discarded).
    ///   - The <see cref="IVoipMediaPort"/> result is bubbled up unchanged
    ///     on failure (typically <see cref="CallErrorKind.MediaPlaneFailed"/>
    ///     when the native plane is unavailable).
    /// </summary>
    internal sealed class SetMutedHandler
    {
        private readonly ICallRepository _repo;
        private readonly IVoipMediaPort _voip;
        private readonly IComponentLogger _log;

        public SetMutedHandler(ICallRepository repo, IVoipMediaPort voip, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (voip == null) throw new ArgumentNullException("voip");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _voip = voip;
            _log = new TimestampedLogger(log, "Calls.SetMuted");
        }

        public async Task<Result<Unit, CallError>> HandleAsync(SetMutedCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, CallError>.Fail(CallError.Unknown("null command"));

            CallSession session = await _repo.FindAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, CallError>.Fail(CallError.CallNotFound(cmd.CallId.ToString()));
            if (session.State != CallSessionState.Active)
                return Result<Unit, CallError>.Fail(
                    CallError.NotInExpectedState("SetMuted requires Active; was " + session.State));

            var result = await _voip.SetMutedAsync(cmd.CallId, cmd.Muted, ct).ConfigureAwait(false);
            if (result.IsFail)
            {
                _log.Warn("voip.SetMutedAsync failed: " + result.Error);
            }
            return result;
        }
    }
}
