// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IAccountApi.cs — Vianigram.Account.Ports.Inbound
// Stable v1 inbound API for the Account bounded context (auth + QR + profile).

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Application;
using Vianigram.Account.Application.Commands;
using Vianigram.Account.Domain.Entities;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Account.Ports.Inbound
{
    /// <summary>
    /// Inbound contract exposed by the Account bounded context. Stable v1
    /// surface — breaking changes go through <c>Api/V2/</c>.
    /// </summary>
    public interface IAccountApi
    {
        /// <summary>Initiate login by issuing auth.sendCode for the given E.164 phone.</summary>
        Task<Result<Unit, AccountError>> SendPhoneCodeAsync(string phoneE164, CancellationToken ct);

        /// <summary>Request the next Telegram delivery method for the current code flow.</summary>
        Task<Result<Unit, AccountError>> ResendPhoneCodeAsync(CancellationToken ct);

        /// <summary>Submit the SMS / push code. Returns AuthOutcome with TwoFaRequired flag.</summary>
        Task<Result<AuthOutcome, AccountError>> VerifyCodeAsync(string code, CancellationToken ct);

        /// <summary>Register a new Telegram account after auth.authorizationSignUpRequired.</summary>
        Task<Result<AuthOutcome, AccountError>> SignUpAsync(string firstName, string lastName, CancellationToken ct);

        /// <summary>Submit the 2FA password (SRP-2048 inputCheckPasswordSRP).</summary>
        Task<Result<Unit, AccountError>> SubmitTwoFaPasswordAsync(string password, CancellationToken ct);

        /// <summary>Server-side logout + local auth_key wipe.</summary>
        Task<Result<Unit, AccountError>> LogoutAsync(CancellationToken ct);

        // ---- QR login + profile ----------------------------------------------

        /// <summary>
        /// auth.exportLoginToken — used both as the initial fetch and as
        /// the periodic poll: re-issuing exportLoginToken is how the
        /// unauthenticated client polls login status per Telegram's
        /// protocol. The result's <see cref="QrLoginStatus"/> tells the
        /// caller what to do next:
        ///   - Pending: render <see cref="QrLoginPoll.Token"/>.
        ///   - Accepted: auth_key persisted, navigate to ChatList.
        ///   - TwoFaRequired: aggregate primed, navigate to TwoFaPassword.
        ///   - Expired: ask again immediately.
        ///   - SignUpRequired: pivot to phone sign-up.
        /// </summary>
        Task<Result<QrLoginPoll, AccountError>> RequestQrTokenAsync(CancellationToken ct);

        /// <summary>
        /// Periodic poll. Functionally identical to
        /// <see cref="RequestQrTokenAsync"/> — the <paramref name="token"/>
        /// argument is retained for backward compatibility but is not used
        /// (per Telegram's protocol the unauthenticated client polls by
        /// re-issuing exportLoginToken, not by importing the prior token).
        /// </summary>
        Task<Result<QrLoginPoll, AccountError>> PollQrLoginAsync(QrLoginToken token, CancellationToken ct);

        /// <summary>
        /// Pre-warm the auth_key cache for the most likely QR-login
        /// migration target DCs. Fire-and-forget from the QR page so that,
        /// when the server returns <c>auth.loginTokenMigrateTo</c>, the
        /// follow-up <c>auth.importLoginToken</c> hits a warm cache
        /// instead of paying for a 5–15 s DH handshake on a slow phone.
        /// Cheap no-op when keys are already cached. Never throws.
        /// </summary>
        Task PrewarmQrLoginDcsAsync(CancellationToken ct);

        /// <summary>
        /// Register a platform push channel URI with Telegram so the server
        /// delivers raw push notifications when the app is suspended.
        /// <paramref name="tokenType"/> = 8 for WNS. Caller
        /// obtains the URI from
        /// <c>Windows.Networking.PushNotifications.PushNotificationChannelManager</c>.
        /// </summary>
        Task<Result<Unit, AccountError>> RegisterPushDeviceAsync(
            int tokenType, string token, byte[] secret, CancellationToken ct);

        /// <summary>
        /// Drop the registered MPNS push channel. Called on logout so the
        /// signed-out account stops receiving pushes via this device.
        /// </summary>
        Task<Result<Unit, AccountError>> UnregisterPushDeviceAsync(
            int tokenType, string token, CancellationToken ct);

        /// <summary>users.getFullUser(inputUserSelf) — read the signed-in user's profile.</summary>
        Task<Result<SelfProfile, AccountError>> GetSelfAsync(CancellationToken ct);

        /// <summary>account.updateProfile + (when changed) account.updateUsername.</summary>
        Task<Result<Unit, AccountError>> UpdateProfileAsync(string firstName, string lastName, string username, string bio, CancellationToken ct);

        /// <summary>account.checkUsername — pre-flight availability check.</summary>
        Task<Result<bool, AccountError>> CheckUsernameAsync(string username, CancellationToken ct);

        /// <summary>Read-only projection of the aggregate.</summary>
        AccountStateSnapshot CurrentState { get; }

        /// <summary>Raised after every state transition, scheduled on the publisher's thread.</summary>
        event EventHandler<AccountStateChanged> StateChanged;

        /// <summary>
        /// Boot-time hook called by the composition root before the host
        /// resolves the initial page. Reads any persisted (homeDcId, userId)
        /// marker and lifts the aggregate to Authorized so a
        /// previously signed-in user lands on ChatListPage instead of being
        /// asked for their phone again. The transport layer loads the auth_key
        /// lazily before the first RPC.
        /// </summary>
        /// <returns>true if the aggregate was restored; false on a clean
        /// (no persistence) boot or any storage failure.</returns>
        Task<bool> RehydrateFromPersistenceAsync(CancellationToken ct);
    }

    /// <summary>
    /// Optional login preflight surface. UI can call this opportunistically
    /// while the user finishes entering a valid phone number; failures are
    /// intentionally non-fatal because SendPhoneCodeAsync remains canonical.
    /// </summary>
    public interface IPhoneLoginPreparationApi
    {
        Task PreparePhoneLoginAsync(string phoneE164, CancellationToken ct);
    }
}
