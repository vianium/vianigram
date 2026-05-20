// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IPreferredDcStore.cs — Vianigram.Account.Ports.Outbound
//
// Persists the user's "home DC" — the DC where auth.signIn last completed
// successfully. The composition root reads this at boot to seed the main
// MtProtoChannelAdapter with the right DC, instead of always defaulting to
// TelegramAppConfig.ActiveDcId. Without this the post-login Sync layer
// connects to a stranger DC, generates an unauthorized auth_key, and
// updates.getState fails with AUTH_KEY_UNREGISTERED.
//
// Implementations are expected to be cheap and synchronous — typically a
// single LocalSettings read/write on the host platform.

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Read/write the persisted "home DC" id and the authenticated user id
    /// for the signed-in account. The user id is what lets the boot path
    /// re-hydrate the AccountIdentity aggregate to Authorized — without it
    /// the auth_key cache hits but the app still treats the user as
    /// anonymous and routes them back to login.
    /// </summary>
    public interface IPreferredDcStore
    {
        /// <summary>
        /// Returns the persisted DC id (positive int) or <c>0</c> if no
        /// preference has been recorded yet (fresh install or pre-login).
        /// Never throws.
        /// </summary>
        int GetHomeDcId();

        /// <summary>
        /// Persist <paramref name="dcId"/> as the home DC. Values &lt;= 0 are
        /// silently ignored (defensive — handlers may probe an undecided
        /// CurrentDcId before the channel migrates). Never throws.
        /// </summary>
        void SetHomeDcId(int dcId);

        /// <summary>
        /// Returns the anonymous login DC hint (positive int) or <c>0</c> if
        /// no pre-auth hint has been learned yet. Never throws.
        /// </summary>
        int GetLoginDcHint();

        /// <summary>
        /// Persist <paramref name="dcId"/> as the next anonymous login DC
        /// seed. Values &lt;= 0 are ignored. Never throws.
        /// </summary>
        void SetLoginDcHint(int dcId);

        /// <summary>
        /// Returns the persisted Telegram user id (positive long) or
        /// <c>0</c> if no user is currently signed in. Never throws.
        /// </summary>
        long GetUserId();

        /// <summary>
        /// Persist <paramref name="userId"/> as the signed-in user. Values
        /// &lt;= 0 are silently ignored. Never throws.
        /// </summary>
        void SetUserId(long userId);

        /// <summary>
        /// Forget the persisted home DC and user id (logout / account switch).
        /// </summary>
        void Clear();
    }
}
