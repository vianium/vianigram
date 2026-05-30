// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IMtProtoRpcPort.cs — Vianigram.Account.Ports.Outbound
// Outbound RPC port: byte-level CallAsync + 5 typed helpers used by the handlers.

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Outbound port for sending TL-serialized requests over the encrypted
    /// MTProto channel. <see cref="Infrastructure.MtProtoRpcAdapter"/> wraps
    /// the native <c>Vianigram.MTProto.MtProtoChannel</c>.
    ///
    /// All errors map to <see cref="MtProtoRpcError"/>; the handler layer
    /// translates that to <see cref="AccountError"/>.
    ///
    /// Five typed methods (QR login, profile, username check) let the handlers
    /// avoid TL-encoding requests inline. The byte-level <see cref="CallAsync"/>
    /// remains the canonical hatch for flows that already own their TL codecs
    /// (sendCode, signIn, logOut, checkPassword).
    /// </summary>
    public interface IMtProtoRpcPort
    {
        Task<Result<byte[], MtProtoRpcError>> CallAsync(byte[] requestBytes, CancellationToken ct);

        // ---- Typed helpers ---------------------------------------------------

        /// <summary>auth.exportLoginToken — issue a fresh QR-login session token.</summary>
        Task<Result<QrTokenResponse, AccountError>> AuthExportLoginTokenAsync(int apiId, string apiHash, CancellationToken ct);

        /// <summary>auth.importLoginToken — poll an existing QR-login token for status.</summary>
        Task<Result<QrPollResponse, AccountError>> AuthImportLoginTokenAsync(byte[] token, CancellationToken ct);

        /// <summary>users.getFullUser(inputUserSelf) — fetch the signed-in user's full profile.</summary>
        Task<Result<UserFullResponse, AccountError>> UsersGetFullUserAsync(InputUserSelf self, CancellationToken ct);

        /// <summary>account.updateProfile — update first/last name and bio.</summary>
        Task<Result<Unit, AccountError>> AccountUpdateProfileAsync(string firstName, string lastName, string about, CancellationToken ct);

        /// <summary>account.checkUsername — pre-flight check that a username is available.</summary>
        Task<Result<bool, AccountError>> AccountCheckUsernameAsync(string username, CancellationToken ct);

        /// <summary>
        /// auth.exportAuthorization — issued on the home DC after a successful
        /// auth.signIn. Returns a short-lived (id, bytes) pair the caller
        /// hands to a peer DC via <see cref="AuthImportAuthorizationAsync"/>
        /// to authenticate that DC's session without re-running signIn.
        /// </summary>
        Task<Result<ExportedAuthorizationResponse, AccountError>> AuthExportAuthorizationAsync(
            int targetDcId, CancellationToken ct);

        /// <summary>
        /// auth.importAuthorization — issued on the peer DC, consuming the
        /// blob from the home DC's <see cref="AuthExportAuthorizationAsync"/>
        /// call. Server returns auth.authorization (same shape as signIn).
        /// </summary>
        Task<Result<Unit, AccountError>> AuthImportAuthorizationAsync(
            long id, byte[] bytes, CancellationToken ct);
    }

    /// <summary>
    /// Optional companion port exposed by live MTProto transports that can
    /// switch DCs after PHONE_MIGRATE_X / NETWORK_MIGRATE_X.
    /// </summary>
    public interface IMtProtoDcProvider
    {
        int CurrentDcId { get; }
    }

    /// <summary>
    /// Optional companion port for login transports that can choose a better
    /// unauthenticated DC before the first auth.sendCode handshake.
    /// </summary>
    public interface IPhoneLoginDcPreferencePort
    {
        int PreferDcForPhone(string phoneE164);
    }

    /// <summary>
    /// Optional companion port for login transports that can open their
    /// unauthenticated MTProto channel before the first user-submitted RPC.
    /// </summary>
    public interface ILoginConnectionWarmupPort
    {
        Task WarmUpAsync(CancellationToken ct);
    }

    /// <summary>
    /// Optional companion port for login transports that can follow a
    /// server-issued <c>auth.loginTokenMigrateTo</c> by switching their
    /// unauthenticated channel to the indicated DC. The QR-login handler
    /// uses this after a poll surfaces a MigrateTo so the subsequent
    /// <c>auth.importLoginToken</c> can land on the correct DC.
    /// </summary>
    public interface IQrLoginMigrationPort
    {
        /// <summary>
        /// Switch the unauthenticated login transport to <paramref name="dcId"/>.
        /// Returns true if the switch was applied (subsequent RPCs will
        /// open / reuse a channel against the new DC).
        /// </summary>
        bool SwitchDcForQrMigration(int dcId);

        /// <summary>
        /// Restore the unauthenticated transport to the default anonymous
        /// login DC after a post-migrate import fails. The next QR refresh
        /// should export a new token from the anonymous DC instead of
        /// staying pinned to the failed migrate DC.
        /// </summary>
        void ResetQrMigrationAfterFailure();

        /// <summary>
        /// Generate (and persist) the auth_key for <paramref name="dcId"/>
        /// without changing the current channel or DC. Cheap no-op when the
        /// key is already cached. The QR-login VM fires this in the
        /// background as soon as the page opens so that, if the server
        /// later returns <c>auth.loginTokenMigrateTo</c>, the migrate's
        /// follow-up <c>auth.importLoginToken</c> can hit a warm
        /// auth_key cache instead of paying for a fresh DH handshake
        /// (which can take 5–15 s on slow phones).
        /// </summary>
        Task PrewarmAuthKeyAsync(int dcId, CancellationToken ct);

        /// <summary>
        /// Returns the auth_key that the unauthenticated channel for
        /// <paramref name="dcId"/> is currently encrypting frames with,
        /// or null when no channel is open for that DC.
        ///
        /// The QR-login handler uses this on
        /// <c>auth.loginTokenSuccess</c> to persist the truly-authorized
        /// key. The persistent store can drift from the channel's
        /// in-memory key in two known cases:
        ///   1) <c>LogoutHandler.DeleteAsync</c> wiped the slot while
        ///      the channel object stayed alive with its prior key, then
        ///      a fresh navigation to QR triggered a prewarm that
        ///      generated a different key into the empty slot.
        ///   2) A concurrent prewarm finished its DH handshake just
        ///      before the channel did, and saved a different key.
        ///
        /// In both cases the server authorized the channel's in-memory
        /// key (since that's what encrypted the export call), not the
        /// store's. Calling this lets the handler override the store
        /// with the channel's key.
        /// </summary>
        AuthKeyRecord TryGetCurrentChannelAuthKey(int dcId);
    }
}
