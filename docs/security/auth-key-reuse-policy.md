# Auth-Key Reuse Policy

Status: **Adopted 2026-05-26**
Author: Angel Careaga
Implementation: `Core/Vianigram.Composition/Infrastructure/AccountLoginMtProtoRpcPort.cs`

## Context

Vianigram's QR login coordinator must decide, on every cold start of an
anonymous (pre-authenticated) session, whether to **reuse** the auth_key
already persisted in `Vianigram.Storage.SqliteAuthKeyStore` or **regenerate**
a fresh one via the MTProto DH handshake against a Telegram DC.

The previous rule was simple but defective: *"if `userId <= 0`, always
regenerate"*. That made every anonymous QR launch race nine endpoints
through 750 ms staggered starts with 16 s individual deadlines —
worst-case 22 s wall time on networks with dead candidates (port 5222,
unreachable IPv6, ISP-blocked ports). Live emulator logs showed cold
starts wedged at 30–40 s before falling back to the cached key that
was on disk the whole time.

This document fixes the contract.

## Decision

The cache-first policy is **evidence-based**, not category-based.
`AccountLoginMtProtoRpcPort.ShouldForceFreshAuthKey(existing, callerRequestedFresh)`
returns `true` (force regenerate) if any of:

1. **Caller asked for it.** The post-timeout retry path passes
   `callerRequestedFresh: true`. We honour that.
2. **No record cached.** `existing == null`.
3. **Structurally unusable.** Fails `IsUsableAuthKeyRecord(existing)`
   (wrong key length, zero key id, malformed bytes).
4. **Server-rejected this session.** The id is in
   `_serverRejectedAuthKeyIds`, populated by `MarkAuthKeyServerRejected`
   whenever a wire RPC returns `AUTH_KEY_UNREGISTERED`,
   `AUTH_KEY_INVALID`, `AUTH_KEY_DUPLICATED`, or `AUTH_KEY_PERM_EMPTY`.

Otherwise the cached key is **trusted** and reused. The very first
encrypted RPC implicitly verifies it; if the server rejects, the id
lands on the blacklist and the next call falls into branch (4).

## Why this is safe

The natural worry is: *"reusing an anonymous key across cold starts
lets an attacker who once held the key impersonate the user."* In
practice this concern is addressed by these existing invariants:

- **Per-DC isolation.** The persistent store is keyed by DC id. The
  only writers are: (a) a successful login session (which then claims
  the key for that user), and (b) a previous anonymous prewarm that
  generated but never verified. Both are first-party writes by this
  app on this device — there is no inter-app channel that could
  inject an attacker-controlled key into our store.
- **DataProtection at rest.** `Vianigram.Storage` encrypts the
  persistent key with the platform DataProtection API. An attacker
  that reads the SQLite file off the device cannot recover the key
  without also escalating to the device's user crypto context.
- **AUTH_KEY_* propagates immediately.** If the server's view of the
  key ever diverges from ours (clock skew, server-side rotation,
  duplicate-session detection), the first RPC returns an
  `AUTH_KEY_*` error. We then: (i) blacklist the id in memory,
  (ii) delete it from the persistent store, (iii) regenerate.
- **No `userId` binding leaks.** A key generated under
  `userId = 0` (anonymous) cannot be silently promoted to belong to
  a different user. The QR start path that uses the cached key is
  exactly the same path that the anonymous prewarm used to write it;
  any subsequent `loginTokenSuccess` writes the resulting authorised
  key as a *new* record under the new user's `userId`.

The previous "always regenerate" rule did **not** strengthen any of
these invariants — it merely added one more DH handshake per cold
start, which (i) increased the attack surface (MITM windows multiply),
(ii) increased the load on Telegram's auth-key DCs, and (iii) made the
QR page wedge for half a minute on hostile networks.

## What changes operationally

| Phase | Before | After |
|---|---|---|
| QR cold start, valid cache | 25–35 s race → fallback to cache | < 1 s cache load → open |
| QR cold start, no cache | 25–35 s race → open or fail | ≤ 8 s race (wall cap) → open or fast-fail |
| QR cold start, network has 5222 endpoints dead | every cold start wastes the same 3 candidates | port 5222 removed from default plan |
| AUTH_KEY_* server rejection | regenerate; on next launch reuse rejected key, fail again | blacklist + persistent-store delete; next launch regenerates cleanly |

## Out of scope (deferred)

1. **Persistent `auth_key_trust_state`.** Right now the blacklist is
   in-memory. After a process restart we trust the persistent store
   again — if the server still rejects, we re-discover it on the
   first RPC and pay one wasted round trip. A future schema bump can
   add `trust_state INTEGER` (Untrusted / Verified / Stale) and
   `bound_user_id INTEGER` to the `auth_keys` table so the discovery
   carries across launches.
2. **Persistent endpoint health.** Endpoint cooldowns live in a
   static dictionary. A future migration can persist them so dead
   endpoints stay deprioritised across launches.
3. **TTL on anonymous keys.** Currently anonymous keys persist
   forever (subject to the per-DC slot being overwritten by a new
   prewarm). The deferred trust-state schema is the natural place to
   add a "regenerate if `now - created_utc > AnonymousKeyTtl`" rule.

## Related changes

- **Port 5222 removed** from `TelegramDcOptions.AddIpv4`. That port
  is IANA-assigned to XMPP and was never an official MTProto endpoint;
  every candidate against it consumed a full 16 s deadline before
  failing.
- **Auth-key race wall deadline** capped at 8 s
  (`AuthKeyRaceWallDeadline`). Bounds the worst case independently of
  the per-candidate deadline.
- **Hard-failure fast-track** in the race
  (`AuthKeyRaceHardFailureBailout = 3`). After three transport-level
  "no route" errors we bail rather than burning 16 s per remaining
  endpoint.
- **`RefreshLeadSeconds` raised 5 → 8 s** in `QrLoginPageViewModel`
  to give the cushion for refresh-during-handshake scenarios. Cache-
  first reuse makes the typical refresh sub-second; the extra lead
  costs nothing and absorbs intermittent network blips.

## Verification

The change passes when a fresh-install emulator launch produces this
log shape against a network where 149.154.167.51:443 is reachable:

```
[T+0   ms App.QrLogin] OnNavigatedTo
[T+5   ms Account.LoginMTProto] QR anonymous start: deferring to cache-first policy for DC#2
[T+10  ms Account.LoginMTProto] opening DC#2 plan=...:443,...:80,...:443,... pool=1 forceFresh=False
[T+40  ms Storage] auth-key-v2 hit dc=2
[T+50  ms Account.LoginMTProto] auth_key DC#2 cache HIT keyId=0x...
[T+350 ms Account.LoginMTProto] opened DC#2 149.154.167.51:443 ... key_source=cache open_ms=300 total_ms=345
[T+360 ms App.QrLogin] Token rendered
```

— versus the pre-fix shape that wedged at `T+33 000 ms` on the race
before reaching the same cache HIT.
