// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Ports.Inbound
{
    /// <summary>
    /// EventArgs payload for the coalesced
    /// <see cref="IMediaApi.ProgressChanged"/> event. Carries enough info for
    /// a UI binding to show throughput, percent-complete, or a phase chip
    /// without having to read back the full transfer aggregate.
    /// </summary>
    public sealed class MediaProgressEventArgs : EventArgs
    {
        public MediaProgressEventArgs(MediaId id, MediaProgressEventKind kind, MediaProgress progress, string reason)
        {
            Id = id;
            Kind = kind;
            Progress = progress;
            Reason = reason ?? string.Empty;
        }

        public MediaId Id { get; private set; }
        public MediaProgressEventKind Kind { get; private set; }
        public MediaProgress Progress { get; private set; }
        public string Reason { get; private set; }
    }

    public enum MediaProgressEventKind
    {
        Started = 0,
        ChunkCompleted = 1,
        Progress = 2,
        Completed = 3,
        Failed = 4,
        Paused = 5,
        Resumed = 6,
        FloodWait = 7
    }
}
