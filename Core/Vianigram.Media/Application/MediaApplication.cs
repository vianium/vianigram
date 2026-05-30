// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Result;
using Vianigram.Media.Application.Handlers;
using Vianigram.Media.Application.UseCases;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.Entities;
using Vianigram.Media.Domain.Events;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Infrastructure;
using Vianigram.Media.Ports.Inbound;

namespace Vianigram.Media.Application
{
    /// <summary>
    /// Implements <see cref="IMediaApi"/> by delegating to the per-command
    /// handlers and bridging typed domain events from the bus to the
    /// coalesced <see cref="IMediaApi.ProgressChanged"/> event.
    /// </summary>
    public sealed class MediaApplication : IMediaApi, IDisposable
    {
        private readonly StartDownloadHandler _download;
        private readonly StartUploadHandler _upload;
        private readonly PauseTransferHandler _pause;
        private readonly ResumeTransferHandler _resume;
        private readonly CancelTransferHandler _cancel;
        private readonly TransferRegistry _registry;
        private readonly IDisposable[] _subs;

        public event EventHandler<MediaProgressEventArgs> ProgressChanged;

        public MediaApplication(
            StartDownloadHandler download,
            StartUploadHandler upload,
            PauseTransferHandler pause,
            ResumeTransferHandler resume,
            CancelTransferHandler cancel,
            TransferRegistry registry,
            IEventBus bus)
        {
            if (download == null) throw new ArgumentNullException("download");
            if (upload == null) throw new ArgumentNullException("upload");
            if (pause == null) throw new ArgumentNullException("pause");
            if (resume == null) throw new ArgumentNullException("resume");
            if (cancel == null) throw new ArgumentNullException("cancel");
            if (registry == null) throw new ArgumentNullException("registry");
            if (bus == null) throw new ArgumentNullException("bus");

            _download = download;
            _upload = upload;
            _pause = pause;
            _resume = resume;
            _cancel = cancel;
            _registry = registry;

            _subs = new IDisposable[]
            {
                bus.Subscribe<TransferStarted>(OnStarted),
                bus.Subscribe<ChunkCompleted>(OnChunkCompleted),
                bus.Subscribe<TransferProgress>(OnProgress),
                bus.Subscribe<TransferCompleted>(OnCompleted),
                bus.Subscribe<TransferFailed>(OnFailed),
                bus.Subscribe<TransferPaused>(OnPaused),
                bus.Subscribe<TransferResumed>(OnResumed),
                bus.Subscribe<TransferFloodWait>(OnFloodWait)
            };
        }

        public Task<Result<MediaTransfer, MediaError>> DownloadAsync(FileLocation location, FileType type, long totalSize, CancellationToken ct)
        {
            return _download.HandleAsync(new StartDownloadCommand(location, type, totalSize), ct);
        }

        public Task<Result<byte[], MediaError>> DownloadRangeAsync(FileLocation location, long offset, int limit, CancellationToken ct)
        {
            return _download.DownloadRangeAsync(location, offset, limit, ct);
        }

        public Task<Result<UploadedFile, MediaError>> UploadAsync(byte[] bytes, string fileName, CancellationToken ct)
        {
            return _upload.HandleAsync(new StartUploadCommand(bytes, fileName), ct);
        }

        public Task<Result<Domain.ValueObjects.Unit, MediaError>> PauseAsync(MediaId id, CancellationToken ct)
        {
            return _pause.HandleAsync(new PauseTransferCommand(id), ct);
        }

        public Task<Result<Domain.ValueObjects.Unit, MediaError>> ResumeAsync(MediaId id, CancellationToken ct)
        {
            return _resume.HandleAsync(new ResumeTransferCommand(id), ct);
        }

        public Task<Result<Domain.ValueObjects.Unit, MediaError>> CancelAsync(MediaId id, CancellationToken ct)
        {
            return _cancel.HandleAsync(new CancelTransferCommand(id), ct);
        }

        public MediaTransfer GetTransfer(MediaId id)
        {
            return _registry.Find(id);
        }

        public void Dispose()
        {
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }

        // ---------- Bus -> EventHandler bridge ----------

        private void OnStarted(TransferStarted e)
        {
            Raise(e.Id, MediaProgressEventKind.Started, new MediaProgress(0, e.TotalSize, 0), null);
        }

        private void OnChunkCompleted(ChunkCompleted e)
        {
            var t = _registry.Find(e.Id);
            long total = t == null ? 0 : t.TotalSize;
            long done = t == null ? 0 : t.BytesCompleted;
            Raise(e.Id, MediaProgressEventKind.ChunkCompleted, new MediaProgress(done, total, 0), null);
        }

        private void OnProgress(TransferProgress e)
        {
            Raise(e.Id, MediaProgressEventKind.Progress, e.Progress, null);
        }

        private void OnCompleted(TransferCompleted e)
        {
            Raise(e.Id, MediaProgressEventKind.Completed, new MediaProgress(e.TotalBytes, e.TotalBytes, 0), e.LocalPath);
        }

        private void OnFailed(TransferFailed e)
        {
            Raise(e.Id, MediaProgressEventKind.Failed, default(MediaProgress), e.Reason);
        }

        private void OnPaused(TransferPaused e)
        {
            Raise(e.Id, MediaProgressEventKind.Paused, default(MediaProgress), e.Reason);
        }

        private void OnResumed(TransferResumed e)
        {
            Raise(e.Id, MediaProgressEventKind.Resumed, default(MediaProgress), null);
        }

        private void OnFloodWait(TransferFloodWait e)
        {
            Raise(e.Id, MediaProgressEventKind.FloodWait, default(MediaProgress), "FLOOD_WAIT_" + e.Seconds);
        }

        private void Raise(MediaId id, MediaProgressEventKind kind, MediaProgress progress, string reason)
        {
            var h = ProgressChanged;
            if (h != null) h(this, new MediaProgressEventArgs(id, kind, progress, reason));
        }
    }
}
