// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Storage.Application;
using Vianigram.Storage.Infrastructure;
using Vianigram.Storage.Infrastructure.Repositories;
using Vianigram.Storage.Infrastructure.Sqlite;
using Vianigram.Storage.Ports.Stubs;
using Windows.Storage;

namespace Vianigram.Storage.Composition
{
    /// <summary>
    /// Factory of the Storage adapter set.
    /// </summary>
    public static class StorageCompositionRoot
    {
        private const string MigrationCompleteKey = "Storage.SqliteLegacyMigrationComplete";

        /// <summary>
        /// Build the Storage adapter set. The caller owns registration into the
        /// host composition root.
        /// </summary>
        public static StorageRegistrations Build()
        {
            var totalSw = Stopwatch.StartNew();
            var phaseSw = Stopwatch.StartNew();

            PlatformDataProtector protector = new PlatformDataProtector();
            phaseSw.Stop();
            EarlyLog.Write("Storage", "protector-init elapsed=" +
                phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            SqliteDatabase db = null;
            try
            {
                phaseSw.Restart();
                db = SqliteDatabase.Acquire();
                phaseSw.Stop();
                EarlyLog.Write("Storage", "sqlite-acquire elapsed=" +
                    phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

                // Legacy .bin migration is scheduled explicitly by the app
                // after the first interactive load. Build() stays strictly on
                // the critical startup path.
            }
            catch (Exception ex)
            {
                phaseSw.Stop();
                EarlyLog.Write("Storage", "sqlite-acquire failed elapsed=" +
                    phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "ms " + ex.GetType().Name + ": " + ex.Message);
                db = null;
            }

            IAuthKeyStore authKeys;
            IDialogRepository dialogs;
            IMessageRepository messages;
            ISyncStateRepository syncState;
            IEndpointHealthStore endpointHealth;
            IDcOptionsStore dcOptions;
            IAvatarCacheStore avatarCache;
            IImportedAuthorizationCacheStore importedAuth;

            phaseSw.Restart();
            if (db != null)
            {
                var legacyAuthKeys = new JsonAuthKeyStore(
                    new SqliteObjectStore<AuthKeyStoreState>(db, "auth_keys", true, protector));
                authKeys = new SqliteAuthKeyStore(db, protector, legacyAuthKeys);

                dialogs = new JsonDialogRepository(
                    new SqliteObjectStore<DialogRepositoryState>(db, "dialogs", false, null));

                messages = new JsonMessageRepository(
                    new SqliteObjectStore<MessageRepositoryState>(db, "messages", false, null));

                syncState = new JsonSyncStateRepository(
                    new SqliteObjectStore<SyncStateRepositoryState>(db, "sync_state", false, null));

                endpointHealth = new SqliteEndpointHealthStore(db);
                dcOptions = new SqliteDcOptionsStore(db);
                avatarCache = new SqliteAvatarCacheStore(db);
                importedAuth = new SqliteImportedAuthorizationCacheStore(db);
            }
            else
            {
                authKeys = new JsonAuthKeyStore(protector);
                dialogs = new JsonDialogRepository();
                messages = new JsonMessageRepository();
                syncState = new JsonSyncStateRepository();
                endpointHealth = null;
                dcOptions = null;
                avatarCache = null;
                importedAuth = null;
            }
            phaseSw.Stop();
            EarlyLog.Write("Storage", "repositories-wire elapsed=" +
                phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            totalSw.Stop();
            EarlyLog.Write("Storage", "build total=" +
                totalSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "ms backend=" + (db == null ? "json" : "sqlite"));

            return new StorageRegistrations(
                protector, authKeys, dialogs, messages, syncState,
                endpointHealth, dcOptions, avatarCache, importedAuth);
        }

        public static void ScheduleDeferredMaintenance()
        {
            Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                    if (IsMigrationComplete())
                    {
                        sw.Stop();
                        EarlyLog.Write("Storage", "sqlite-migration-bg skipped elapsed=" +
                            sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            "ms reason=complete");
                        return;
                    }

                    SqliteDatabase db = SqliteDatabase.Acquire();
                    int migrated = await SqliteMigrationRunner.RunAsync(db).ConfigureAwait(false);
                    MarkMigrationComplete();
                    sw.Stop();
                    EarlyLog.Write("Storage", "sqlite-migration-bg elapsed=" +
                        sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "ms migrated=" + migrated.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    EarlyLog.Write("Storage", "sqlite-migration-bg failed elapsed=" +
                        sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "ms " + ex.GetType().Name + ": " + ex.Message);
                }
            });
        }

        private static bool IsMigrationComplete()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return false;
                object raw;
                if (!settings.Values.TryGetValue(MigrationCompleteKey, out raw)) return false;
                return raw is bool && (bool)raw;
            }
            catch
            {
                return false;
            }
        }

        private static void MarkMigrationComplete()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                settings.Values[MigrationCompleteKey] = true;
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Bundle of Storage-published stub-shaped ports. The host composition root
    /// owns the lifetime of these instances.
    /// </summary>
    public sealed class StorageRegistrations
    {
        public StorageRegistrations(
            IDataProtector dataProtector,
            IAuthKeyStore authKeyStore,
            IDialogRepository dialogRepository,
            IMessageRepository messageRepository,
            ISyncStateRepository syncStateRepository,
            IEndpointHealthStore endpointHealthStore,
            IDcOptionsStore dcOptionsStore,
            IAvatarCacheStore avatarCacheStore,
            IImportedAuthorizationCacheStore importedAuthorizationCacheStore)
        {
            if (dataProtector == null) throw new ArgumentNullException("dataProtector");
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");
            if (dialogRepository == null) throw new ArgumentNullException("dialogRepository");
            if (messageRepository == null) throw new ArgumentNullException("messageRepository");
            if (syncStateRepository == null) throw new ArgumentNullException("syncStateRepository");

            // endpointHealthStore, dcOptionsStore, avatarCacheStore and
            // importedAuthorizationCacheStore are intentionally nullable —
            // in the JSON-fallback path (db == null) there is no backing
            // storage. Callers gracefully degrade to the process-only
            // behaviour when the store is null.
            DataProtector = dataProtector;
            AuthKeyStore = authKeyStore;
            DialogRepository = dialogRepository;
            MessageRepository = messageRepository;
            SyncStateRepository = syncStateRepository;
            EndpointHealthStore = endpointHealthStore;
            DcOptionsStore = dcOptionsStore;
            AvatarCacheStore = avatarCacheStore;
            ImportedAuthorizationCacheStore = importedAuthorizationCacheStore;
        }

        public IDataProtector DataProtector { get; private set; }
        public IAuthKeyStore AuthKeyStore { get; private set; }
        public IDialogRepository DialogRepository { get; private set; }
        public IMessageRepository MessageRepository { get; private set; }
        public ISyncStateRepository SyncStateRepository { get; private set; }
        public IEndpointHealthStore EndpointHealthStore { get; private set; }
        public IDcOptionsStore DcOptionsStore { get; private set; }
        public IAvatarCacheStore AvatarCacheStore { get; private set; }
        public IImportedAuthorizationCacheStore ImportedAuthorizationCacheStore { get; private set; }
    }
}
