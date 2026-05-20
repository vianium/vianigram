# Vianigram.SmokeTests

WP8.1 class library that hosts the Vianigram **Phase 1 acceptance** smoke
tests. It is consumed by `Vianigram.App` (Phase 2+) — there is no `Main`.

## What it covers

| Suite     | Source                                                      | Type    | Network? |
|-----------|-------------------------------------------------------------|---------|----------|
| `Crypto`  | `Vianium.Crypto.CryptoSelfTest.RunFast()` (sibling `vianium-crypto`) | offline | no       |
| `Tl`      | `Vianium.Tl.TlSelfTest.RunAll()` (sibling `vianium-mtproto\src\tl\`) | offline | no       |
| `MTProto` | `Vianium.Mtproto.MtProtoSelfTest.RunFastStep(...)` (sibling `vianium-mtproto\src\mtproto\`) | offline | no       |
| `MTProto` | `MsgIdReplayTest` (filters MTProto self-tests for msg_id)   | offline | no       |
| `Live`    | `MtProtoHandshakeSmokeTest` against test DC #2              | live    | **yes**  |

The live handshake targets Telegram's public test datacenter #2 at
`149.154.167.40:443`. See <https://core.telegram.org/api/datacenter>. It will
**not pass in offline environments**.

## Invoking from `Vianigram.App`

```csharp
using System.Threading;
using Vianigram.SmokeTests;

var summary = await SmokeTestRunner.RunAllAsync(CancellationToken.None);

foreach (var entry in summary.Entries)
{
    System.Diagnostics.Debug.WriteLine(
        $"[{entry.Suite}] {(entry.Passed ? "PASS" : "FAIL")} " +
        $"{entry.Name} ({(long)entry.Elapsed.TotalMilliseconds} ms) " +
        $"— {entry.Detail}");
}

if (!summary.AllPassed)
{
    // Surface failure to the user / exit code / telemetry.
}
```

`RunAllAsync` never throws; every exception is captured into a failed
`TestEntry`. Failures show up under their suite tag with a `Detail` string.
Very expensive device burn-in checks can surface as `Skipped` entries so they
are visible without failing the interactive smoke run.

## Phase 1 acceptance criteria

The `Live` suite passes when:

1. `MtProtoConnection.ConnectAsync(host, port)` returns a usable connection.
2. `GenerateAuthKeyAsync()` returns `Success == true`.
3. `AuthKeyBytes.Length == 256` (2048-bit DH-derived key).
4. `AuthKeyId != 0` (low 64 bits of `SHA1(auth_key)`).
5. `|ServerTimeOffset| <= 300` seconds (server clock within sane drift).

When all five hold, Phase 1 is provably wired end-to-end against live Telegram
infrastructure.

## Solution wiring

When this project is added to `Vianigram.sln`, MSBuild resolves the WinMD
references through the listed `<ProjectReference>` entries:

- `..\..\Core\Vianigram.Kernel\Vianigram.Kernel.csproj`
- `..\..\..\vianium-crypto\Vianium.Crypto.vcxproj`
- `..\..\..\vianium-mtproto\Vianium.Mtproto.vcxproj` (provides both the
  Tl and MTProto WinMDs from its `src\tl\` and `src\mtproto\`
  subcomponents)

The C++ projects in those sibling repos are being built in parallel; if
their public WinMD ref class APIs differ from the assumed shape
(`CryptoSelfTest.RunFast()`, `TlSelfTest.RunAll()`,
`MtProtoSelfTest.RunFastStep(...)`,
`MtProtoConnection.ConnectAsync` / `GenerateAuthKeyAsync` /
`AuthKeyResult { Success, AuthKeyBytes, AuthKeyId, ServerTimeOffset, ErrorMessage }`),
adjust the shims under `Tests/` accordingly.
