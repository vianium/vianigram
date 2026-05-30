// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// DialogListSnapshotStore — SQLite-backed cache of the dialog list as
// last observed by the client. Lets ChatListPage paint a complete-looking
// list on cold boot before the messages.getDialogs RPC lands, then merge
// the fresh server response on top. Keeps the post-login UX cache-first:
// "open the app instantly with whatever we last saw and update in place
// when the network finishes."
//
// Storage: a single row in the shared `kv` table (scope = "dialog_snapshot",
// key = "state"). Uses the existing SqliteObjectStore<T> infrastructure so
// we share the same connection, the same serializer (DataContractJsonSerializer),
// and the same atomic upsert path the auth-key / dialogs / messages
// repositories use. SQLite reads on WP 8.1 ARM are ~20× faster than the
// equivalent ApplicationData.LocalFolder file open + ReadBufferAsync
// (~30 ms vs ~600 ms for a 50 KB payload).
//
// Threading: SqliteObjectStore serialises writes through the shared
// SqliteDatabase.Gate so concurrent Load / Save calls don't tear the
// blob. Best-effort everywhere — failures are logged and the network
// path remains the source of truth.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Kernel.Logging;
using Vianigram.Storage.Infrastructure.Sqlite;

namespace Vianigram.App.Services
{
    /// <summary>
    /// Persistent snapshot of the most recent dialog list the user saw.
    /// Hidrates ChatListPage on cold boot so the first paint shows real
    /// rows immediately — the live messages.getDialogs response then
    /// reconciles on top.
    /// </summary>
    public sealed class DialogListSnapshotStore
    {
        private const string Scope = "dialog_snapshot";

        // SemaphoreSlim acts as an async monitor — keeps Load/Save
        // serialised across our own call sites. SqliteObjectStore itself
        // takes the shared DB gate so SQL-level concurrency is already
        // handled; this slim gate just keeps successive saves from
        // re-serialising the same data in parallel.
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        // Lazy: store is constructed on first access so callers that
        // never reach the disk path (designer / tests) don't open the DB.
        private readonly object _storeGate = new object();
        private SqliteObjectStore<DialogSnapshotDto> _store;
        private bool _storeFailed;

        public async Task<DialogSnapshot> LoadAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SqliteObjectStore<DialogSnapshotDto> store = ResolveStore();
                if (store == null) return null;

                DialogSnapshotDto dto;
                try
                {
                    dto = await store.LoadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    EarlyLog.Write("App.DialogSnapshot",
                        "load failed: " + ex.GetType().Name + ": " + ex.Message);
                    return null;
                }

                if (dto == null || dto.Items == null || dto.Items.Count == 0) return null;

                var previews = new List<DialogPreview>(dto.Items.Count);
                for (int i = 0; i < dto.Items.Count; i++)
                {
                    DialogPreview preview = TryRebuildPreview(dto.Items[i]);
                    if (preview != null) previews.Add(preview);
                }

                DialogCursor cursor = DialogCursor.Empty;
                if (dto.Cursor != null && (dto.Cursor.OffsetId != 0 || dto.Cursor.OffsetPeerId != 0))
                {
                    PeerId offsetPeer = null;
                    if (dto.Cursor.OffsetPeerId > 0)
                    {
                        offsetPeer = TryBuildPeer(dto.Cursor.OffsetPeerKind, dto.Cursor.OffsetPeerId, dto.Cursor.OffsetPeerAccessHash);
                    }
                    DateTime offsetDate = dto.Cursor.OffsetDateBinary != 0
                        ? DateTime.FromBinary(dto.Cursor.OffsetDateBinary)
                        : default(DateTime);
                    cursor = new DialogCursor(offsetDate, dto.Cursor.OffsetId, offsetPeer);
                }

                return new DialogSnapshot(previews, cursor, dto.HasMore, dto.SavedAtBinary);
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task SaveAsync(
            IList<DialogPreview> items,
            DialogCursor cursor,
            bool hasMore,
            CancellationToken ct)
        {
            if (items == null) return CompletedTask;
            return SaveCoreAsync(items, cursor, hasMore, ct);
        }

        private async Task SaveCoreAsync(
            IList<DialogPreview> items,
            DialogCursor cursor,
            bool hasMore,
            CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SqliteObjectStore<DialogSnapshotDto> store = ResolveStore();
                if (store == null) return;

                var dto = new DialogSnapshotDto
                {
                    SavedAtBinary = DateTime.UtcNow.ToBinary(),
                    HasMore = hasMore,
                    Cursor = ToCursorDto(cursor),
                    Items = new List<DialogPreviewDto>(items.Count)
                };
                for (int i = 0; i < items.Count; i++)
                {
                    DialogPreview item = items[i];
                    if (item == null || item.Peer == null) continue;
                    dto.Items.Add(ToPreviewDto(item));
                }

                try
                {
                    await store.SaveAsync(dto, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    EarlyLog.Write("App.DialogSnapshot",
                        "save failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task ClearAsync(CancellationToken ct)
        {
            return ClearCoreAsync(ct);
        }

        private async Task ClearCoreAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                SqliteObjectStore<DialogSnapshotDto> store = ResolveStore();
                if (store == null) return;
                try
                {
                    await store.DeleteAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EarlyLog.Write("App.DialogSnapshot",
                        "clear failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        // ---- Lazy store resolution ----------------------------------

        /// <summary>
        /// Lazily acquire the shared SqliteDatabase and wrap a
        /// per-scope SqliteObjectStore. Returns null when storage isn't
        /// available (designer / tests) — the caller treats that as a
        /// snapshot miss and the live RPC path runs unchanged.
        /// </summary>
        private SqliteObjectStore<DialogSnapshotDto> ResolveStore()
        {
            if (_store != null) return _store;
            if (_storeFailed) return null;
            lock (_storeGate)
            {
                if (_store != null) return _store;
                if (_storeFailed) return null;
                try
                {
                    SqliteDatabase db = SqliteDatabase.Acquire();
                    // No encryption: the snapshot only contains
                    // titles + last-message previews + counts, none of
                    // which justify the extra DPAPI round trip on cold
                    // boot. The same data sits in the network layer's
                    // unencrypted memory cache anyway.
                    _store = new SqliteObjectStore<DialogSnapshotDto>(db, Scope, encrypted: false, protector: null);
                    return _store;
                }
                catch (Exception ex)
                {
                    EarlyLog.Write("App.DialogSnapshot",
                        "store init failed: " + ex.GetType().Name + ": " + ex.Message);
                    _storeFailed = true;
                    return null;
                }
            }
        }

        // ---- DTO projection helpers ----------------------------------

        private static DialogPreviewDto ToPreviewDto(DialogPreview src)
        {
            return new DialogPreviewDto
            {
                PeerKind = (int)src.Peer.Kind,
                PeerId = src.Peer.Id,
                PeerAccessHash = src.Peer.AccessHash,
                Title = src.Title,
                LastMessageText = src.LastMessageText,
                LastMessageDateBinary = src.LastMessageDate == default(DateTime) ? 0L : src.LastMessageDate.ToBinary(),
                UnreadCount = src.UnreadCount,
                IsPinned = src.IsPinned,
                IsMuted = src.IsMuted,
                MutedUntilBinary = src.MutedUntil.HasValue ? src.MutedUntil.Value.ToBinary() : 0L,
                LastMessageId = src.LastMessageId.HasValue ? src.LastMessageId.Value : 0L,
                LastMessageIdHasValue = src.LastMessageId.HasValue
            };
        }

        private static DialogPreview TryRebuildPreview(DialogPreviewDto dto)
        {
            if (dto == null || dto.PeerId <= 0) return null;
            PeerId peer = TryBuildPeer(dto.PeerKind, dto.PeerId, dto.PeerAccessHash);
            if (peer == null) return null;

            DateTime lastMessageDate = dto.LastMessageDateBinary != 0
                ? DateTime.FromBinary(dto.LastMessageDateBinary)
                : default(DateTime);
            DateTime? mutedUntil = null;
            if (dto.MutedUntilBinary != 0) mutedUntil = DateTime.FromBinary(dto.MutedUntilBinary);
            long? lastMsgId = dto.LastMessageIdHasValue ? (long?)dto.LastMessageId : null;

            try
            {
                return new DialogPreview(
                    peer,
                    dto.Title ?? string.Empty,
                    dto.LastMessageText ?? string.Empty,
                    lastMessageDate,
                    dto.UnreadCount < 0 ? 0 : dto.UnreadCount,
                    dto.IsPinned,
                    dto.IsMuted,
                    mutedUntil,
                    lastMsgId);
            }
            catch
            {
                return null;
            }
        }

        private static PeerId TryBuildPeer(int kindRaw, long id, long accessHash)
        {
            if (id <= 0) return null;
            try
            {
                switch ((PeerKind)kindRaw)
                {
                    case PeerKind.User: return PeerId.User(id, accessHash);
                    case PeerKind.Chat: return PeerId.Chat(id);
                    case PeerKind.Channel: return PeerId.Channel(id, accessHash);
                    default: return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static DialogCursorDto ToCursorDto(DialogCursor cursor)
        {
            if (cursor == null || cursor.IsEmpty) return null;
            var dto = new DialogCursorDto
            {
                OffsetId = cursor.OffsetId,
                OffsetDateBinary = cursor.OffsetDate == default(DateTime) ? 0L : cursor.OffsetDate.ToBinary()
            };
            if (cursor.OffsetPeer != null)
            {
                dto.OffsetPeerKind = (int)cursor.OffsetPeer.Kind;
                dto.OffsetPeerId = cursor.OffsetPeer.Id;
                dto.OffsetPeerAccessHash = cursor.OffsetPeer.AccessHash;
            }
            return dto;
        }

        private static readonly Task _completed = TaskHelpers.CompletedTaskInstance();
        private static Task CompletedTask { get { return _completed; } }

        private static class TaskHelpers
        {
            public static Task CompletedTaskInstance()
            {
                var tcs = new TaskCompletionSource<bool>();
                tcs.SetResult(true);
                return tcs.Task;
            }
        }
    }

    /// <summary>
    /// In-memory snapshot returned by <see cref="DialogListSnapshotStore.LoadAsync"/>.
    /// Decoupled from the DTO shape so callers depend on domain types only.
    /// </summary>
    public sealed class DialogSnapshot
    {
        public IList<DialogPreview> Items { get; private set; }
        public DialogCursor Cursor { get; private set; }
        public bool HasMore { get; private set; }
        public DateTime SavedAtUtc { get; private set; }

        public DialogSnapshot(IList<DialogPreview> items, DialogCursor cursor, bool hasMore, long savedAtBinary)
        {
            Items = items ?? new List<DialogPreview>();
            Cursor = cursor ?? DialogCursor.Empty;
            HasMore = hasMore;
            SavedAtUtc = savedAtBinary != 0 ? DateTime.FromBinary(savedAtBinary) : default(DateTime);
        }
    }

    // ---- Persisted DTOs ----------------------------------------------
    // DateTimes are stored as Int64 (.ToBinary) so the JSON survives
    // ISO-8601 formatting quirks across CLR versions. Nullable shapes are
    // expressed via *Binary == 0 / *HasValue flags to keep the DTOs flat
    // and DataContractJsonSerializer-friendly (no struct-nullable juggling).
    //
    // These types are public so the shared SqliteObjectStore<T> in
    // Vianigram.Storage can construct + serialise them; they live here
    // because that storage layer doesn't know about Chats.Domain types.

    [System.Runtime.Serialization.DataContract]
    public sealed class DialogSnapshotDto
    {
        [System.Runtime.Serialization.DataMember(Name = "saved_at")]
        public long SavedAtBinary { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "has_more")]
        public bool HasMore { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "cursor")]
        public DialogCursorDto Cursor { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "items")]
        public List<DialogPreviewDto> Items { get; set; }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class DialogCursorDto
    {
        [System.Runtime.Serialization.DataMember(Name = "offset_id")]
        public long OffsetId { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "offset_date")]
        public long OffsetDateBinary { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "offset_peer_kind")]
        public int OffsetPeerKind { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "offset_peer_id")]
        public long OffsetPeerId { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "offset_peer_access_hash")]
        public long OffsetPeerAccessHash { get; set; }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class DialogPreviewDto
    {
        [System.Runtime.Serialization.DataMember(Name = "peer_kind")]
        public int PeerKind { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "peer_id")]
        public long PeerId { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "peer_access_hash")]
        public long PeerAccessHash { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "title")]
        public string Title { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "last_message_text")]
        public string LastMessageText { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "last_message_date")]
        public long LastMessageDateBinary { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "unread")]
        public int UnreadCount { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "pinned")]
        public bool IsPinned { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "muted")]
        public bool IsMuted { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "muted_until")]
        public long MutedUntilBinary { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "last_message_id")]
        public long LastMessageId { get; set; }

        [System.Runtime.Serialization.DataMember(Name = "last_message_id_has_value")]
        public bool LastMessageIdHasValue { get; set; }
    }
}
