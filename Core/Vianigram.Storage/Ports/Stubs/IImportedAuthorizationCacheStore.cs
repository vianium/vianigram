// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// Persistent cache of cross-DC authorization blobs minted by
    /// <c>auth.exportAuthorization</c>. Telegram media (avatars, file
    /// downloads) lives on per-region DCs (DC#2/EU, DC#3/AMS, etc.).
    /// On cold login each media DC needs its own authenticated session,
    /// which today costs two RPCs: <c>auth.exportAuthorization</c> on
    /// the home DC (~200-300 ms) plus <c>auth.importAuthorization</c>
    /// on the target media DC (~150-200 ms). Two media DCs typically
    /// come up at first paint, so the cold path burns ~1.0-1.5 s of
    /// pure round-trip latency before the first thumbnail can decode.
    ///
    /// The exported authorization blob is valid until the user revokes
    /// the session server-side, so persisting it lets re-login skip
    /// the export step entirely — only the cheap import RPC on the
    /// target DC remains. On cache hit the saved blob short-circuits
    /// <see cref="ImportedAuthorizationCacheRecord.AuthBlob"/> straight
    /// into <c>auth.importAuthorization</c>.
    ///
    /// Implementations must:
    ///   - Be safe to call concurrently; the SQLite adapter serialises
    ///     on the shared <c>SqliteDatabase.Gate</c>.
    ///   - Be best-effort: TryLoad returns null on any failure; Save
    ///     and Evict swallow internal errors so a stale cache never
    ///     breaks the live RPC path.
    ///   - Key on <c>(user_id, target_dc_id)</c> so multi-account
    ///     installs don't collide. Records also carry the
    ///     <c>home_dc_id</c> they were minted from — callers MUST
    ///     re-mint (export+import) if the user's current home DC
    ///     changed (e.g. after a phone-number migrate), because the
    ///     blob is bound to the issuing DC.
    /// </summary>
    public interface IImportedAuthorizationCacheStore
    {
        Task<ImportedAuthorizationCacheRecord> TryLoadAsync(
            long userId, int targetDcId, CancellationToken ct);

        Task SaveAsync(
            long userId, int targetDcId, int homeDcId, byte[] authBlob, CancellationToken ct);

        Task EvictAllForUserAsync(long userId, CancellationToken ct);

        Task EvictForTargetAsync(long userId, int targetDcId, CancellationToken ct);
    }

    public sealed class ImportedAuthorizationCacheRecord
    {
        public long UserId { get; set; }
        public int TargetDcId { get; set; }
        public int HomeDcId { get; set; }
        public byte[] AuthBlob { get; set; }
        public DateTimeOffset CachedAt { get; set; }
    }
}
