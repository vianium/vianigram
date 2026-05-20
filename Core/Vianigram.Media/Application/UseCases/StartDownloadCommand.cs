// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Application.UseCases
{
    /// <summary>
    /// Begin a download for a server-resolved <see cref="FileLocation"/>.
    /// TotalSize is required up-front (Telegram returns it as part of the
    /// referencing message's TL document/photo so the caller already has it);
    /// without it we cannot plan chunks.
    /// </summary>
    public sealed class StartDownloadCommand
    {
        public StartDownloadCommand(FileLocation location, FileType type, long totalSize)
        {
            Location = location;
            Type = type;
            TotalSize = totalSize;
        }

        public FileLocation Location { get; private set; }
        public FileType Type { get; private set; }
        public long TotalSize { get; private set; }
    }
}
