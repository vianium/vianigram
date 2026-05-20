// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LocalSettingsPreferencesStore.cs
//
// Disk-backed IPreferencesStore against
// Windows.Storage.ApplicationData.Current.LocalSettings. Replaces the
// in-memory placeholder so saved preferences (theme, language, MTProxy
// config, etc.) survive app launches.
//
// Per-key strategy:
//   * All values < 8 KB are written to LocalSettings.Values[<key>].
//     LocalSettings imposes a 64 KB hard cap per setting + 8 KB safe
//     roaming-friendly cap; we stay under the safe cap.
//
//   * Oversized values fall through to LocalFolder/preferences/<safe_key>.txt.
//     The Vianigram preference catalog has no values that large today —
//     this branch is a safety net for future composites.
//
// Threading: WinRT LocalSettings is documented as thread-safe for
// individual key reads/writes. We don't take an extra lock because
// settings_api.SetAsync already serialises per-key writes through the
// SettingsApplication command dispatcher.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;
using Windows.Storage;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class LocalSettingsPreferencesStore : IPreferencesStore
    {
        private const int LocalSettingsValueCap = 8 * 1024;
        private const string LargeValuePrefix = "@@FILE:";   // marker stored under the key when the value spilled to LocalFolder

        private readonly IComponentLogger _log;
        private readonly ApplicationDataContainer _localSettings;

        public LocalSettingsPreferencesStore(ILogger log)
        {
            if (log == null) throw new ArgumentNullException("log");
            _log = new TimestampedLogger(log, "Settings.LocalStore");
            _localSettings = ApplicationData.Current.LocalSettings;
        }

        public Task<Result<string, SettingsError>> GetRawAsync(string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key))
            {
                return TaskOk(string.Empty);
            }
            try
            {
                object boxed;
                if (!_localSettings.Values.TryGetValue(key, out boxed) || boxed == null)
                {
                    return TaskOk(string.Empty);
                }
                string raw = boxed as string;
                if (raw == null) return TaskOk(string.Empty);

                if (raw.StartsWith(LargeValuePrefix, StringComparison.Ordinal))
                {
                    string fileName = raw.Substring(LargeValuePrefix.Length);
                    return ReadLargeAsync(fileName, ct);
                }
                return TaskOk(raw);
            }
            catch (Exception ex)
            {
                _log.Warn("GetRawAsync('" + key + "') threw: " + ex.Message);
                return TaskFail<string>(SettingsError.Unknown("GetRawAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SettingsError>> SetRawAsync(string key, string value, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("key required"));
            }
            value = value ?? string.Empty;
            try
            {
                if (value.Length * 2 /* UTF-16 worst case */ <= LocalSettingsValueCap)
                {
                    _localSettings.Values[key] = value;
                    return Result<Unit, SettingsError>.Ok(Unit.Value);
                }

                // Spill path — write the payload to LocalFolder and store
                // a pointer in LocalSettings. Filename = sanitized key.
                string fileName = SanitizeForFile(key) + ".txt";
                StorageFolder folder = await GetSpillFolderAsync().ConfigureAwait(false);
                StorageFile file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, value);
                _localSettings.Values[key] = LargeValuePrefix + fileName;
                return Result<Unit, SettingsError>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                _log.Warn("SetRawAsync('" + key + "') threw: " + ex.Message);
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SetRawAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SettingsError>> RemoveAsync(string key, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Result<Unit, SettingsError>.Ok(Unit.Value);
            }
            try
            {
                object boxed;
                if (_localSettings.Values.TryGetValue(key, out boxed))
                {
                    string raw = boxed as string;
                    if (raw != null && raw.StartsWith(LargeValuePrefix, StringComparison.Ordinal))
                    {
                        string fileName = raw.Substring(LargeValuePrefix.Length);
                        try
                        {
                            StorageFolder folder = await GetSpillFolderAsync().ConfigureAwait(false);
                            StorageFile file = await folder.GetFileAsync(fileName);
                            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        }
                        catch (Exception)
                        {
                            // Best-effort; the LocalSettings pointer is the
                            // source of truth and we're about to remove it.
                        }
                    }
                    _localSettings.Values.Remove(key);
                }
                return Result<Unit, SettingsError>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                _log.Warn("RemoveAsync('" + key + "') threw: " + ex.Message);
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("RemoveAsync failed", ex));
            }
        }

        public async Task<Result<IDictionary<string, string>, SettingsError>> GetAllAsync(CancellationToken ct)
        {
            try
            {
                var snapshot = new Dictionary<string, string>(_localSettings.Values.Count);
                foreach (KeyValuePair<string, object> kv in _localSettings.Values)
                {
                    string raw = kv.Value as string;
                    if (raw == null) continue;
                    if (raw.StartsWith(LargeValuePrefix, StringComparison.Ordinal))
                    {
                        string fileName = raw.Substring(LargeValuePrefix.Length);
                        Result<string, SettingsError> r = await ReadLargeAsync(fileName, ct).ConfigureAwait(false);
                        snapshot[kv.Key] = r.IsOk ? r.Value : string.Empty;
                    }
                    else
                    {
                        snapshot[kv.Key] = raw;
                    }
                }
                return Result<IDictionary<string, string>, SettingsError>.Ok(snapshot);
            }
            catch (Exception ex)
            {
                _log.Warn("GetAllAsync threw: " + ex.Message);
                return Result<IDictionary<string, string>, SettingsError>.Fail(SettingsError.Unknown("GetAllAsync failed", ex));
            }
        }

        // -----------------------------------------------------------------

        private static Task<Result<string, SettingsError>> TaskOk(string v)
        {
            var tcs = new TaskCompletionSource<Result<string, SettingsError>>();
            tcs.SetResult(Result<string, SettingsError>.Ok(v));
            return tcs.Task;
        }

        private static Task<Result<T, SettingsError>> TaskFail<T>(SettingsError err)
        {
            var tcs = new TaskCompletionSource<Result<T, SettingsError>>();
            tcs.SetResult(Result<T, SettingsError>.Fail(err));
            return tcs.Task;
        }

        private static async Task<Result<string, SettingsError>> ReadLargeAsync(string fileName, CancellationToken ct)
        {
            try
            {
                StorageFolder folder = await GetSpillFolderAsync().ConfigureAwait(false);
                StorageFile file = await folder.GetFileAsync(fileName);
                string text = await FileIO.ReadTextAsync(file);
                return Result<string, SettingsError>.Ok(text ?? string.Empty);
            }
            catch (Exception ex)
            {
                return Result<string, SettingsError>.Fail(SettingsError.Unknown("ReadLargeAsync failed", ex));
            }
        }

        private static async Task<StorageFolder> GetSpillFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                "preferences", CreationCollisionOption.OpenIfExists);
        }

        private static string SanitizeForFile(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '.' || char.IsLetterOrDigit(c)) sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }
    }
}
