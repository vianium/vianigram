// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Domain.ValueObjects
{
    /// <summary>
    /// The set of privacy "channels" Telegram exposes through
    /// <c>account.getPrivacy#dadbc950</c> / <c>account.setPrivacy#c9f81ce8</c>.
    ///
    /// <para>Each enum value maps to a TL <c>privacyKey*</c> constructor:</para>
    /// <list type="table">
    ///   <listheader><term>Enum</term><description>TL constructor</description></listheader>
    ///   <item><term><see cref="StatusTimestamp"/></term><description><c>privacyKeyStatusTimestamp#4f96cb18</c></description></item>
    ///   <item><term><see cref="ChatInvite"/></term><description><c>privacyKeyChatInvite#500e6dfa</c></description></item>
    ///   <item><term><see cref="PhoneCall"/></term><description><c>privacyKeyPhoneCall#3d662b7b</c></description></item>
    ///   <item><term><see cref="PhoneP2P"/></term><description><c>privacyKeyPhoneP2P#39491cc8</c></description></item>
    ///   <item><term><see cref="Forwards"/></term><description><c>privacyKeyForwards#69ec56a3</c></description></item>
    ///   <item><term><see cref="ProfilePhoto"/></term><description><c>privacyKeyProfilePhoto#96151fed</c></description></item>
    ///   <item><term><see cref="PhoneNumber"/></term><description><c>privacyKeyPhoneNumber#d19ae46d</c></description></item>
    ///   <item><term><see cref="AddedByPhone"/></term><description><c>privacyKeyAddedByPhone#42ffd42b</c></description></item>
    ///   <item><term><see cref="VoiceMessages"/></term><description><c>privacyKeyVoiceMessages#697f414</c></description></item>
    ///   <item><term><see cref="About"/></term><description><c>privacyKeyAbout#a486b761</c></description></item>
    ///   <item><term><see cref="Birthday"/></term><description><c>privacyKeyBirthday#2000a518</c></description></item>
    /// </list>
    /// </summary>
    public enum PrivacyKey
    {
        Unknown = 0,
        StatusTimestamp = 1,
        ChatInvite = 2,
        PhoneCall = 3,
        PhoneP2P = 4,
        Forwards = 5,
        ProfilePhoto = 6,
        PhoneNumber = 7,
        AddedByPhone = 8,
        VoiceMessages = 9,
        About = 10,
        Birthday = 11
    }
}
