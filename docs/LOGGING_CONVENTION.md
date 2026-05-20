# Vianigram Logging Convention

## Format

Every log line MUST match:

`[HH:MM:SS.fff Component] message`

- `HH:MM:SS.fff` — UTC wall-clock with milliseconds (24h).
- `Component` — hierarchical, dot-separated (`App`, `Account.SendPhoneCode`,
  `Net.Http`, `MTProto.Handshake`).
- `message` — factual, terse. Include `elapsed=Nms` when measuring duration.

The legacy `[<Level>] <msg>` shape produced by `DebugLogger` BEFORE Wave 1
has been retired. `DebugLogger` is now the sink only — it emits the message
verbatim. The level (`Trace/Debug/Info/Warn/Error/Fatal`) is preserved as the
`LogLevel` argument passed into the sink and is filtered by the sink's
`_minLevel`, but it does NOT appear in the formatted line. Severity is
expressed by the call site choosing `_log.Info` vs `_log.Warn` etc.

## Component Naming

Top-level (cross-cutting):

- `App` — application bootstrap and root frame lifecycle.
- `Composition` — composition root wiring.
- `Net` — network plumbing.
- `Db` — local persistence.
- `Cache` — in-memory caches.

Per bounded context:

- `Account` — phone code, sign-in, SRP, password, session.
- `Chats` — dialog list, chat metadata.
- `Messages` — message send / receive / edit / delete.
- `Sync` — `getDifference`, `updates`, push subscription.
- `Contacts` — contact import / sync.
- `Media` — audio / image / video / sticker decoding.
- `Stickers` — sticker set management.
- `SecretChats` — end-to-end encrypted chats.
- `Calls` — voice / video calls.
- `Notifications` — push notifications.
- `Settings` — user preferences.
- `Search` — full-text search.
- `Privacy` — privacy rules.
- `Storage` — encrypted persistence layer.
- `MTProto` — MTProto transport (handshake, channel, dispatcher).
- `Tl` — TL serialization.
- `Crypto` — SRP, AES-IGE, RSA, KDF.

Sub-component: `<Top>.<UseCase>`. Examples:

- `Account.SendPhoneCode`
- `Account.SignIn`
- `Sync.GetDifference`
- `Messages.Send`
- `MTProto.Handshake`
- `Crypto.Srp`

## Adding Logs

In a class with DI:

```csharp
using Vianigram.Kernel.Logging;

public sealed class SendPhoneCodeHandler
{
    private readonly IComponentLogger _log;

    public SendPhoneCodeHandler(ILoggerFactory loggerFactory)
    {
        _log = loggerFactory.ForComponent("Account.SendPhoneCode");
    }

    public void Handle()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _log.Debug("verifying phone");
        _log.Info("ok", sw.ElapsedMilliseconds);
    }
}
```

Pre-DI (App.OnLaunched, static ctors):

```csharp
EarlyLog.Write("App", "OnLaunched begin");
```

C++ (Core.MTProto / Core.Media):

```c
MTPROTO_DEBUG_LOG("handshake done elapsed=%ums", ms);
MEDIA_DEBUG_LOG("decoded webp w=%d h=%d", width, height);
```

The C++ macros prepend the UTC timestamp and component name automatically;
do NOT bake `[Component]` into the format string yourself.

## Rules

1. Always go through the wrapper. Never call `Debug.WriteLine` from outside
   `Vianigram.Kernel.Logging`.
2. UTC timestamps only — never local time.
3. Hierarchical components with dots — no spaces, no colons.
4. Include `elapsed=Nms` for any operation you time. Prefer
   `_log.Info("verified", sw.ElapsedMilliseconds)` over manual concatenation.
5. Log byte counts, not byte contents. Never log `auth_key`, `new_nonce`,
   DH private exponents, SRP `a`, message plaintext, password, or PII
   (phone number, name, email).
6. Keep messages terse: `code=ok elapsed=320ms` beats `Successfully verified
   the user's authentication code in 320 milliseconds.`
7. One log line per logical event. Avoid noisy start/end pairs when one
   summary with `elapsed=` will do.
8. Choose the level deliberately:
   - `Trace` — extremely verbose; default off.
   - `Debug` — routine diagnostics.
   - `Info` — user-visible milestones, navigation, RPC outcomes.
   - `Warn` — recoverable surprises (fallback paths, retries).
   - `Error` — operation failed; user is affected.
   - `Fatal` — process-level corruption.

## Anti-Patterns

- `Debug.WriteLine("hello")` — no timestamp, no component. Forbidden.
- `Debug.WriteLine("[Account] hello")` — no timestamp. Forbidden.
- `_log.Info("[Account] hello")` when `_log` was already obtained via
  `ForComponent("Account")` — produces `[ts Account] [Account] hello`. The
  factory already tagged the component.
- Logging full TL payloads or auth_key bytes. Hard ban.
- Long sentences. Use key=value snippets.

## Examples

```text
[14:23:45.789 App] OnLaunched begin
[14:23:45.812 Composition] DH handshake started host=149.154.167.40
[14:23:46.001 MTProto] handshake done elapsed=212ms auth_key_id=0x3b...
[14:23:46.450 Account.SendPhoneCode] sent phone=+** code_hash=…
[14:23:46.612 Sync.GetDifference] applied updates=15 elapsed=42ms
[14:23:46.789 Messages.Send] ok msg_id=4523... elapsed=178ms
```
