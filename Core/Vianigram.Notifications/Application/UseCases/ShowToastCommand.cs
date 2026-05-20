// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Application.UseCases
{
    /// <summary>
    /// Show a toast notification for an inbound event. The handler honors the
    /// resolved <see cref="MuteRule"/> for <see cref="PeerKey"/> and the
    /// platform notifier's preview policy.
    /// </summary>
    public sealed class ShowToastCommand
    {
        public NotificationKind Kind { get; private set; }
        public string PeerKey { get; private set; }
        public string Title { get; private set; }
        public string Body { get; private set; }
        public string DeepLink { get; private set; }

        public ShowToastCommand(NotificationKind kind, string peerKey, string title, string body, string deepLink)
        {
            Kind = kind;
            PeerKey = peerKey;
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            DeepLink = deepLink ?? string.Empty;
        }
    }
}
