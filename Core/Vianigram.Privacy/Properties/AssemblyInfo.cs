// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Vianigram.Privacy")]
[assembly: AssemblyDescription("Privacy & Lock bounded context: passcode lock, last-seen / call / forwards / pfp privacy rules (account.getPrivacy / account.setPrivacy), active session management (account.getAuthorizations / account.resetAuthorization / auth.resetAuthorizations), with FLOOD_WAIT-aware Result<T,PrivacyError> surface, domain events on every state change, and CLR events for XAML / UI consumers. Blocked-users management is owned by Vianigram.Contacts and consumed via ACL adapters.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Vianium")]
[assembly: AssemblyProduct("Vianigram")]
[assembly: AssemblyCopyright("Copyright (c) Vianium")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]
[assembly: InternalsVisibleTo("Vianigram.Privacy.Tests")]
