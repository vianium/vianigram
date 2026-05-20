// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Routes ICallsApi.SetSpeakerAsync to the IVoipMediaPort (no MTProto hop).

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
    /// Local-only speakerphone toggle. Confirms the session exists and is
    /// Active, then forwards the command to
    /// <see cref="IVoipMediaPort.SetSpeakerAsync"/>. Does NOT dispatch any
    /// MTProto request — audio routing is purely device-local.
    /// </summary>
    internal sealed class SetSpeakerHandler
    {
        private readonly ICallRepository _repo;
        private readonly IVoipMediaPort _voip;
        private readonly IComponentLogger _log;

        public SetSpeakerHandler(ICallRepository repo, IVoipMediaPort voip, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (voip == null) throw new ArgumentNullException("voip");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _voip = voip;
            _log = new TimestampedLogger(log, "Calls.SetSpeaker");
        }

        public async Task<Result<Unit, CallError>> HandleAsync(SetSpeakerCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, CallError>.Fail(CallError.Unknown("null command"));

            CallSession session = await _repo.FindAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, CallError>.Fail(CallError.CallNotFound(cmd.CallId.ToString()));
            if (session.State != CallSessionState.Active)
                return Result<Unit, CallError>.Fail(
                    CallError.NotInExpectedState("SetSpeaker requires Active; was " + session.State));

            var result = await _voip.SetSpeakerAsync(cmd.CallId, cmd.On, ct).ConfigureAwait(false);
            if (result.IsFail)
            {
                _log.Warn("voip.SetSpeakerAsync failed: " + result.Error);
            }
            return result;
        }
    }
}
