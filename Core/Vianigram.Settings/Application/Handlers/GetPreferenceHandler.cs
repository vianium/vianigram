// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Application.UseCases;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Infrastructure;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Application.Handlers
{
    /// <summary>
    /// Reads the canonical string for a preference from
    /// <see cref="IPreferencesStore"/> and deserializes it into
    /// <typeparamref name="T"/> via <see cref="PreferenceSerializer"/>.
    ///
    /// Errors:
    ///   * <c>NotFound</c> from the store -&gt; the supplied default is
    ///     returned as a success (the absence of a value is not an error).
    ///   * <c>StorageError</c> -&gt; propagated.
    ///   * <c>TypeMismatch</c> -&gt; raised when the canonical string cannot
    ///     coerce into <typeparamref name="T"/>.
    /// </summary>
    internal sealed class GetPreferenceHandler<T>
    {
        private readonly IPreferencesStore _store;
        private readonly IComponentLogger _log;

        public GetPreferenceHandler(IPreferencesStore store, ILogger log)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (log == null) throw new ArgumentNullException("log");
            _store = store;
            _log = new TimestampedLogger(log, "Settings.GetPreference");
        }

        public async Task<Result<T, SettingsError>> HandleAsync(GetPreferenceCommand<T> cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<T, SettingsError>.Fail(SettingsError.Unknown("null command"));
            if (cmd.Key == null) return Result<T, SettingsError>.Fail(SettingsError.InvalidValue("key required"));

            var raw = await _store.GetRawAsync(cmd.Key.Name, ct).ConfigureAwait(false);
            if (raw.IsFail)
            {
                if (raw.Error.Kind == SettingsErrorKind.NotFound)
                {
                    return Result<T, SettingsError>.Ok(cmd.Default);
                }
                _log.Warn("store.GetRaw('" + cmd.Key.Name + "') failed: " + raw.Error);
                return Result<T, SettingsError>.Fail(raw.Error);
            }

            try
            {
                T value = PreferenceSerializer.Deserialize<T>(raw.Value, cmd.Default);
                return Result<T, SettingsError>.Ok(value);
            }
            catch (Exception ex)
            {
                return Result<T, SettingsError>.Fail(
                    SettingsError.TypeMismatch("could not deserialize '" + cmd.Key.Name + "' as " + typeof(T).Name + ": " + ex.Message));
            }
        }
    }
}
