// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LocalSettingsPreferredDcStore.cs — Vianigram.Composition.Infrastructure
//
// Trivial IPreferredDcStore implementation backed by Windows.Storage
// LocalSettings. Persists the home DC plus authenticated user marker, with
// string mirrors so older phone builds do not lose the session to a narrow
// property-type read on the next cold launch.
//
// We deliberately don't use the bigger IAuthKeyStore plumbing because:
//   1. Boot needs the home DC BEFORE the storage layer (sqlite, blobs) is
//      ready — LocalSettings is available immediately at App.OnLaunched.
//   2. The value is one int; a full async/store ceremony would be overkill.
//
// All methods are exception-safe: a degraded LocalSettings (e.g. unit tests
// without a CoreApplication context) returns 0 on read and silently no-ops
// on write. Callers downstream already treat 0 as "no preference recorded".

using System.Globalization;
using Vianigram.Account.Ports.Outbound;
using Windows.Storage;

namespace Vianigram.Composition.Infrastructure
{
    internal interface ILoginEndpointPreferenceStore
    {
        bool TryGetLoginEndpoint(int dcId, out string host, out int port);
        void SetLoginEndpoint(int dcId, string host, int port);
    }

    public sealed class LocalSettingsPreferredDcStore : IPreferredDcStore
    {
        private const string DcKey = "HomeDcId";
        private const string DcTextKey = "HomeDcIdText";
        private const string LoginDcHintKey = "LoginDcHint";
        private const string LoginDcHintTextKey = "LoginDcHintText";
        private const string LoginEndpointHostPrefix = "LoginEndpointHost.";
        private const string LoginEndpointPortPrefix = "LoginEndpointPort.";
        private const string LoginEndpointPortTextPrefix = "LoginEndpointPortText.";
        private const string UserIdKey = "AuthorisedUserId";
        private const string UserIdTextKey = "AuthorisedUserIdText";
        private const string LegacyUserIdKey = "AuthorizedUserId";
        private const string LegacyUserIdTextKey = "AuthorizedUserIdText";
        private const long MaxReasonableUserId = 100000000000000L;

        public int GetHomeDcId()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return 0;
                return ReadPositiveInt(settings, DcKey, DcTextKey);
            }
            catch
            {
                return 0;
            }
        }

        public void SetHomeDcId(int dcId)
        {
            if (dcId <= 0) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                settings.Values[DcTextKey] = dcId.ToString(CultureInfo.InvariantCulture);
                TrySetValue(settings, DcKey, dcId);
            }
            catch
            {
                // LocalSettings unavailable — degraded mode acceptable; the
                // user just pays the migration cost again next launch.
            }
        }

        public int GetLoginDcHint()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return 0;
                return ReadPositiveInt(settings, LoginDcHintKey, LoginDcHintTextKey);
            }
            catch
            {
                return 0;
            }
        }

        public void SetLoginDcHint(int dcId)
        {
            if (dcId <= 0) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                settings.Values[LoginDcHintTextKey] = dcId.ToString(CultureInfo.InvariantCulture);
                TrySetValue(settings, LoginDcHintKey, dcId);
            }
            catch
            {
            }
        }

        public bool TryGetLoginEndpoint(int dcId, out string host, out int port)
        {
            host = null;
            port = 0;
            if (dcId <= 0) return false;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return false;

                object rawHost;
                if (!settings.Values.TryGetValue(LoginEndpointHostKey(dcId), out rawHost))
                {
                    return false;
                }

                host = rawHost as string;
                if (string.IsNullOrWhiteSpace(host))
                {
                    host = null;
                    return false;
                }

                port = ReadPositiveInt(
                    settings,
                    LoginEndpointPortKey(dcId),
                    LoginEndpointPortTextKey(dcId));
                if (port <= 0 || port > 65535)
                {
                    host = null;
                    port = 0;
                    return false;
                }

                return true;
            }
            catch
            {
                host = null;
                port = 0;
                return false;
            }
        }

        public void SetLoginEndpoint(int dcId, string host, int port)
        {
            if (dcId <= 0 || string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
            {
                return;
            }

            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;

                string portText = port.ToString(CultureInfo.InvariantCulture);
                settings.Values[LoginEndpointHostKey(dcId)] = host;
                settings.Values[LoginEndpointPortTextKey(dcId)] = portText;
                TrySetValue(settings, LoginEndpointPortKey(dcId), port);
            }
            catch
            {
            }
        }

        public long GetUserId()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return 0L;
                return ReadPositiveLong(settings, UserIdKey, UserIdTextKey, LegacyUserIdKey, LegacyUserIdTextKey);
            }
            catch
            {
                return 0L;
            }
        }

        public void SetUserId(long userId)
        {
            if (userId <= 0L || userId > MaxReasonableUserId) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                string userIdText = userId.ToString(CultureInfo.InvariantCulture);
                settings.Values[UserIdTextKey] = userIdText;
                settings.Values[LegacyUserIdTextKey] = userIdText;
                TrySetValue(settings, UserIdKey, userId);
                TrySetValue(settings, LegacyUserIdKey, userId);
                RemoveIfPresent(settings, LoginDcHintKey);
                RemoveIfPresent(settings, LoginDcHintTextKey);
            }
            catch
            {
            }
        }

        public void Clear()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                if (settings.Values.ContainsKey(DcKey)) settings.Values.Remove(DcKey);
                if (settings.Values.ContainsKey(DcTextKey)) settings.Values.Remove(DcTextKey);
                if (settings.Values.ContainsKey(LoginDcHintKey)) settings.Values.Remove(LoginDcHintKey);
                if (settings.Values.ContainsKey(LoginDcHintTextKey)) settings.Values.Remove(LoginDcHintTextKey);
                if (settings.Values.ContainsKey(UserIdKey)) settings.Values.Remove(UserIdKey);
                if (settings.Values.ContainsKey(UserIdTextKey)) settings.Values.Remove(UserIdTextKey);
                if (settings.Values.ContainsKey(LegacyUserIdKey)) settings.Values.Remove(LegacyUserIdKey);
                if (settings.Values.ContainsKey(LegacyUserIdTextKey)) settings.Values.Remove(LegacyUserIdTextKey);
            }
            catch
            {
            }
        }

        private static int ReadPositiveInt(ApplicationDataContainer settings, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                object raw;
                if (!settings.Values.TryGetValue(keys[i], out raw)) continue;

                int value;
                if (TryReadPositiveInt(raw, out value))
                {
                    return value;
                }
            }

            return 0;
        }

        private static long ReadPositiveLong(ApplicationDataContainer settings, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                object raw;
                if (!settings.Values.TryGetValue(keys[i], out raw)) continue;

                long value;
                if (TryReadPositiveLong(raw, out value))
                {
                    return value;
                }
            }

            return 0L;
        }

        private static string LoginEndpointHostKey(int dcId)
        {
            return LoginEndpointHostPrefix + dcId.ToString(CultureInfo.InvariantCulture);
        }

        private static string LoginEndpointPortKey(int dcId)
        {
            return LoginEndpointPortPrefix + dcId.ToString(CultureInfo.InvariantCulture);
        }

        private static string LoginEndpointPortTextKey(int dcId)
        {
            return LoginEndpointPortTextPrefix + dcId.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryReadPositiveInt(object raw, out int value)
        {
            value = 0;
            if (raw is int)
            {
                value = (int)raw;
                return value > 0;
            }

            if (raw is long)
            {
                long v = (long)raw;
                if (v > 0L && v <= int.MaxValue)
                {
                    value = (int)v;
                    return true;
                }
            }

            if (raw is uint)
            {
                uint v = (uint)raw;
                if (v > 0U && v <= int.MaxValue)
                {
                    value = (int)v;
                    return true;
                }
            }

            if (raw is string)
            {
                long parsed;
                if (long.TryParse((string)raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
                    parsed > 0L &&
                    parsed <= int.MaxValue)
                {
                    value = (int)parsed;
                    return true;
                }
            }

            return false;
        }

        private static void TrySetValue(ApplicationDataContainer settings, string key, object value)
        {
            try
            {
                settings.Values[key] = value;
            }
            catch
            {
            }
        }

        private static void RemoveIfPresent(ApplicationDataContainer settings, string key)
        {
            try
            {
                if (settings.Values.ContainsKey(key)) settings.Values.Remove(key);
            }
            catch
            {
            }
        }

        private static bool TryReadPositiveLong(object raw, out long value)
        {
            value = 0L;
            if (raw is long)
            {
                value = (long)raw;
                return value > 0L && value <= MaxReasonableUserId;
            }

            if (raw is int)
            {
                int v = (int)raw;
                if (v > 0)
                {
                    value = v;
                    return true;
                }
            }

            if (raw is uint)
            {
                uint v = (uint)raw;
                if (v > 0U)
                {
                    value = v;
                    return true;
                }
            }

            if (raw is ulong)
            {
                ulong v = (ulong)raw;
                if (v > 0UL && v <= MaxReasonableUserId)
                {
                    value = (long)v;
                    return true;
                }
            }

            if (raw is string)
            {
                long parsed;
                if (long.TryParse((string)raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) &&
                    parsed > 0L &&
                    parsed <= MaxReasonableUserId)
                {
                    value = parsed;
                    return true;
                }
            }

            return false;
        }
    }
}
