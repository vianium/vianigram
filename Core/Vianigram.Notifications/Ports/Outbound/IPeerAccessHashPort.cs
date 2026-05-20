// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IPeerAccessHashPort.cs -- Vianigram.Notifications.Ports.Outbound
//
// Outbound port for resolving the access_hash Telegram requires when
// account.updateNotifySettings/account.getNotifySettings address a concrete
// user or channel through inputNotifyPeer -> inputPeerUser/inputPeerChannel.

namespace Vianigram.Notifications.Ports.Outbound
{
    /// <summary>
    /// Resolves access_hash values for notification peer RPCs. Returns 0 when
    /// the peer has not been observed yet; the server will reject that concrete
    /// user/channel request and the caller maps the RPC error normally.
    /// </summary>
    public interface IPeerAccessHashPort
    {
        long GetUserAccessHash(long userId);
        long GetChannelAccessHash(long channelId);
    }
}
