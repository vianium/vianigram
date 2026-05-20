// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Media.Application.UseCases;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.Events;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Infrastructure;

namespace Vianigram.Media.Application.Handlers
{
    /// <summary>
    /// Resume a paused transfer. The chunk fan-out tasks have been parked
    /// in their pause-poll loop; flipping the state to InProgress lets them
    /// proceed at the next iteration (≤ 250 ms).
    /// </summary>
    public sealed class ResumeTransferHandler
    {
        private readonly TransferRegistry _registry;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public ResumeTransferHandler(TransferRegistry registry, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (registry == null) throw new ArgumentNullException("registry");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _registry = registry;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Media.ResumeTransfer");
            _telemetry = telemetry;
        }

        public Task<Result<Domain.ValueObjects.Unit, MediaError>> HandleAsync(ResumeTransferCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Wrap(Result<Domain.ValueObjects.Unit, MediaError>.Fail(MediaError.InvalidArgument("cmd null")));

            var transfer = _registry.Find(cmd.Id);
            if (transfer == null)
                return Wrap(Result<Domain.ValueObjects.Unit, MediaError>.Fail(MediaError.InvalidState("transfer not found")));

            transfer.Resume();
            _bus.Publish(new TransferResumed(cmd.Id, _clock.UtcNow));
            _telemetry.Track("media.transfer.resumed", 1);
            return Wrap(Result<Domain.ValueObjects.Unit, MediaError>.Ok(Domain.ValueObjects.Unit.Value));
        }

        private static Task<Result<Domain.ValueObjects.Unit, MediaError>> Wrap(Result<Domain.ValueObjects.Unit, MediaError> r)
        {
            var tcs = new TaskCompletionSource<Result<Domain.ValueObjects.Unit, MediaError>>();
            tcs.SetResult(r);
            return tcs.Task;
        }
    }
}
