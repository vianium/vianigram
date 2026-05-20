// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Messages.Application;
using Vianigram.Messages.Application.Handlers;
using Vianigram.Messages.Infrastructure;
using Vianigram.Messages.Ports.Inbound;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Composition
{
    /// <summary>
    /// Wires up the Messages bounded context. Returns a constructed
    /// <see cref="IMessagesApi"/> ready to be registered with the global
    /// <c>VianigramCompositionRoot</c>.
    ///
    /// The MTProto adapter is injected by the caller — the Messages context
    /// has no ambient knowledge of which DC pool / channel implementation is
    /// in use.
    /// </summary>
    public static class MessagesCompositionRoot
    {
        public static IMessagesApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            IClock clock,
            ILogger log,
            ITelemetry telemetry,
            IMessageRepository repository = null,
            IMessageIdGenerator idGenerator = null,
            IPeerAccessHashPort peerHashes = null)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            var repo = repository ?? new InMemoryMessageRepository();
            var idGen = idGenerator ?? new MonotonicMessageIdGenerator();

            var sendHandler = new SendTextMessageHandler(repo, idGen, rpc, bus, clock, log, telemetry);
            var editHandler = new EditTextMessageHandler(repo, rpc, bus, clock, log, telemetry);
            var deleteHandler = new DeleteMessageHandler(repo, rpc, bus, clock, log, telemetry);
            var readHandler = new MarkAsReadHandler(rpc, bus, clock, log, telemetry);
            var historyHandler = new LoadHistoryHandler(repo, rpc, clock, log, telemetry, peerHashes);
            var forwardHandler = new ForwardMessagesHandler(rpc, bus, clock, log, telemetry);
            var sendPollHandler = new SendPollHandler(rpc, bus, clock, log, telemetry);
            var scheduleTextHandler = new ScheduleTextHandler(rpc, bus, clock, log, telemetry);
            var getScheduledHandler = new GetScheduledHandler(rpc, clock, log, telemetry);
            var sendScheduledNowHandler = new SendScheduledNowHandler(rpc, bus, clock, log, telemetry);
            var deleteScheduledHandler = new DeleteScheduledHandler(rpc, bus, clock, log, telemetry);

            return new MessagesApplication(
                sendHandler,
                editHandler,
                deleteHandler,
                readHandler,
                historyHandler,
                forwardHandler,
                sendPollHandler,
                scheduleTextHandler,
                getScheduledHandler,
                sendScheduledNowHandler,
                deleteScheduledHandler,
                repo,
                bus);
        }
    }
}
