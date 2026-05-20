# Vianigram.Privacy — Privacy & Lock Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md). This context covers the privacy mechanisms of a Telegram client: the app passcode lock (PIN + optional biometric), server-side privacy rules (last-seen, who-can-call, who-can-add-to-groups, who-can-forward, who-can-pfp), management of blocked users, and management of authorized sessions (active devices). The passcode also governs the **at-rest decryption** of the app's encrypted stores: without a passcode unlock, the encrypted blobs are not unlocked, and chat content is not hydrated.

---

## 1. Bounded context

- **Ubiquitous language:** passcode, PIN, biometric, lock screen, auto-lock interval, privacy rule, privacy key (`status_timestamp`, `phone_call`, `chat_invite`, `forwards`, `profile_photo`, `phone_number`, `voice_messages`), allow / disallow list, blocked user, active session (authorization), terminate session, account TTL, two-step verification (cloud password).
- **Aggregate root:** `PrivacyProfile` — the complete set: the passcode setting + the list of per-key privacy rules + the list of blocked users + the list of active sessions. One per authenticated account.
- **Secondary aggregates:**
  - `LockState` — the runtime state of the lock (locked / unlocked + lastUnlockUtc + failedAttempts). Frequently mutated, a process lifecycle.
- **Value objects:**
  - `PasscodeHash` — `(byte[] hash, byte[] salt, int iterationCount)`. Algorithm: PBKDF2-HMAC-SHA256, 100k iterations.
  - `PasscodeKind` — `None`, `Pin4`, `Pin6`, `Alphanumeric`.
  - `BiometricKind` — `None`, `Fingerprint`. WP8.1 does not expose face/iris in V1.
  - `AutoLockInterval` — enum: `Immediate`, `OneMinute`, `FiveMinutes`, `OneHour`, `Disabled`.
  - `PrivacyKey` — enum: `StatusTimestamp`, `PhoneCall`, `ChatInvite`, `Forwards`, `ProfilePhoto`, `PhoneNumber`, `VoiceMessages`, `AddedByPhone`.
  - `PrivacyRule` — discriminated: `AllowAll`, `DisallowAll`, `AllowContacts`, `DisallowContacts`, `AllowUsers(long[])`, `DisallowUsers(long[])`, `AllowChatParticipants(long[])`, `DisallowChatParticipants(long[])`. The effective rule is composed as an ordered list.
  - `BlockedUser` — `(userId, blockedAtUtc)`.
  - `ActiveSession` — `(hash, deviceModel, platform, systemVersion, appName, appVersion, dateCreatedUtc, dateActiveUtc, ip, country, region, isCurrent)`.
  - `KeyChainHandle` — an opaque handle of the DataProtectionProvider scope for wrapping/unwrapping blobs.
- **Domain events emitted:**
  - `PasscodeEnabled(PasscodeKind)`, `PasscodeDisabled`, `PasscodeChanged`, `PasscodeFailedAttempt(int count)`, `PasscodeLocked`, `PasscodeUnlocked`.
  - `PrivacyRulesUpdated(PrivacyKey, PrivacyRule rule)`.
  - `UserBlocked(userId)`, `UserUnblocked(userId)`.
  - `ActiveSessionTerminated(sessionHash)`, `AllOtherSessionsTerminated(int count)`.
  - `BiometricEnabled`, `BiometricDisabled`.
  - `AutoLockIntervalChanged(AutoLockInterval old, AutoLockInterval new)`.
- **Capabilities exposed:**
  - `privacy.passcode` — the app allows a passcode. Off in V1 ⇒ always unlocked.
  - `privacy.biometric` — the fingerprint API is available. Detected at boot via `Windows.Devices.Enumeration` or equivalent; off on Windows Phone 8.1 devices without a fingerprint.
  - `privacy.terminate_sessions` — UI Settings → Active Sessions.
  - `privacy.blocked_users` — UI Settings → Blocked Users.
  - `privacy.privacy_rules` — UI Settings → Privacy.
  - `privacy.account_ttl` — read/write `account.setAccountTTL` (when the account auto-deletes due to inactivity).

---

## 2. Goal

PivoraTelegram has `PasscodePage.xaml.cs`, `BlockedUsersPage.xaml.cs`, `ActiveSessionsPage.xaml.cs` with duplicated code: each one does its own direct TL call, its own parse, its own storage. Replace it with a context that:

1. **Centralizes** the privacy rules and the integration with TL.
2. **Implements the lock screen** in such a way that the rest of the app **does not read encrypted storage** while it is locked. The passcode is the PBKDF2 key that derives the `master decryption key` for `ISecretStore` and the `vianium-crypto` sibling (at-rest blobs).
3. **Synchronizes the privacy rules with the server**: V1 writes locally and sends `account.setPrivacy`; reads at boot with `account.getPrivacy` per key.
4. **Manages active sessions** with `account.getAuthorizations` + `account.resetAuthorization`.
5. **Auto-lock by timer** per the `AutoLockInterval`. When the app goes to background, start a timer; if it returns after the interval, lock.
6. **Failed attempts policy**: after 5 failed attempts, increment the delay (5s, 30s, 5min, 30min, 1h, locked permanently → require an account reset).

---

## 3. Lock interaction with encrypted storage

### The passcode is a cryptographic key

When the user enables a passcode:
1. Generate a random 32-byte `salt`.
2. Derive `derivedKey = PBKDF2-HMAC-SHA256(passcode, salt, 100_000, 32)`.
3. Generate a random 32-byte `masterKey` — this is the key that encrypts the at-rest blobs.
4. Wrap: `wrappedMasterKey = AES-GCM(derivedKey, masterKey)`.
5. Persist in `ISecretStore`: `(salt, wrappedMasterKey, iterationCount, passcodeHashFingerprint)`.

When the user unlocks:
1. Read `(salt, wrappedMasterKey, iterationCount, fingerprint)`.
2. Compute `derivedKey = PBKDF2(input, salt, iterationCount)`.
3. Verify `fingerprint == HMAC(derivedKey, "verify")`.
4. If it matches: unwrap `masterKey = AES-GCM-Decrypt(derivedKey, wrappedMasterKey)`.
5. Load `masterKey` into `LockState.MemoryKey` (zero on lock).
6. Publish `PasscodeUnlocked`. Other contexts that consume encrypted blobs (Auth, Messaging, SecretChats) react to the event and begin to hydrate.

When locking:
1. Zero `LockState.MemoryKey` (memset 0).
2. Publish `PasscodeLocked`. Consumers clear the in-memory state of decrypted blobs (lazy: the next read will fail and the VM will show the lock screen).

### Without a passcode

`privacy.passcode_enabled = false` ⇒ the `masterKey` is stored encrypted by `DataProtectionProvider` (DPAPI) under the app's scope. This gives at-rest encryption against "someone copies the SD card", but not against "someone with access to the unlocked device". That is the trade-off the user chooses by not enabling a passcode.

---

## 4. Native target — the `Vianigram.Privacy` project

```
Core/Vianigram.Privacy/
├── Vianigram.Privacy.csproj                   (WP8.1)
├── Properties/AssemblyInfo.cs
│
├── Domain/
│   ├── ValueObjects/
│   │   ├── PasscodeHash.cs
│   │   ├── PasscodeKind.cs
│   │   ├── BiometricKind.cs
│   │   ├── AutoLockInterval.cs
│   │   ├── PrivacyKey.cs
│   │   ├── PrivacyRule.cs
│   │   ├── BlockedUser.cs
│   │   ├── ActiveSession.cs
│   │   └── KeyChainHandle.cs
│   ├── Aggregates/
│   │   ├── PrivacyProfile.cs                  (root)
│   │   └── LockState.cs                       (runtime aggregate)
│   ├── Events/
│   │   ├── PasscodeEnabled.cs
│   │   ├── PasscodeDisabled.cs
│   │   ├── PasscodeChanged.cs
│   │   ├── PasscodeFailedAttempt.cs
│   │   ├── PasscodeLocked.cs
│   │   ├── PasscodeUnlocked.cs
│   │   ├── PrivacyRulesUpdated.cs
│   │   ├── UserBlocked.cs
│   │   ├── UserUnblocked.cs
│   │   ├── ActiveSessionTerminated.cs
│   │   ├── AllOtherSessionsTerminated.cs
│   │   ├── BiometricEnabled.cs
│   │   ├── BiometricDisabled.cs
│   │   └── AutoLockIntervalChanged.cs
│   ├── Services/
│   │   ├── PasscodeKdfService.cs              (PBKDF2-HMAC-SHA256)
│   │   ├── PasscodeFingerprintService.cs      (HMAC verify-only fingerprint)
│   │   ├── MasterKeyWrapService.cs            (AES-GCM wrap/unwrap)
│   │   ├── PrivacyRuleComposer.cs             (an ordered allow/disallow list → effective rule)
│   │   ├── AutoLockTimer.cs                   (a mid-class managed timer)
│   │   └── FailedAttemptsBackoff.cs           (5s, 30s, 5min, 30min, 1h)
│   ├── Policies/
│   │   ├── PasscodeStrengthPolicy.cs          (PIN4 = 4 digits; PIN6 = 6; Alphanumeric ≥6 with a mix)
│   │   ├── BiometricFallbackPolicy.cs         (after N failed bio, fallback to passcode)
│   │   └── BlockedUsersLimitsPolicy.cs        (max 1000)
│   └── Errors/
│       └── PrivacyErrors.cs
│
├── Application/
│   ├── Commands/
│   │   ├── EnablePasscodeCommand.cs
│   │   ├── DisablePasscodeCommand.cs
│   │   ├── ChangePasscodeCommand.cs
│   │   ├── UnlockCommand.cs
│   │   ├── LockCommand.cs
│   │   ├── EnableBiometricCommand.cs
│   │   ├── DisableBiometricCommand.cs
│   │   ├── SetAutoLockIntervalCommand.cs
│   │   ├── UpdatePrivacyRulesCommand.cs
│   │   ├── BlockUserCommand.cs
│   │   ├── UnblockUserCommand.cs
│   │   ├── TerminateSessionCommand.cs
│   │   └── TerminateAllOtherSessionsCommand.cs
│   ├── Queries/
│   │   ├── GetPrivacyRulesQuery.cs
│   │   ├── ListBlockedUsersQuery.cs
│   │   └── ListActiveSessionsQuery.cs
│   ├── UseCases/
│   │   ├── EnablePasscodeUseCase.cs
│   │   ├── DisablePasscodeUseCase.cs
│   │   ├── ChangePasscodeUseCase.cs
│   │   ├── UnlockUseCase.cs                   (verify + unwrap masterKey + publish unlocked)
│   │   ├── LockUseCase.cs                     (zero memoryKey + publish locked)
│   │   ├── AutoLockArmUseCase.cs              (on app suspend / focus loss)
│   │   ├── AutoLockTickUseCase.cs             (on resume → check elapsed, lock if past interval)
│   │   ├── EnableBiometricUseCase.cs
│   │   ├── DisableBiometricUseCase.cs
│   │   ├── BiometricUnlockUseCase.cs
│   │   ├── UpdatePrivacyRulesUseCase.cs       (TL setPrivacy + publish event)
│   │   ├── LoadPrivacyRulesUseCase.cs         (TL getPrivacy at boot)
│   │   ├── BlockUserUseCase.cs
│   │   ├── UnblockUserUseCase.cs
│   │   ├── ListBlockedUsersUseCase.cs
│   │   ├── ListActiveSessionsUseCase.cs
│   │   ├── TerminateSessionUseCase.cs
│   │   └── TerminateAllOtherSessionsUseCase.cs
│   └── Internal/
│       └── PasscodeAttemptRateLimiter.cs
│
├── Ports/
│   ├── Inbound/
│   │   └── IPrivacyApi.cs
│   └── Outbound/
│       ├── IPrivacyTlGateway.cs               (account.getPrivacy/setPrivacy/etc.)
│       ├── ISecretStore.cs                    (re-export from the Kernel — wraps DataProtectionProvider)
│       ├── IPasscodeMaterialStore.cs          (salt + wrappedMasterKey + fingerprint)
│       ├── IBiometricGateway.cs               (WP8.1 fingerprint API if it exists)
│       ├── IAppFocusEvents.cs                 (suspended/resumed)
│       └── IClock.cs                          (re-export from the Kernel)
│
├── Infrastructure/
│   ├── Crypto/
│   │   ├── Pbkdf2HmacSha256.cs                (BCL CryptographicEngine.DeriveKey)
│   │   ├── AesGcmWrap.cs                      (wrap/unwrap masterKey)
│   │   └── HmacFingerprint.cs
│   ├── Tl/
│   │   ├── TlPrivacyGateway.cs
│   │   └── TlPrivacyMappers.cs
│   ├── Persistence/
│   │   ├── DataProtectedPasscodeStore.cs      (impl IPasscodeMaterialStore)
│   │   └── PrivacyProfileSnapshot.cs
│   ├── Biometric/
│   │   └── WindowsHelloBiometricGateway.cs    (placeholder; WP8.1 limited)
│   └── Lifecycle/
│       └── AppFocusEventsAdapter.cs           (CoreApplication.Suspending/Resuming)
│
└── Api/
    └── V1/
        ├── IPrivacyApi.cs
        ├── PrivacyRuleDto.cs
        ├── BlockedUserDto.cs
        ├── ActiveSessionDto.cs
        ├── PasscodeRequest.cs
        ├── UnlockRequest.cs
        ├── UnlockResult.cs
        └── PrivacyApiErrors.cs
```

---

## 5. Inbound — `IPrivacyApi`

```csharp
namespace Vianigram.Privacy.Api.V1
{
    public interface IPrivacyApi
    {
        // Passcode
        Task<Result<bool, Error>> EnablePasscodeAsync(string passcode, PasscodeKind kind, CancellationToken ct);
        Task<Result<bool, Error>> DisablePasscodeAsync(string currentPasscode, CancellationToken ct);
        Task<Result<bool, Error>> ChangePasscodeAsync(string oldPasscode, string newPasscode, PasscodeKind newKind, CancellationToken ct);
        Task<Result<UnlockResult, Error>> UnlockAsync(string passcode, CancellationToken ct);
        Task<Result<bool, Error>> LockAsync(CancellationToken ct);
        Task<Result<bool, Error>> SetAutoLockIntervalAsync(AutoLockInterval interval, CancellationToken ct);

        // Biometric
        Task<Result<bool, Error>> EnableBiometricAsync(string passcode, CancellationToken ct);
        Task<Result<bool, Error>> DisableBiometricAsync(CancellationToken ct);
        Task<Result<UnlockResult, Error>> BiometricUnlockAsync(CancellationToken ct);

        // Privacy rules
        Task<Result<PrivacyRuleDto, Error>> GetPrivacyRulesAsync(PrivacyKey key, CancellationToken ct);
        Task<Result<bool, Error>> UpdatePrivacyRulesAsync(PrivacyKey key, PrivacyRuleDto rule, CancellationToken ct);

        // Blocked users
        Task<Result<IReadOnlyList<BlockedUserDto>, Error>> ListBlockedUsersAsync(int offset, int limit, CancellationToken ct);
        Task<Result<bool, Error>> BlockUserAsync(long userId, CancellationToken ct);
        Task<Result<bool, Error>> UnblockUserAsync(long userId, CancellationToken ct);

        // Active sessions
        Task<Result<IReadOnlyList<ActiveSessionDto>, Error>> ListActiveSessionsAsync(CancellationToken ct);
        Task<Result<bool, Error>> TerminateSessionAsync(long hash, CancellationToken ct);
        Task<Result<bool, Error>> TerminateAllOtherSessionsAsync(CancellationToken ct);
    }
}
```

---

## 6. Outbound — `IPrivacyTlGateway`

```csharp
public interface IPrivacyTlGateway
{
    Task<Result<TlPrivacyRules, Error>> GetPrivacyAsync(PrivacyKey key, CancellationToken ct);
    Task<Result<TlPrivacyRules, Error>> SetPrivacyAsync(PrivacyKey key, IReadOnlyList<TlInputPrivacyRule> rules, CancellationToken ct);
    Task<Result<TlBlockedSlice, Error>> GetBlockedAsync(int offset, int limit, CancellationToken ct);
    Task<Result<bool, Error>> BlockAsync(long userId, long accessHash, CancellationToken ct);
    Task<Result<bool, Error>> UnblockAsync(long userId, long accessHash, CancellationToken ct);
    Task<Result<TlAuthorizations, Error>> GetAuthorizationsAsync(CancellationToken ct);
    Task<Result<bool, Error>> ResetAuthorizationAsync(long hash, CancellationToken ct);
    Task<Result<bool, Error>> ResetWebAuthorizationsAsync(CancellationToken ct);
    Task<Result<int, Error>> GetAccountTtlAsync(CancellationToken ct);
    Task<Result<bool, Error>> SetAccountTtlAsync(int days, CancellationToken ct);
}
```

---

## 7. Notable use cases

### `EnablePasscodeUseCase`

```csharp
public sealed class EnablePasscodeUseCase
{
    private readonly IPasscodeMaterialStore _materialStore;
    private readonly ISecretStore _secrets;
    private readonly PasscodeKdfService _kdf;
    private readonly MasterKeyWrapService _wrap;
    private readonly PasscodeFingerprintService _fingerprint;
    private readonly IRandom _random;
    private readonly IEventBus _bus;
    private readonly IClock _clock;

    public async Task<Result<bool, Error>> ExecuteAsync(string passcode, PasscodeKind kind, CancellationToken ct)
    {
        var policyOk = PasscodeStrengthPolicy.Validate(passcode, kind);
        if (!policyOk.IsOk) return Result.Fail<bool, Error>(policyOk.Error);

        var salt = _random.NextBytes(32);
        var derivedKey = _kdf.Derive(passcode, salt, iterationCount: 100_000, outputLen: 32);

        // If a masterKey already exists (i.e., the app was without a passcode using DPAPI), reuse it so as not to lose data.
        var existingMaster = await _secrets.GetAsync("vng.master_key", ct).ConfigureAwait(false);
        byte[] masterKey = existingMaster.IsOk ? existingMaster.Value : _random.NextBytes(32);

        var wrapped = _wrap.Wrap(derivedKey, masterKey);
        var fp = _fingerprint.Build(derivedKey);

        await _materialStore.SaveAsync(new PasscodeMaterial(salt, wrapped, fp, 100_000, kind), ct).ConfigureAwait(false);
        // Delete the no-passcode DPAPI copy
        await _secrets.RemoveAsync("vng.master_key", ct).ConfigureAwait(false);

        Array.Clear(derivedKey, 0, derivedKey.Length);
        Array.Clear(masterKey, 0, masterKey.Length);

        _bus.Publish(new PasscodeEnabled(kind));
        return Result.Ok<bool, Error>(true);
    }
}
```

### `UnlockUseCase`

```csharp
public sealed class UnlockUseCase
{
    private readonly IPasscodeMaterialStore _materialStore;
    private readonly PasscodeKdfService _kdf;
    private readonly MasterKeyWrapService _wrap;
    private readonly PasscodeFingerprintService _fingerprint;
    private readonly LockState _lockState;
    private readonly PasscodeAttemptRateLimiter _rateLimiter;
    private readonly IEventBus _bus;
    private readonly IClock _clock;

    public async Task<Result<UnlockResult, Error>> ExecuteAsync(string passcode, CancellationToken ct)
    {
        var allowed = _rateLimiter.MayAttempt(_lockState.FailedAttempts, _lockState.LastFailUtc, _clock.UtcNow);
        if (!allowed.IsOk) return Result.Fail<UnlockResult, Error>(allowed.Error);

        var material = await _materialStore.LoadAsync(ct).ConfigureAwait(false);
        if (!material.IsOk) return Result.Fail<UnlockResult, Error>(material.Error);

        var derivedKey = _kdf.Derive(passcode, material.Value.Salt, material.Value.IterationCount, 32);
        var fp = _fingerprint.Build(derivedKey);
        if (!_fingerprint.ConstantTimeEquals(fp, material.Value.Fingerprint))
        {
            _lockState.RecordFailedAttempt(_clock.UtcNow);
            _bus.Publish(new PasscodeFailedAttempt(_lockState.FailedAttempts));
            Array.Clear(derivedKey, 0, derivedKey.Length);
            return Result.Fail<UnlockResult, Error>(PrivacyErrors.WrongPasscode);
        }

        var unwrap = _wrap.Unwrap(derivedKey, material.Value.WrappedMasterKey);
        if (!unwrap.IsOk) return Result.Fail<UnlockResult, Error>(unwrap.Error);

        _lockState.MarkUnlocked(unwrap.Value, _clock.UtcNow);
        Array.Clear(derivedKey, 0, derivedKey.Length);

        _bus.Publish(new PasscodeUnlocked());
        return Result.Ok<UnlockResult, Error>(new UnlockResult(success: true));
    }
}
```

### `AutoLockArmUseCase` / `AutoLockTickUseCase`

```csharp
// Fired in App.OnSuspending or LostFocus
public sealed class AutoLockArmUseCase
{
    public Task<Result<bool, Error>> ExecuteAsync(CancellationToken ct)
    {
        _lockState.ArmAutoLock(_clock.UtcNow);
        return Task.FromResult(Result.Ok<bool, Error>(true));
    }
}

// Fired in App.OnResuming
public sealed class AutoLockTickUseCase
{
    public async Task<Result<bool, Error>> ExecuteAsync(CancellationToken ct)
    {
        if (!_lockState.IsArmed) return Result.Ok<bool, Error>(false);
        var elapsed = _clock.UtcNow - _lockState.ArmedUtc;
        var threshold = _autoLockInterval.AsTimeSpan();
        if (elapsed >= threshold)
        {
            _lockState.MarkLocked();
            _bus.Publish(new PasscodeLocked());
            return Result.Ok<bool, Error>(true);
        }
        return Result.Ok<bool, Error>(false);
    }
}
```

---

## 8. Cross-context

| Outbound | Implemented by | Doc |
|---|---|---|
| `IPrivacyTlGateway` | `Composition.Adapters.TlPrivacyGatewayAdapter` (wraps the sibling `vianium-mtproto` `src\tl\`) | [15-shell-and-host.md](15-shell-and-host.md) |
| `ISecretStore` | Kernel — `Composition.Storage.LocalFolderSecretStore` (wraps `DataProtectionProvider`) | self |
| `IPasscodeMaterialStore` | `DataProtectedPasscodeStore` (its own infra, also via `DataProtectionProvider`) | self |
| `IBiometricGateway` | `WindowsHelloBiometricGateway` placeholder; in V1 it returns `Unsupported` | self |
| `IAppFocusEvents` | `Composition.Lifecycle.AppFocusEventsAdapter` | [15-shell-and-host.md](15-shell-and-host.md) |

Published events consumed:
- `Vianigram.Auth` listens to `PasscodeUnlocked` to hydrate the auth session from the encrypted blob.
- `Vianigram.Messaging`, `Vianigram.SecretChats`, `Vianigram.Stickers`, `Vianigram.Notifications` listen to `PasscodeLocked` to purge in-memory caches.
- `Vianigram.App` listens to `PasscodeLocked` to navigate to `PasscodePage` (the lock screen).
- `Vianigram.App` listens to `PasscodeFailedAttempt` to show the delay countdown.

Events consumed:
- `Vianigram.Settings` publishes `PreferenceChanged<bool>` for `privacy.passcode_enabled` — but it is **not the writer**: the mutation of the flag goes through `EnablePasscodeUseCase`/`DisablePasscodeUseCase`, which updates the setting via an adapter. Directionality: Privacy → Settings (write), not the inverse.

---

## 9. Storage

### `LocalFolder/privacy/passcode_material.bin`

Serialized binary: `[saltLen|salt|wrappedKeyLen|wrappedKey|fingerprintLen|fingerprint|iterations(int)|kind(byte)]`. Encrypted with `DataProtectionProvider` scope `LOCAL=user`. Without the `LOCAL=machine` scope so as not to leak between the device's Windows accounts.

### `LocalSettings`

| Key | Type | Meaning |
|---|---|---|
| `privacy.passcode_enabled` | `bool` | A public mirror (consistent with Settings) |
| `privacy.auto_lock_interval` | `string` | A serialized enum |
| `privacy.biometric_enabled` | `bool` | |
| `privacy.last_unlock_utc` | `long` | Audit; not secret |
| `privacy.failed_attempts` | `int` | The counter for the backoff (reset on a successful unlock) |

### `LocalFolder/privacy/sessions_cache.json`

A cache of the last list of active sessions (not authoritative; always re-fetch in `ActiveSessionsPage`).

### `LocalFolder/privacy/blocked_cache.json`

A cache of blocked users (page 0, max 50 items). Re-fetch in `BlockedUsersPage`.

---

## 10. Rate-limit of failed attempts

`FailedAttemptsBackoff`:

| Attempt | Min wait |
|---|---|
| 1–4 | 0 (no penalty) |
| 5 | 5 seconds |
| 6 | 30 seconds |
| 7 | 5 minutes |
| 8 | 30 minutes |
| 9 | 1 hour |
| 10+ | 1 hour (capped) |

After 50 cumulative failures without a successful unlock, the use case returns `PrivacyErrors.AccountLockoutSuggestReset` and the UI proposes "Forgot passcode? Reset account" — this wipes the local material but does NOT log out of the server (the user should re-login to hydrate and choose a new passcode).

Implementation: persist `failed_attempts` and `last_fail_utc` in LocalSettings. On a successful unlock, reset to 0.

---

## 11. Security — critical invariants

1. **`derivedKey` and `masterKey` live ONLY on the stack/heap during the use case**. After wrap/unwrap, `Array.Clear` to zero. No log, no telemetry with these bytes.
2. **The `fingerprint` does NOT allow reversal to `derivedKey`** — it is an HMAC with the label "verify".
3. **`ConstantTimeEquals`** in the fingerprint comparison (avoids a timing attack to distinguish "first byte correct"). The BCL does not expose one; a custom impl with XOR + accumulate.
4. **`ISecretStore` for the masterKey without a passcode** uses `DataProtectionProvider.ProtectAsync(buffer, "LOCAL=user")`. This binds the wrap to the Windows user account; a device reset ⇒ loses access. Acceptable: the user must re-login in that case.
5. **A PIN is a number** ⇒ low entropy: `Pin4` = 10000 combinations, `Pin6` = 1M. PBKDF2 100k iterations + AES-GCM makes bruteforce expensive but not impossible. Additional mitigation: a local rate limiter + the remote `account.resetAccount` requires an SMS verify.
6. **Do not persist the passcode in plaintext** or in logs. A verifier in code review.
7. **Memory key zero on lock** — `Vianigram.Privacy.Domain.Aggregates.LockState.Lock()` must call `Array.Clear(_memoryKey, 0, _memoryKey.Length)` before setting it to null.
8. **Diagnostic dumps** (Settings → Send debug log) must **redact** everything that touches this context: salt, fingerprint, wrappedKey never appear in the dump.

---

## 12. Privacy rules — composition

Telegram models `account.setPrivacy(key, rules: TlInputPrivacyRule[])` where the list is ordered and the first applicable rule wins. `PrivacyRuleComposer` materializes our discriminated `PrivacyRule` into a TL list:

```csharp
public sealed class PrivacyRuleComposer
{
    public IReadOnlyList<TlInputPrivacyRule> Compose(PrivacyRule rule)
    {
        var list = new List<TlInputPrivacyRule>();
        switch (rule.Kind)
        {
            case PrivacyRuleKind.AllowAll:
                list.Add(TlInputPrivacyRule.AllowAll); break;
            case PrivacyRuleKind.DisallowAll:
                list.Add(TlInputPrivacyRule.DisallowAll); break;
            case PrivacyRuleKind.AllowContacts:
                list.Add(TlInputPrivacyRule.AllowContacts); break;
            // Composite: "Allow contacts but not these specific users"
            case PrivacyRuleKind.AllowContactsExcept:
                list.Add(TlInputPrivacyRule.DisallowUsers(rule.ExceptUserIds));
                list.Add(TlInputPrivacyRule.AllowContacts);
                break;
            // ...
        }
        return list;
    }
}
```

UI Settings → Privacy → "Last seen" has three visible options ("Everyone", "My Contacts", "Nobody") + "+ Exceptions" for a per-user list. They map to these `PrivacyRule` cases.

---

## 13. Performance

| Operation | Target | Notes |
|---|---|---|
| Unlock (PIN4) | < 350 ms | PBKDF2 100k on a 512 MB device ~= 200 ms |
| Unlock (PIN6) | < 350 ms | The same PBKDF2 cost |
| Boot → show the lock screen | < 100 ms | LocalSettings + render |
| Auto-lock arm | < 5 ms | Only set the timestamp |
| Auto-lock tick (resume) | < 10 ms | Compare the ts |
| List active sessions | < 800 ms | A TL roundtrip |
| Block user (TL) | < 600 ms | A TL roundtrip |

If PBKDF2 100k takes > 500 ms on the target device, consider dropping it to 50k — still resistant for a PIN + a local rate limiter.

---

## 14. TL methods consumed

| Method | Use | When |
|---|---|---|
| `account.getPrivacy(key)` | Boot, on Settings → Privacy open | On demand |
| `account.setPrivacy(key, rules)` | A user toggle | User action |
| `contacts.getBlocked` | List blocked users | On demand |
| `contacts.block` / `contacts.unblock` | UI | User action |
| `account.getAuthorizations` | List active sessions | On demand |
| `account.resetAuthorization(hash)` | Terminate one | User action |
| `account.resetWebAuthorizations` | Terminate all web sessions | User action |
| `account.getAccountTTL` / `account.setAccountTTL` | Account auto-delete days | Settings |
| `account.getPassword` / `account.updatePasswordSettings` | Two-step verification (V2) | Out of V1 |

---

## 15. Open questions

1. **WP8.1 fingerprint**: only certain 1 GB-class devices expose a usable API (`Windows.Devices.Sensors.FingerprintReader`?). In V1 we detect it at boot and disable the biometric UI on other devices. V2 can add Windows Hello face on HoloLens-class hardware (irrelevant).
2. **Two-step verification (cloud password)**: `account.getPassword` returns an SRP challenge; `account.updatePasswordSettings` changes it. It is orthogonal to the local passcode. The same context or a separate one? Decision: the same context, a `CloudPasswordSettings` sub-aggregate. V2 prioritizes it.
3. **Account TTL**: trivial, one request — add it to `IPrivacyApi` directly.
4. **Multi-account passcode**: does a single passcode cover all accounts or one per account? V1 decision: a single one (simpler, better UX). The master key wraps the auth blob of ALL the accounts.
5. **Auto-lock when a call is active**: if there is a VOIP call in progress and the app goes to background, do NOT arm auto-lock (the user expects the call to continue). The exception is registered in `AutoLockArmUseCase`.
6. **Reset account flow**: if the user forgets the passcode, we must allow a local wipe + re-login. UI: `PasscodePage` with a "Forgot passcode?" button → `account.resetAccount` (TL), which requires an SMS confirm. Implement it in V1; but coordinate with `Vianigram.Auth`.
7. **The `PasscodeFailedAttempt` publish** may leak info in debug logs (how many of the attacker's attempts). In release, telemetry shows aggregate counters, not per-event.

---

## 16. Crosslinks

- [00-overview.md](00-overview.md)
- [11-settings.md](11-settings.md) — the `privacy.passcode_*` keys.
- [10-notifications.md](10-notifications.md) — `notifications.preview_in_lockscreen` interacts with the lock state.
- [14-presentation.md](14-presentation.md) — `PasscodePage`, `BlockedUsersPage`, `ActiveSessionsPage` are adapters of `IPrivacyApi`.
- [15-shell-and-host.md](15-shell-and-host.md) — the TL gateway adapter + the AppFocusEvents adapter.
