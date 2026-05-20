// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Search.Application.UseCases;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Application.Handlers
{
    /// <summary>
    /// Handles <see cref="CancelSearchCommand"/>: marks the session as
    /// <c>Cancelled</c> and drains events. Idempotent — cancelling a finished
    /// session returns <c>Ok(Unit)</c> without staging additional events.
    /// </summary>
    internal sealed class CancelSearchHandler
    {
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public CancelSearchHandler(IEventBus bus, ILogger log, IClock clock)
        {
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _bus = bus;
            _log = new TimestampedLogger(log, "Search.CancelSearch");
            _clock = clock;
        }

        public Task<Result<Unit, SearchError>> HandleAsync(CancelSearchCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Task.FromResult(Result<Unit, SearchError>.Fail(SearchError.Unknown("null command")));

            ct.ThrowIfCancellationRequested();

            DateTime now = _clock.UtcNow;
            cmd.Session.Cancel(now, cmd.Reason);
            HandlerEventBridge.Drain(cmd.Session, _bus);
            _log.Info("session cancelled: id=" + cmd.Session.SessionId + " reason=" + cmd.Reason);
            return Task.FromResult(Result<Unit, SearchError>.Ok(Unit.Value));
        }
    }
}
