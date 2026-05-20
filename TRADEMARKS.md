# Trademark and Affiliation Notice

## Telegram and MTProto

"Telegram" and "MTProto" are trademarks of Telegram FZ-LLC. All product
names, logos, and brands associated with the Telegram messenger are the
property of their respective owners.

## Independent, Unofficial Implementation

**Vianigram is an independent, unofficial client implementation of the
publicly-documented [MTProto 2.0 protocol](https://core.telegram.org/mtproto).**

Vianigram is **not affiliated with, endorsed by, or sponsored by Telegram
FZ-LLC**, the Telegram Messenger LLP, or any associated entity. No
endorsement is implied. References to "Telegram" or "MTProto" within
this codebase describe the third-party network protocol that Vianigram
connects to.

The MTProto specification is published by Telegram for third-party
client developers at <https://core.telegram.org/mtproto>. Vianigram
consumes that public protocol to provide a native Windows Phone 8.1
client experience for Telegram users on a platform no longer served by
the official client.

## Clean-room Implementation Policy

Vianigram implements the MTProto 2.0 transport, the TL (Type Language)
serialization, and the call-signaling and voice protocols (`Phone.Call.*`,
`tgcalls`, reflector framing) from publicly-available specifications and
third-party reverse-engineering documentation. The implementation:

- Does **not** copy source code from the official Telegram clients
  (Telegram Desktop, Telegram for Android, Telegram for iOS,
  Telegram Web) and is not derived from them.
- Does **not** copy server-side code, infrastructure, or non-public
  documentation.
- Vendors `libtgvoip` as **third-party** code under its own license
  (LGPL/Unlicense, see `LICENSES/` and `THIRD_PARTY_NOTICES.md` in the
  `vianium-voip` repo). `libtgvoip` is the publicly-published reference
  voice client released by Telegram for embedding by third parties.

## Third-Party Notices

Detailed third-party attribution lives in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)
and in the per-library `LICENSES/` directories of the sibling
`vianium-mtproto`, `vianium-voip`, and `vianium-crypto` repos.

## Contact

For trademark concerns: hello@angelcareaga.com.

Copyright (c) 2026 Angel Careaga. Licensed under PolyForm Noncommercial 1.0.0.
