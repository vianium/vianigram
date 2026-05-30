// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Vianigram.Calls")]
[assembly: AssemblyDescription("Calls bounded context: signaling and UI state for 1:1 voice/video calls. Drives Telegram phone.* RPCs (requestCall, acceptCall, confirmCall, sendSignalingData, discardCall) and the call-session state machine. The media plane lives in VianiumVoIP behind IVoipMediaPort.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Vianium")]
[assembly: AssemblyProduct("Vianigram")]
[assembly: AssemblyCopyright("Copyright (c) Vianium")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("0.1.2.0")]
[assembly: AssemblyFileVersion("0.1.2.0")]
[assembly: AssemblyInformationalVersion("0.1.2.0")]
[assembly: InternalsVisibleTo("Vianigram.Calls.Tests")]
[assembly: InternalsVisibleTo("Vianigram.SmokeTests")]
