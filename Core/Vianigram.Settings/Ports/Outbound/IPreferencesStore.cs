// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the underlying KV store backing the Settings
    /// aggregate. The in-memory implementation
    /// (<c>InMemoryPreferencesStore</c>) keeps every value in process memory;
    /// the real adapter wraps
    /// <c>Windows.Storage.ApplicationData.Current.LocalSettings.Values</c> and
    /// lives in <c>Vianigram.Storage</c> (or the App composition root) where
    /// the WinRT dependency is acceptable.
    ///
    /// Contract:
    ///   - All values are persisted as canonical UTF-8 strings (JSON or scalar
    ///     literal). Type marshalling is owned by the application layer's
    ///     serializer so this port is free of CLR-type knowledge.
    ///   - Operations return <c>Result&lt;_, SettingsError&gt;</c> and never
    ///     throw across the port. Network / disk / quota faults map to
    ///     <c>SettingsError.StorageError</c>; missing keys map to
    ///     <c>SettingsError.NotFound</c>.
    ///   - Implementations MUST be thread-safe; the application layer issues
    ///     concurrent reads while a single write is in flight.
    /// </summary>
    public interface IPreferencesStore
    {
        /// <summary>
        /// Read the canonical string under <paramref name="key"/>. Returns
        /// <c>SettingsError.NotFound</c> when no value has been stored.
        /// </summary>
        Task<Result<string, SettingsError>> GetRawAsync(string key, CancellationToken ct);

        /// <summary>Upsert the canonical string under <paramref name="key"/>.</summary>
        Task<Result<Unit, SettingsError>> SetRawAsync(string key, string value, CancellationToken ct);

        /// <summary>
        /// Drop the value stored under <paramref name="key"/>. Returns
        /// success when the key was already absent.
        /// </summary>
        Task<Result<Unit, SettingsError>> RemoveAsync(string key, CancellationToken ct);

        /// <summary>
        /// Snapshot every stored (key, value) pair. Used for export, reset
        /// (returns the keys to delete), and aggregate hydration on cold start.
        /// </summary>
        Task<Result<IDictionary<string, string>, SettingsError>> GetAllAsync(CancellationToken ct);
    }
}
