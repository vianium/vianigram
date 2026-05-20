// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Media.Application;
using Vianigram.Media.Application.Handlers;
using Vianigram.Media.Infrastructure;
using Vianigram.Media.Ports.Inbound;
using Vianigram.Media.Ports.Outbound;

namespace Vianigram.Media.Composition
{
    /// <summary>
    /// Wires up the Media bounded context. Returns a constructed
    /// <see cref="IMediaApi"/> ready to be registered with the global
    /// <c>VianigramCompositionRoot</c>.
    ///
    /// <para>Caller injects the <see cref="IMtProtoRpcPort"/> (so this
    /// context never knows which DC pool it's hitting) and optionally an
    /// <see cref="IMediaCache"/> (if omitted, the in-memory cache is used; a
    /// SQLite cache can be swapped in).</para>
    ///
    /// <para>Native codec decode (Opus / WebP / JPEG thumbs) is intentionally
    /// not wired here: it ships as <c>Vianigram.Core.Media</c> WinMDs. The
    /// orchestrator never needs to decode for upload/download to function —
    /// the cache returns raw bytes and the UI decoder is the consumer.</para>
    /// </summary>
    public static class MediaCompositionRoot
    {
        public static IMediaApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            IClock clock,
            ILogger log,
            ITelemetry telemetry,
            IMediaCache cache = null)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            var resolvedCache = cache ?? new InMemoryMediaCache();
            var registry = new TransferRegistry();

            var downloadHandler = new StartDownloadHandler(rpc, resolvedCache, registry, bus, clock, log, telemetry);
            var uploadHandler = new StartUploadHandler(rpc, registry, bus, clock, log, telemetry);
            var pauseHandler = new PauseTransferHandler(registry, bus, clock, log, telemetry);
            var resumeHandler = new ResumeTransferHandler(registry, bus, clock, log, telemetry);
            var cancelHandler = new CancelTransferHandler(registry, bus, clock, log, telemetry);

            return new MediaApplication(
                downloadHandler,
                uploadHandler,
                pauseHandler,
                resumeHandler,
                cancelHandler,
                registry,
                bus);
        }
    }
}
