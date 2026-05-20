# Vianigram.Calls — Calls Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), [01-account.md](01-account.md), [03-messages.md](03-messages.md). DDD + hex + managed Kernel + standard C# patterns. Kernel concepts come from `Vianigram.Kernel`.
>
> **Native cross-link:** the media plane (RTP, jitter buffer, packet loss concealment, Opus) lives entirely in the sibling `vianium-voip`. The signaling plane (TL phone.* methods, DH key exchange, peer reachability) is what this managed context owns. Same DDD+hex skeleton, different technology.
>
> **Roadmap position:** Phase 1.7 — depends on Account, Messages (call records are stored as service messages), Contacts (caller identity), and Sync (incoming-call signal arrives on the long-poll). Calls is a smaller context but has hard real-time requirements that no other context faces.

---

## 1. Purpose

`Vianigram.Calls` owns the **signaling and lifecycle** of voice and (Phase 5) video calls between two Telegram users. It mediates the call state machine — from call request → server-relayed offer → answer → key confirmation → active media stream → hang-up — and binds the live media stream into the native VoIP runtime. It does **not** handle audio packetization, jitter, echo cancellation, or Opus encode/decode itself; those concerns live in the sibling `vianium-voip`. It does **not** persist call history as data; call records become *service messages* in the relevant 1:1 dialog and are stored by `Vianigram.Messages`.

The hard line: **only one call may be active per device.** Group calls (voice chats) are a separate concern handled by a future `Vianigram.GroupCalls` context and are explicitly out of scope here.

---

## 2. Aggregate root

`CallSession` — single aggregate per active call. Identity = `CallId(long)` once the server assigns one. Invariants:

- State machine: `Idle → Requested → Waiting → Accepted → Confirmed → Active → Ending → Ended`. No backwards transitions except into `Ended`.
- At most one `CallSession` is non-`Ended` at any time per device — a hard invariant enforced by `CallsApi`.
- The DH key exchange artifacts (`g_a`, `g_b`, `g_a_hash`, shared key fingerprint) are stored as `AuthKeyHandle`-like opaque references into the sibling `vianium-crypto`. Bytes never live in managed code.
- A call carries a `peer: UserId` — Telegram does not currently support 1:1 calls with non-user peers (no calling channels).
- `state` transitions are stamped with `DateTimeOffset` from `IClock`; the difference between `Requested` and `Active` is exposed as `setupDuration` for diagnostics.
- `keyFingerprint` (8 bytes shown to user as 4 emoji or QR) is computed in the crypto context after key exchange completes; it is part of the aggregate's identity-after-Confirmed.
- A call can only be `Active` if the media plane (the sibling `vianium-voip`) reports `MediaPlaneReady`.

There is also a small **`CallHistoryProjection`** read model — *not* an aggregate root — populated from service messages received via Sync. Calls reads it for "call history" UI surfaces; it doesn't own the data.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `CallSession` | aggregate | The live call. |
| `CallRequest` | entity | One in-flight request before the server has assigned a `CallId`. Tombstoned on `requestCall` reply. |
| `CallId` | VO struct | `long`, server-assigned. |
| `CallState` | VO enum | `Idle`, `Requested`, `Waiting`, `Accepted`, `Confirmed`, `Active`, `Ending`, `Ended`. |
| `CallDirection` | VO enum | `Outgoing`, `Incoming`. |
| `CallEndReason` | VO enum | `Hangup`, `Missed`, `Busy`, `Disconnected`, `Discarded`, `AllowGroupCallNoBye`, `ProtocolError`, `KeyMismatch`. |
| `CallProtocol` | VO record | `(udpP2p, udpReflector, minLayer, maxLayer, libraryVersions[])` — capability negotiation. |
| `CallEndpoint` | VO record | `(id, ip, port, transport, peerTag)` — relay reachability. |
| `KeyFingerprint` | VO struct | 8 bytes; rendered as 4-emoji visualization or scannable string. |
| `CallRating` | VO struct | `0..5` star rating + free-form `comment` for `phone.setCallRating`. |
| `MediaPlaneState` | VO enum | `Initializing`, `Probing`, `Connected`, `LossHigh`, `Stalled`, `Closed`. (Mirrored from the sibling `vianium-voip`.) |
| `CallSnapshot` | VO sealed | Outbound projection. |
| `CallHistoryEntry` | VO record | Read-model row from service messages. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `CallRequested` | Outgoing `phone.requestCall` returned | `CallSnapshot` |
| `CallIncoming` | Sync delivered `updatePhoneCall` of kind `phoneCallRequested` | `CallSnapshot`, `UserId fromUser` |
| `CallAccepted` | Local user accepted, `phone.acceptCall` returned | `CallId` |
| `CallConfirmed` | DH exchange completed, `phone.confirmCall` returned with `phoneCall` | `CallId`, `KeyFingerprint`, `IReadOnlyList<CallEndpoint>` |
| `CallActive` | Media plane reported `Connected` | `CallId`, `DateTimeOffset since` |
| `CallMediaStateChanged` | Media plane state transitions during a call | `CallId`, `MediaPlaneState previous`, `MediaPlaneState current` |
| `CallEnded` | Server discard or local hangup applied | `CallId`, `CallEndReason`, `TimeSpan duration` |
| `CallKeyMismatch` | `g_a_hash` did not match `g_a` (security alert) | `CallId` |
| `CallRated` | `phone.setCallRating` succeeded | `CallId`, `CallRating` |

All events extend `DomainEventBase`.

---

## 5. Inbound ports

```csharp
namespace Vianigram.Calls.Ports.Inbound
{
    public interface ICallsApi
    {
        Task<Result<CallSnapshot, Error>> RequestCallAsync(UserId peer, RequestCallOptions options);
        Task<Result<Unit, Error>> AcceptCallAsync(CallId id);
        Task<Result<Unit, Error>> DiscardCallAsync(CallId id, CallEndReason reason);
        Task<Result<Unit, Error>> SetMutedAsync(CallId id, bool muted);
        Task<Result<Unit, Error>> SetSpeakerAsync(CallId id, bool speakerOn);
        Task<Result<Unit, Error>> RateCallAsync(CallId id, CallRating rating);
        Maybe<CallSnapshot> GetActiveCall();
        Task<Result<IReadOnlyList<CallHistoryEntry>, Error>> GetHistoryAsync(int limit, Maybe<MessageId> after);
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }
}
```

`RequestCallOptions` carries `(video: bool, lowBandwidth: bool, preferredLibraryVersion: string)`.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `IVoipMediaPlane` | Bridge to the sibling `vianium-voip`: start RTP/SRTP, push key, mute, switch endpoints, stop. | `Infrastructure/Voip/NativeVoipPlaneAdapter.cs` |
| `ICallCryptoVault` | Bridge to the sibling `vianium-crypto`: DH g^ab, key fingerprint compute. Returns opaque handles. | `Infrastructure/Crypto/CallCryptoAdapter.cs` |
| `IAuthorizedInvoker` | from Account; used for all `phone.*` RPCs. | injected |
| `INetworkProbePort` | UDP NAT traversal probe + reflector reachability test before answering. | `Infrastructure/Net/UdpReachabilityProbe.cs` |
| `IDeviceAudioPort` | Acquire microphone, switch route (earpiece/speaker/Bluetooth). On WP8.1 backed by `Windows.Media.Capture.MediaCapture`. | `Infrastructure/Audio/WinMediaAudioAdapter.cs` |
| `IRingerPort` | Play ringtones, vibrate. | `Infrastructure/Audio/RingerAdapter.cs` |
| `IMessagesObserver` | Read-only view of service messages for call history projection. | injected |
| `IClock`, `ILogger`, `ITelemetry` | Kernel | injected |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Description |
|---|---|---|
| `RequestCallCommand(peer, options)` | `RequestCallUseCase` | Calls `phone.requestCall` with `g_a_hash`; transitions Idle → Requested. |
| `AcceptCallCommand(callId)` | `AcceptCallUseCase` | Calls `phone.acceptCall` with `g_b`; Waiting → Accepted. |
| `ConfirmCallCommand(callId)` | `ConfirmCallUseCase` | Calls `phone.confirmCall` with `g_a` and computed key fingerprint; Accepted → Confirmed. |
| `DiscardCallCommand(callId, reason)` | `DiscardCallUseCase` | Calls `phone.discardCall`; *any* state → Ending → Ended. Idempotent — safe to call twice. |
| `SignalReceivedCommand(rawTl)` | `SignalReceivedUseCase` | Internal — invoked from a Sync subscriber. Dispatches based on `phoneCall.kind`. |
| `MarkReceivedCommand(callId)` | `MarkReceivedUseCase` | Calls `phone.receivedCall` so the server knows the device is alive (UI dismissed lock screen). |
| `SetCallRatingCommand` | `SetCallRatingUseCase` | After `Ended`, optionally call `phone.setCallRating`. |
| `SaveCallDebugCommand` | `SaveCallDebugUseCase` | `phone.saveCallDebug` for diagnostics. Capability-gated. |

**Queries**

| Query | Returns |
|---|---|
| `GetActiveCallQuery` | `Maybe<CallSnapshot>` |
| `GetCallHistoryQuery(limit, after)` | `IReadOnlyList<CallHistoryEntry>` |
| `GetCallProtocolQuery` | `CallProtocol` (negotiation hints) |

**Reactive subscribers**

- `SyncCallReactor` — listens to `Vianigram.Sync.CallSignal` and routes to `SignalReceivedUseCase`.
- `MediaPlaneReactor` — bridges callbacks from the sibling `vianium-voip` (`MediaPlaneStateChanged`) into domain events.
- `LifecycleReactor` — on suspend during active call, must hand off to OS background voice agent or risk drop.
- `AccountReactor` — login/logout triggers cleanup of any orphaned `CallSession`.

---

## 8. Cross-context interactions

**Emits**

- `CallEnded` carries `(callId, reason, duration)`. Consumed by Presentation (UI) and indirectly by Messages (a service message about the call arrives via Sync; we don't double-write history).
- `CallIncoming` → consumed by Presentation (push UI) and `IRingerPort` triggers via the Application's `IncomingCallReactor`.

**Consumes**

- From `Vianigram.Sync`: `CallSignal(updatePhoneCall)` — the only inbound channel for incoming calls and remote state changes (peer accepted, peer discarded). We never poll.
- From `Vianigram.Account`: `LoginCompleted` (warm `phone.getCallConfig` cache), `AccountLoggedOut` (force-end any active call).
- From `Vianigram.Contacts`: `IUserResolver` capability — to render caller name + photo in incoming-call UI.
- From `Vianigram.Messages`: read-only via `IMessagesObserver` to project call history. Not a hot dependency; lazy.
- From Kernel: `BatterySaverChanged`, `NetworkConnectivityChanged` (loss during active call → graceful end via `CallEndReason.Disconnected`).

**Capabilities:** `calls.voice` (always on if microphone capability granted), `calls.video` (off in v1, planned Phase 5), `calls.debug_dump` (off; QA tool).

---

## 9. Storage strategy

**Persisted:** virtually nothing. Calls is fundamentally ephemeral. The only durable artifacts:

- `LocalSettings["calls.{accountId}.last_protocol"]` — cached `CallProtocol` to avoid recomputing on every call request.
- `LocalSettings["calls.{accountId}.preferred_audio_route"]` — last-used audio route (earpiece / speaker / Bluetooth) for UX continuity.

**Memory-only:**

- The active `CallSession` aggregate.
- `CallHistoryProjection` — built on demand from `IMessagesObserver`, not cached aggressively.
- DH artifacts during `Requested → Confirmed`: held by the native crypto vault; managed code keeps only `AuthKeyHandle`-style indices.

**Encryption needs:** key bytes for the call **must never** leave the sibling `vianium-crypto`. The managed `CallSession` only stores the `KeyFingerprint` (a 8-byte digest used for visual confirmation, intentionally non-secret) and an opaque handle to the key. The `IVoipMediaPlane` adapter receives a *pointer to the key in the crypto vault* and the native side fetches it directly — there is no managed → native byte transfer for keys.

**Persistence on logout:** wipe both `LocalSettings` keys. No SQLite tables are owned by Calls.

---

## 10. Performance considerations

- **Setup time target:** ≤ 3 s from "tap call" to first audio frame for a typical user-to-user call where both devices are on Wi-Fi or LTE. Breakdown: ~200 ms `phone.requestCall`, ~500 ms server signaling to peer, ~800 ms peer accept + DH, ~1 s NAT probe + relay selection, ~500 ms slack.
- **Audio quality target:** ≥ 22 kHz Opus, 32 kbps minimum bitrate; adaptive up to 64 kbps when bandwidth allows. Decided in the sibling `vianium-voip`; we just surface the achieved bitrate via `CallMediaStateChanged`.
- **Thread discipline:** signaling RPCs run on the standard Kernel async pump. The media plane runs on a dedicated high-priority thread inside the sibling `vianium-voip` — managed code never blocks waiting for a single packet.
- **NAT traversal:** UDP P2P attempted first; falls back to relay through Telegram-provided reflectors within 1.5 s if probe fails. The probe runs in parallel with key exchange so latency is hidden.
- **Battery:** active calls cost ~12% battery/hour on a 512 MB device. Mitigations: turn off display when held to ear (proximity sensor), stop UI rendering, drop non-essential network activity (pause `messages.*` non-critical RPCs during active call).
- **Memory:** call uses ~3 MB RSS (jitter buffer + Opus state + signaling state). Comfortable on 512 MB devices.
- **Audio underruns:** the jitter buffer in the sibling `vianium-voip` is sized at 60 ms target with 200 ms cap. We expose the underrun count via `CallMediaStateChanged` for telemetry.
- **Ringer latency:** incoming call → ringer audible target ≤ 150 ms from `CallIncoming` event, blocking on neither network nor disk.
- **Cancellation correctness:** `DiscardCallCommand` must be safe to invoke from any state, including racing with a concurrent `phone.discardCall` from the peer side. We coalesce in the use case via a single-flight gate.

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `phone.getCallConfig` | Fetch call protocol parameters (libtgvoip versions, relay endpoints). Cached. |
| `phone.requestCall` | Initiate outgoing call. Sends `g_a_hash`. |
| `phone.acceptCall` | Local user accepts incoming call. Sends `g_b`. |
| `phone.confirmCall` | After server returns the peer's `g_a`, send `g_a` from our side and the computed `key_fingerprint`. |
| `phone.discardCall` | Hang up with reason. Idempotent. |
| `phone.receivedCall` | Acknowledge that the server's signal reached us (for missed-call differentiation). |
| `phone.setCallRating` | Post-call user rating. Optional. |
| `phone.saveCallDebug` | Upload debug logs (capability-gated). |
| `phone.sendSignalingData` | Send signaling/control data over the call channel (Telegram's SDP-equivalent). |

> Note on the inbound side: Sync receives `updatePhoneCall` with embedded `phoneCallRequested`, `phoneCallWaiting`, `phoneCallAccepted`, `phoneCall`, `phoneCallDiscarded`. Each is mapped by Sync's translators into a `CallSignal` event we consume.

---

## 12. Open questions / future work

1. **Video calls.** Deferred to Phase 5. `RequestCallOptions.video` is plumbed end-to-end but the `IVoipMediaPlane` adapter ignores it in v1 (returns `Error.UnsupportedCapability`). The state machine is identical; only the media plane changes.
2. **Group calls / voice chats.** A separate `Vianigram.GroupCalls` context will own group call state, SFU signaling (`groupCall*` TL types), and per-participant streams. They share *no* code with this context beyond the crypto vault primitives.
3. **CallKit-equivalent integration.** WP8.1 has limited "voice agent" background support; we can trigger a foreground notification but cannot fully replicate iOS CallKit. Open: investigate whether `BackgroundTaskBuilder` with `VoipCallTrigger` allows incoming-call ringing while suspended. If not, missed calls in suspended state are an accepted limitation.
4. **Bluetooth audio routing.** Switching to a paired Bluetooth headset mid-call requires platform support that's not entirely consistent across Windows Phone 8.1 devices. We expose `SetSpeakerAsync(speakerOn)` only and route Bluetooth at OS level.
5. **End-to-end key visualization UX.** Today: 4 emojis derived from `KeyFingerprint`. Future: alternative QR-scan flow for device-to-device fingerprint compare. The aggregate already carries the fingerprint; the UX is downstream.
6. **Reference notes from PivoraTelegram.** Pivora has no calls implementation. We are net-new; the design draws on Telegram Desktop's `CallSession` model and the libtgvoip integration patterns from third-party clients.
7. **Telemetry.** `CallsTelemetryEmitter` emits `calls.requests`, `calls.completed`, `calls.failed{reason}`, histograms for `calls.setup_ms` and `calls.duration_s`, gauge `calls.active` (0 or 1), counter `calls.media_plane.underruns`.
