// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Media.Application.UseCases
{
    /// <summary>
    /// Begin an upload from an in-memory byte buffer. A future stream-based
    /// overload will read from <c>IRandomAccessStream</c> for large videos to
    /// avoid loading the whole file into managed heap; for now we accept a
    /// <c>byte[]</c> because Messages already hands us text-adjacent
    /// attachments that fit in memory.
    /// </summary>
    public sealed class StartUploadCommand
    {
        public StartUploadCommand(byte[] bytes, string fileName)
        {
            Bytes = bytes;
            FileName = fileName;
        }

        public byte[] Bytes { get; private set; }
        public string FileName { get; private set; }
    }
}
