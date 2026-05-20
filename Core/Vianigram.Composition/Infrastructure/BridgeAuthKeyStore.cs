// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BridgeAuthKeyStore.cs
//
// Anti-corruption bridge.
//
// Vianigram.Account defines its outbound IAuthKeyStore with the shape
//     LoadAsync(dcId, ct) / SaveAsync(dcId, record, ct) / DeleteAsync(dcId, ct)
// using a simple plaintext-in-memory AuthKeyRecord (AuthKey/AuthKeyId/
// ServerSalt/ServerTimeOffset).
//
// Vianigram.Storage publishes a stub-shaped equivalent under
// Vianigram.Storage.Ports.Stubs.IAuthKeyStore with
//     GetAsync(dcId, ct) / PutAsync(record, ct) / DeleteAsync(dcId, ct) /
//     ClearAsync(ct)
// where the stub AuthKeyRecord is a DataContract-decorated DTO carrying
// DcId, AuthKeyId (long), AuthKey (byte[]), ServerSalt (byte[8]) and
// CreatedAtUnix.
//
// This bridge maps between the two without leaking either side's types.
// The encrypted JsonAuthKeyStore underneath does the at-rest DPAPI envelope.
//
// Salt encoding: the storage DTO carries an 8-byte big-endian byte array
// (CryptoSelfTest convention); the Account port carries a long. We treat
// the long as the raw 8-byte salt in big-endian order on save/load.
// The native MtProtoChannel takes int64 salt as well, so the Account
// shape and the channel shape match — only the on-disk DTO needs the
// byte[] form.
//
// The bridge does NOT cache; the underlying store is already a single
// process-local instance and its loads are O(1) once the JSON envelope is
// in memory.

using System;
using System.Threading;
using System.Threading.Tasks;
using AccountIAuthKeyStore = Vianigram.Account.Ports.Outbound.IAuthKeyStore;
using AccountAuthKeyRecord = Vianigram.Account.Ports.Outbound.AuthKeyRecord;
using StorageIAuthKeyStore = Vianigram.Storage.Ports.Stubs.IAuthKeyStore;
using StorageAuthKeyRecord = Vianigram.Storage.Ports.Stubs.AuthKeyRecord;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class BridgeAuthKeyStore : AccountIAuthKeyStore
    {
        private readonly StorageIAuthKeyStore _inner;

        public BridgeAuthKeyStore(StorageIAuthKeyStore inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
        }

        public async Task<AccountAuthKeyRecord> LoadAsync(int dcId, CancellationToken ct)
        {
            StorageAuthKeyRecord rec = await _inner.GetAsync(dcId, ct).ConfigureAwait(false);
            if (rec == null) return null;

            return new AccountAuthKeyRecord
            {
                AuthKey = CloneBytes(rec.AuthKey),
                AuthKeyId = unchecked((ulong)rec.AuthKeyId),
                ServerSalt = SaltBytesToInt64(rec.ServerSalt),
                ServerTimeOffset = rec.ServerTimeOffset
            };
        }

        public Task SaveAsync(int dcId, AccountAuthKeyRecord record, CancellationToken ct)
        {
            if (record == null) throw new ArgumentNullException("record");

            var dto = new StorageAuthKeyRecord
            {
                DcId = dcId,
                AuthKey = CloneBytes(record.AuthKey),
                AuthKeyId = unchecked((long)record.AuthKeyId),
                ServerSalt = SaltInt64ToBytes(record.ServerSalt),
                CreatedAtUnix = NowUnixSeconds(),
                ServerTimeOffset = record.ServerTimeOffset
            };
            return _inner.PutAsync(dto, ct);
        }

        public Task DeleteAsync(int dcId, CancellationToken ct)
        {
            return _inner.DeleteAsync(dcId, ct);
        }

        // ---------- Helpers ----------

        private static long SaltBytesToInt64(byte[] saltBytes)
        {
            if (saltBytes == null || saltBytes.Length == 0) return 0L;
            // Big-endian 8 bytes -> int64. Tolerant if shorter — pads with zeros.
            long acc = 0L;
            int take = saltBytes.Length < 8 ? saltBytes.Length : 8;
            for (int i = 0; i < take; i++)
            {
                acc = (acc << 8) | (long)(saltBytes[i] & 0xff);
            }
            return acc;
        }

        private static byte[] SaltInt64ToBytes(long salt)
        {
            byte[] buf = new byte[8];
            for (int i = 7; i >= 0; i--)
            {
                buf[i] = (byte)(salt & 0xff);
                salt = salt >> 8;
            }
            return buf;
        }

        private static long NowUnixSeconds()
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalSeconds;
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
