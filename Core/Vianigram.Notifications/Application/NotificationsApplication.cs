// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Notifications.Application.Handlers;
using Vianigram.Notifications.Application.UseCases;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.Entities;
using Vianigram.Notifications.Domain.Events;
using Vianigram.Notifications.Domain.ValueObjects;
using Vianigram.Notifications.Ports.Inbound;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Application
{
    /// <summary>
    /// <see cref="INotificationsApi"/> implementation. Dispatches each public
    /// method to the matching handler, surfaces results as
    /// <c>Result&lt;T, NotificationsError&gt;</c>, and re-broadcasts internal
    /// domain events on the kernel bus into CLR events
    /// (<see cref="Delivered"/>, <see cref="MuteRuleChanged"/>) so XAML / UI
    /// consumers don't need an <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="NotificationsError"/>.
    /// </summary>
    public sealed class NotificationsApplication : INotificationsApi, IDisposable
    {
        private readonly INotificationProfileRepository _repo;
        private readonly UpdateMuteRuleHandler _updateMute;
        private readonly MuteAllHandler _muteAll;
        private readonly MarkAsReadHandler _markRead;
        private readonly SyncSettingsHandler _sync;
        private readonly ShowToastHandler _show;
        private readonly UpdateBadgeHandler _badge;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<NotificationDeliveredEventArgs> Delivered;
        public event EventHandler<MuteRuleChangedEventArgs> MuteRuleChanged;

        public NotificationsApplication(
            IMtProtoRpcPort rpc,
            IPlatformNotifier notifier,
            INotificationProfileRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
            : this(rpc, notifier, repo, bus, logger, clock, null)
        {
        }

        public NotificationsApplication(
            IMtProtoRpcPort rpc,
            IPlatformNotifier notifier,
            INotificationProfileRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IPeerAccessHashPort peerHashes)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (notifier == null) throw new ArgumentNullException("notifier");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _repo = repo;
            _updateMute = new UpdateMuteRuleHandler(repo, rpc, bus, logger, clock, peerHashes);
            _muteAll = new MuteAllHandler(repo, rpc, bus, logger, clock, peerHashes);
            _markRead = new MarkAsReadHandler(repo, notifier, bus, logger, clock);
            _sync = new SyncSettingsHandler(repo, rpc, bus, logger, clock);
            _show = new ShowToastHandler(repo, notifier, bus, logger, clock);
            _badge = new UpdateBadgeHandler(repo, notifier, bus, logger, clock);

            _subs = new IDisposable[]
            {
                bus.Subscribe<NotificationDelivered>(OnDelivered),
                bus.Subscribe<MuteRuleChanged>(OnRuleChanged)
            };
        }

        // ---- INotificationsApi ----------------------------------------------

        public async Task<Result<Unit, NotificationsError>> SetMuteAsync(string peerKey, MuteRule rule, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(peerKey))
                    return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));
                if (rule == null)
                    return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("rule required"));
                return await _updateMute.HandleAsync(new UpdateMuteRuleCommand(peerKey, rule), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("SetMuteAsync failed", ex));
            }
        }

        public async Task<Result<MuteRule, NotificationsError>> GetMuteAsync(string peerKey, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(peerKey))
                    return Result<MuteRule, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));
                NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
                MuteRule resolved = profile.Resolve(peerKey);
                return Result<MuteRule, NotificationsError>.Ok(resolved);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<MuteRule, NotificationsError>.Fail(NotificationsError.Unknown("GetMuteAsync failed", ex));
            }
        }

        public async Task<Result<Unit, NotificationsError>> SyncSettingsAsync(CancellationToken ct)
        {
            try
            {
                return await _sync.HandleAsync(SyncSettingsCommand.Default, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("SyncSettingsAsync failed", ex));
            }
        }

        public async Task<Result<Unit, NotificationsError>> ShowAsync(
            NotificationKind kind,
            string peerKey,
            string title,
            string body,
            CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(peerKey))
                    return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));
                var cmd = new ShowToastCommand(kind, peerKey, title, body, /*deepLink*/ null);
                return await _show.HandleAsync(cmd, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("ShowAsync failed", ex));
            }
        }

        public async Task<Result<Unit, NotificationsError>> UpdateBadgeAsync(BadgeCount count, CancellationToken ct)
        {
            try
            {
                return await _badge.HandleAsync(new UpdateBadgeCommand(count), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("UpdateBadgeAsync failed", ex));
            }
        }

        public async Task<Result<Unit, NotificationsError>> ClearForPeerAsync(string peerKey, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(peerKey))
                    return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));
                NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
                BadgeCount preserved = profile.Badge;
                return await _markRead.HandleAsync(new MarkAsReadCommand(peerKey, preserved), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("ClearForPeerAsync failed", ex));
            }
        }

        /// <summary>
        /// Mute every peer plus global. Exposed beyond the published
        /// <see cref="INotificationsApi"/> for the settings page; presentation
        /// can call this directly via the concrete instance.
        /// </summary>
        public async Task<Result<Unit, NotificationsError>> MuteAllAsync(DateTime? muteUntilUtc, CancellationToken ct)
        {
            try
            {
                return await _muteAll.HandleAsync(new MuteAllCommand(muteUntilUtc), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("MuteAllAsync failed", ex));
            }
        }

        /// <summary>
        /// Mark a peer as read with the new badge total. Exposed for callers
        /// that have already computed the badge (e.g. <c>Vianigram.Sync</c>).
        /// </summary>
        public async Task<Result<Unit, NotificationsError>> MarkAsReadAsync(string peerKey, BadgeCount newBadge, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(peerKey))
                    return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));
                return await _markRead.HandleAsync(new MarkAsReadCommand(peerKey, newBadge), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("MarkAsReadAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge ---------------------------------------

        private void OnDelivered(NotificationDelivered e)
        {
            RaiseDelivered(new NotificationDeliveredEventArgs(e.Kind, e.PeerKey, e.At));
        }

        private void OnRuleChanged(MuteRuleChanged e)
        {
            RaiseRuleChanged(new MuteRuleChangedEventArgs(e.PeerKey, e.NewRule, e.At));
        }

        private void RaiseDelivered(NotificationDeliveredEventArgs args)
        {
            var h = Delivered;
            if (h == null) return;
            try { h(this, args); }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        private void RaiseRuleChanged(MuteRuleChangedEventArgs args)
        {
            var h = MuteRuleChanged;
            if (h == null) return;
            try { h(this, args); }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }
    }
}
