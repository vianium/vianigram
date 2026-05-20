// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Reloads the dialog catalog from the start by delegating to
    /// <see cref="LoadDialogListHandler"/> with an empty cursor and a default limit.
    ///
    /// The default limit (100) matches Telegram's per-call cap for getDialogs;
    /// callers wanting a different value should call Load directly.
    /// </summary>
    public sealed class RefreshDialogListHandler
    {
        public const int DefaultLimit = 100;

        private readonly LoadDialogListHandler _load;

        public RefreshDialogListHandler(LoadDialogListHandler load)
        {
            if (load == null) throw new ArgumentNullException("load");
            _load = load;
        }

        public async Task<Result<Unit, ChatError>> HandleAsync(RefreshDialogListCommand cmd, CancellationToken ct)
        {
            // cmd is the singleton; null is tolerated.
            var inner = await _load.HandleAsync(new LoadDialogListCommand(DefaultLimit, DialogCursor.Empty), ct)
                                   .ConfigureAwait(false);
            if (inner.IsFail) return Result<Unit, ChatError>.Fail(inner.Error);
            return Result<Unit, ChatError>.Ok(Unit.Value);
        }
    }
}
