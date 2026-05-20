// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Routes ICallsApi.FlipCameraAsync to the IVoipMediaPort (no MTProto hop).

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
    /// Local-only camera flip for active video calls. Confirms the
    /// session exists and is Active, then forwards the command to
    /// <see cref="IVoipMediaPort.FlipCameraAsync"/>. Does NOT dispatch any
    /// MTProto request — capture-device selection is purely device-local;
    /// the remote peer simply continues receiving the locally-encoded
    /// stream from whichever camera is currently feeding the encoder.
    /// </summary>
    internal sealed class FlipCameraHandler
    {
        private readonly ICallRepository _repo;
        private readonly IVoipMediaPort _voip;
        private readonly IComponentLogger _log;

        public FlipCameraHandler(ICallRepository repo, IVoipMediaPort voip, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (voip == null) throw new ArgumentNullException("voip");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _voip = voip;
            _log = new TimestampedLogger(log, "Calls.FlipCamera");
        }

        public async Task<Result<Unit, CallError>> HandleAsync(FlipCameraCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, CallError>.Fail(CallError.Unknown("null command"));

            CallSession session = await _repo.FindAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, CallError>.Fail(CallError.CallNotFound(cmd.CallId.ToString()));
            if (session.State != CallSessionState.Active)
                return Result<Unit, CallError>.Fail(
                    CallError.NotInExpectedState("FlipCamera requires Active; was " + session.State));

            var result = await _voip.FlipCameraAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (result.IsFail)
            {
                _log.Warn("voip.FlipCameraAsync failed: " + result.Error);
            }
            return result;
        }
    }
}
