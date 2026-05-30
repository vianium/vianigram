// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Vianigram.Account")]
[assembly: AssemblyDescription("Account bounded context. Aggregate root AccountIdentity, phone+SMS login, 2FA SRP-2048, multi-account session lifecycle, auth_key persistence, and Telegram MTProto auth orchestration.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Vianium")]
[assembly: AssemblyProduct("Vianigram")]
[assembly: AssemblyCopyright("Copyright (c) Vianium")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("0.1.2.0")]
[assembly: AssemblyFileVersion("0.1.2.0")]
[assembly: InternalsVisibleTo("Vianigram.Account.Tests")]
// AccountLoginMtProtoRpcPort in Vianigram.Composition is the composition-
// layer adapter for Account's IMtProtoRpcPort. It bridges Account's
// internal TL encoders/decoders to the live MTProto channel — that is
// literally what it exists to do. Granting it visibility into our
// internal TL helpers avoids leaking TlEncoder / TlDecoder as public
// API just to satisfy one wiring assembly.
[assembly: InternalsVisibleTo("Vianigram.Composition")]
