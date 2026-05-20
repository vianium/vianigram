// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Settings bounded context (V1 shape). Every method
    /// is async, takes a <see cref="CancellationToken"/>, and returns
    /// <c>Result&lt;T, SettingsError&gt;</c>; no exceptions cross this boundary.
    ///
    /// Consumers: presentation/ViewModels (Settings page + sub-pages),
    /// <c>Vianigram.Stickers</c> (autoplay flags), <c>Vianigram.Notifications</c>
    /// (preview / sound flags), <c>Vianigram.Privacy</c> (passcode flags),
    /// <c>Vianigram.Media</c> (auto-download policy), composition root for
    /// wiring the domain events into a CLR event surface.
    ///
    /// Cross-context callers wrap this API behind ACL adapters defined in
    /// <c>Vianigram.Composition</c> (one adapter per consuming context — see
    /// <c>docs/managed-architecture/11-settings.md §9</c>).
    /// </summary>
    public interface ISettingsApi
    {
        // ---- generic typed access ----------------------------------------------

        /// <summary>
        /// Read the value stored under <paramref name="key"/>. Returns the
        /// catalog default when no user value is present.
        /// </summary>
        Task<Result<T, SettingsError>> GetAsync<T>(PreferenceKey key, CancellationToken ct);

        /// <summary>
        /// Persist <paramref name="value"/> under <paramref name="key"/>.
        /// Validators on the catalog (range / enum / shape) reject invalid
        /// inputs as <c>SettingsError.InvalidValue</c>; the previous stored
        /// value is preserved on rejection.
        /// </summary>
        Task<Result<Unit, SettingsError>> SetAsync<T>(PreferenceKey key, T value, CancellationToken ct);

        /// <summary>
        /// Reset every preference to its catalog default. The store is wiped
        /// and a single <c>PreferencesReset</c> domain event is emitted; the
        /// CLR <see cref="PreferenceChanged"/> event is NOT raised per-key to
        /// avoid event storms.
        /// </summary>
        Task<Result<Unit, SettingsError>> ResetAsync(CancellationToken ct);

        // ---- specialized facades ----------------------------------------------

        Task<Result<Theme, SettingsError>> GetThemeAsync(CancellationToken ct);
        Task<Result<Unit, SettingsError>> SetThemeAsync(Theme theme, CancellationToken ct);

        Task<Result<LanguagePack, SettingsError>> GetLanguageAsync(CancellationToken ct);
        Task<Result<Unit, SettingsError>> SetLanguageAsync(string langCode, CancellationToken ct);

        /// <summary>
        /// Read the auto-download policy for the supplied
        /// <paramref name="network"/>. Falls back to the well-known default
        /// (<c>DataUsagePolicy.Default*</c>) when no user value is present.
        /// </summary>
        Task<Result<DataUsagePolicy, SettingsError>> GetDataUsageAsync(NetworkKind network, CancellationToken ct);
        Task<Result<Unit, SettingsError>> SetDataUsageAsync(DataUsagePolicy policy, CancellationToken ct);

        /// <summary>
        /// Re-hydrate from the server: fetches <c>langpack.getLangPack</c> for
        /// the active language pack and (best-effort) <c>account.getContentSettings</c>.
        /// Returns success when at least the language sync succeeded; partial
        /// content-settings failures are logged and do not poison the result.
        /// </summary>
        Task<Result<Unit, SettingsError>> SyncFromServerAsync(CancellationToken ct);

        /// <summary>
        /// Read the saved MTProxy descriptor. Returns
        /// <see cref="ProxyConfig.Disabled"/> when no proxy is configured.
        /// </summary>
        Task<Result<ProxyConfig, SettingsError>> GetProxyAsync(CancellationToken ct);

        /// <summary>
        /// Persist <paramref name="config"/> as the active proxy descriptor
        /// AND push it to the live MTProto transport (via the registered
        /// <see cref="Vianigram.Settings.Ports.Outbound.IProxyRuntimeSink"/>).
        ///
        /// Passing <see cref="ProxyConfig.Disabled"/> persists the
        /// disabled state and clears the runtime. Validation lives in
        /// the <see cref="ProxyConfig"/> constructor — malformed inputs
        /// surface as <c>SettingsError.InvalidValue</c>.
        /// </summary>
        Task<Result<Unit, SettingsError>> SetProxyAsync(ProxyConfig config, CancellationToken ct);

        /// <summary>
        /// Live obfuscated-handshake probe against the proxy described
        /// by <paramref name="config"/>. Performs the full 64-byte
        /// MTProxy init exchange without touching the active runtime,
        /// so an in-progress channel session is not disturbed.
        ///
        /// The probe does NOT verify the secret is correct — that is
        /// only knowable after Telegram's DH handshake (which lives
        /// behind <c>SetProxyAsync</c> + the next channel open). It
        /// DOES rule out host typos, blocked ports, network-level
        /// blockage, and dead proxies.
        /// </summary>
        Task<ProxyProbeResult> TestProxyAsync(ProxyConfig config, CancellationToken ct);

        /// <summary>
        /// CLR event raised whenever a preference value changes. Multicast,
        /// thread-safe add/remove. Includes specialized changes (theme,
        /// language, data policy) — subscribers narrow on <see cref="PreferenceChangedEventArgs.Key"/>.
        /// </summary>
        event EventHandler<PreferenceChangedEventArgs> PreferenceChanged;
    }
}
