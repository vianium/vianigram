// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Route.cs
//
// Stable enumeration of every page the App can navigate to. Mapped to a
// concrete Page Type by NavigationService.BuildMap.

namespace Vianigram.App.Navigation
{
    public enum Route
    {
        // Core:
        Welcome,
        PhoneNumber,
        SmsCode,
        ChatList,
        Chat,
        LanguagePicker,
        CountryPicker,

        // Auth:
        QrLogin,
        AccountSwitcher,
        Passcode,
        TwoFaPassword,
        SignUp,
        ProxySettings,

        // Profile:
        Profile,
        EditProfile,
        GroupInfo,
        Contacts,
        BlockedUsers,

        // Settings:
        Settings,
        ActiveSessions,
        Scheduled,
        Search,

        // Compose:
        NewChat,
        NewChannel,
        Forward,
        Poll,

        // Media:
        MediaViewer,

        // Calls:
        Calls,
        Call,
        IncomingCall,

        // Secret:
        SecretChat,
        KeyFingerprint,

        // Topics:
        Topics
    }
}
