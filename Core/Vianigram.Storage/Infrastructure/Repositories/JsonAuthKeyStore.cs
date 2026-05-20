// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Storage.Application;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>
    /// File-backed implementation of <see cref="IAuthKeyStore"/>.
    /// <para>
    /// Encryption: <b>always-on</b> via <see cref="IDataProtector"/> with scope
    /// <c>LOCAL=user</c> — auth keys are <i>Critical</i> per
    /// <c>docs/security/at-rest-encryption.md</c> §2 and never live in
    /// plaintext on disk.
    /// </para>
    /// <para>
    /// Storage path: <c>LocalFolder/auth_keys.bin</c>. Atomic writes via
    /// <see cref="JsonObjectStore{T}"/>. Records keyed by DC id.
    /// </para>
    /// <para>
    /// Caveat: this adapter persists raw <c>byte[]</c> auth_key material
    /// as a <see cref="DataMember"/>. A future revision should move auth
    /// keys into the native <c>SecretKeyHandle</c> arena (policy §3) and
    /// surface them through this store as opaque handle ids rather than
    /// managed byte arrays. The DPAPI envelope on disk does not change.
    /// </para>
    /// </summary>
    public sealed class JsonAuthKeyStore : IAuthKeyStore
    {
        private const string FileName = "auth_keys.bin";

        private readonly IObjectStore<AuthKeyStoreState> _store;
        private readonly object _gate = new object();

        /// <summary>
        /// Compat constructor: builds the legacy JSON-on-disk backend.
        /// Composition uses <see cref="JsonAuthKeyStore(IObjectStore{AuthKeyStoreState})"/>
        /// so the SQLite-backed implementation can be injected.
        /// </summary>
        public JsonAuthKeyStore(IDataProtector protector)
        {
            if (protector == null) throw new ArgumentNullException("protector");
            _store = new JsonObjectStore<AuthKeyStoreState>(FileName, true /* encrypted */, protector);
        }

        /// <summary>
        /// DI constructor: receives an already-configured object store
        /// (typically <see cref="Vianigram.Storage.Infrastructure.Sqlite.SqliteObjectStore{T}"/>).
        /// </summary>
        public JsonAuthKeyStore(IObjectStore<AuthKeyStoreState> store)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
        }

        public async Task<AuthKeyRecord> GetAsync(int dcId, CancellationToken ct)
        {
            AuthKeyStoreState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            return FindRecord(state, dcId);
        }

        public async Task PutAsync(AuthKeyRecord record, CancellationToken ct)
        {
            if (record == null) throw new ArgumentNullException("record");

            AuthKeyStoreState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            UpsertRecord(state, record);
            await _store.SaveAsync(state, ct).ConfigureAwait(false);
        }

        public async Task DeleteAsync(int dcId, CancellationToken ct)
        {
            AuthKeyStoreState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (RemoveRecord(state, dcId))
            {
                await _store.SaveAsync(state, ct).ConfigureAwait(false);
            }
        }

        public Task ClearAsync(CancellationToken ct)
        {
            return _store.DeleteAsync(ct);
        }

        private AuthKeyRecord FindRecord(AuthKeyStoreState state, int dcId)
        {
            lock (_gate)
            {
                if (state == null || state.Records == null) return null;
                foreach (AuthKeyRecord rec in state.Records)
                {
                    if (rec != null && rec.DcId == dcId) return CloneRecord(rec);
                }
                return null;
            }
        }

        private void UpsertRecord(AuthKeyStoreState state, AuthKeyRecord record)
        {
            lock (_gate)
            {
                if (state.Records == null) state.Records = new List<AuthKeyRecord>();
                for (int i = 0; i < state.Records.Count; i++)
                {
                    if (state.Records[i] != null && state.Records[i].DcId == record.DcId)
                    {
                        state.Records[i] = CloneRecord(record);
                        return;
                    }
                }
                state.Records.Add(CloneRecord(record));
            }
        }

        private bool RemoveRecord(AuthKeyStoreState state, int dcId)
        {
            lock (_gate)
            {
                if (state == null || state.Records == null) return false;
                for (int i = 0; i < state.Records.Count; i++)
                {
                    if (state.Records[i] != null && state.Records[i].DcId == dcId)
                    {
                        state.Records.RemoveAt(i);
                        return true;
                    }
                }
                return false;
            }
        }

        private static AuthKeyRecord CloneRecord(AuthKeyRecord record)
        {
            if (record == null) return null;
            return new AuthKeyRecord
            {
                DcId = record.DcId,
                AuthKeyId = record.AuthKeyId,
                AuthKey = CloneBytes(record.AuthKey),
                ServerSalt = CloneBytes(record.ServerSalt),
                CreatedAtUnix = record.CreatedAtUnix,
                ServerTimeOffset = record.ServerTimeOffset
            };
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null) return null;
            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
