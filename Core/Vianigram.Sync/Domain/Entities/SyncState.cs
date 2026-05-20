// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Time;
using Vianigram.Sync.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Sync.Domain.Entities
{
    /// <summary>
    /// Aggregate root for the Sync bounded context.
    ///
    /// Holds the (pts, qts, seq, date) cursor for the common box plus a per-channel
    /// pts map. Folds an <see cref="UpdatesEnvelope"/> into:
    ///   1. Cursor transitions (server-truth wins, principle M5).
    ///   2. Derived domain events for downstream contexts.
    ///   3. Gap signals (common box and per-channel).
    ///
    /// Per principle M6, this aggregate is the EXCLUSIVE owner of these counters.
    /// No other context reads or writes them; the only outward-facing surface is
    /// the IDomainEvent stream returned via <see cref="ApplyResult"/>.
    ///
    /// Pure domain code: synchronous, no awaits, no I/O. Persistence happens at
    /// the application layer via <see cref="Vianigram.Sync.Ports.Outbound.ISyncStateRepository"/>.
    ///
    /// Pts arithmetic:
    ///   - cursor.pts == 0  OR  ptsCount &lt;= 0 →  unconditional advance if pts &gt; cursor.pts
    ///                                              (we're cold-starting, can't validate).
    ///   - pts == cursor.pts + ptsCount         → in-order, advance.
    ///   - pts &lt;= cursor.pts                 → duplicate, drop (no event).
    ///   - pts &gt; cursor.pts + ptsCount        → gap, signal NeedsGetDifference; do NOT
    ///                                              advance the cursor (we'll re-apply
    ///                                              this update via the difference response).
    /// </summary>
    public sealed class SyncState
    {
        private readonly IClock _clock;
        private SyncCursor _common;
        private readonly IDictionary<long, ChannelCursor> _channels;

        public SyncState(IClock clock)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            _clock = clock;
            _common = SyncCursor.Initial();
            _channels = new Dictionary<long, ChannelCursor>();
        }

        public SyncState(IClock clock, SyncCursor initial, IDictionary<long, ChannelCursor> channels)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            if (initial == null) throw new ArgumentNullException("initial");
            _clock = clock;
            _common = initial;
            _channels = channels ?? new Dictionary<long, ChannelCursor>();
        }

        public SyncCursor Common { get { return _common; } }

        public IDictionary<long, ChannelCursor> Channels { get { return _channels; } }

        /// <summary>
        /// Replace the common-box cursor wholesale — used after updates.getState
        /// or differenceFull responses. allowReset:true permits regression
        /// (only legitimate path: server-side full state reseed).
        /// </summary>
        public void Reseed(SyncCursor newCursor)
        {
            if (newCursor == null) throw new ArgumentNullException("newCursor");
            _common = newCursor;
        }

        public void SetChannelCursor(long channelId, int pts)
        {
            if (channelId <= 0) throw new ArgumentOutOfRangeException("channelId");
            _channels[channelId] = new ChannelCursor(channelId, pts, _clock.UtcNow);
        }

        public void RemoveChannel(long channelId)
        {
            _channels.Remove(channelId);
        }

        // -----------------------------------------------------------------
        // Apply path
        // -----------------------------------------------------------------

        public ApplyResult Apply(UpdatesEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException("envelope");

            var emitted = new List<IDomainEvent>(8);
            bool needsGetDiff = false;
            var needsChannelDiff = new List<long>(0);

            UpdatesTooLong tooLong = envelope as UpdatesTooLong;
            if (tooLong != null)
            {
                return new ApplyResult(emitted, false, needsChannelDiff, true);
            }

            UpdatesEnvelopeShortMessage shortMsg = envelope as UpdatesEnvelopeShortMessage;
            if (shortMsg != null)
            {
                ApplyShortMessage(shortMsg, emitted, ref needsGetDiff);
                return new ApplyResult(emitted, needsGetDiff, needsChannelDiff, false);
            }

            UpdatesEnvelopeShortSent shortSent = envelope as UpdatesEnvelopeShortSent;
            if (shortSent != null)
            {
                ApplyShortSent(shortSent, ref needsGetDiff);
                return new ApplyResult(emitted, needsGetDiff, needsChannelDiff, false);
            }

            UpdatesEnvelopeShort shortEnv = envelope as UpdatesEnvelopeShort;
            if (shortEnv != null)
            {
                if (shortEnv.Date > _common.Date)
                {
                    _common = _common.WithDate(shortEnv.Date);
                }
                if (shortEnv.Update != null)
                {
                    ApplyUpdate(shortEnv.Update, emitted, ref needsGetDiff, needsChannelDiff);
                }
                return new ApplyResult(emitted, needsGetDiff, needsChannelDiff, false);
            }

            UpdatesEnvelopeFull full = envelope as UpdatesEnvelopeFull;
            if (full != null)
            {
                ApplyDateSeq(full.Date, full.Seq);
                if (full.Updates != null)
                {
                    for (int i = 0; i < full.Updates.Count; i++)
                    {
                        ApplyUpdate(full.Updates[i], emitted, ref needsGetDiff, needsChannelDiff);
                    }
                }
                return new ApplyResult(emitted, needsGetDiff, needsChannelDiff, false);
            }

            // Unknown envelope kind — treat as no-op so the loop survives.
            return ApplyResult.Empty();
        }

        // -----------------------------------------------------------------
        // Per-update dispatch
        // -----------------------------------------------------------------

        private void ApplyUpdate(Update update, List<IDomainEvent> emitted, ref bool needsGetDiff, List<long> needsChannelDiff)
        {
            if (update == null) return;

            UpdateNewMessage newMsg = update as UpdateNewMessage;
            if (newMsg != null)
            {
                if (TryAdvancePts(newMsg.Pts, newMsg.PtsCount, ref needsGetDiff))
                {
                    emitted.Add(new RemoteMessageReceived(newMsg.Message != null ? newMsg.Message.PeerKey : string.Empty,
                        newMsg.Message, _clock.UtcNow));
                }
                return;
            }

            UpdateNewChannelMessage newChMsg = update as UpdateNewChannelMessage;
            if (newChMsg != null)
            {
                if (TryAdvanceChannelPts(newChMsg.ChannelId, newChMsg.Pts, newChMsg.PtsCount, needsChannelDiff))
                {
                    emitted.Add(new RemoteMessageReceived(newChMsg.Message != null ? newChMsg.Message.PeerKey : PeerKey.ForChannel(newChMsg.ChannelId),
                        newChMsg.Message, _clock.UtcNow));
                }
                return;
            }

            // Edits ride the same pts machinery as new messages so a missed
            // edit triggers getDifference too.
            UpdateEditMessage editMsg = update as UpdateEditMessage;
            if (editMsg != null)
            {
                if (TryAdvancePts(editMsg.Pts, editMsg.PtsCount, ref needsGetDiff))
                {
                    emitted.Add(new RemoteMessageEdited(
                        editMsg.Message != null ? editMsg.Message.PeerKey : string.Empty,
                        editMsg.Message,
                        _clock.UtcNow));
                }
                return;
            }

            UpdateEditChannelMessage editChMsg = update as UpdateEditChannelMessage;
            if (editChMsg != null)
            {
                if (TryAdvanceChannelPts(editChMsg.ChannelId, editChMsg.Pts, editChMsg.PtsCount, needsChannelDiff))
                {
                    emitted.Add(new RemoteMessageEdited(
                        editChMsg.Message != null ? editChMsg.Message.PeerKey : PeerKey.ForChannel(editChMsg.ChannelId),
                        editChMsg.Message,
                        _clock.UtcNow));
                }
                return;
            }

            UpdateMessageId msgId = update as UpdateMessageId;
            if (msgId != null)
            {
                emitted.Add(new RemoteMessageIdAssigned(msgId.LocalId, msgId.RandomId, _clock.UtcNow));
                return;
            }

            UpdateDeleteMessages del = update as UpdateDeleteMessages;
            if (del != null)
            {
                if (TryAdvancePts(del.Pts, del.PtsCount, ref needsGetDiff))
                {
                    // Common-box deletes don't carry a peer (server-side: scoped by user_id, applies wherever).
                    // Downstream subscribers must scope by message id, not peer.
                    emitted.Add(new RemoteMessageDeleted(string.Empty, del.MessageIds, _clock.UtcNow));
                }
                return;
            }

            UpdateDeleteChannelMessages delCh = update as UpdateDeleteChannelMessages;
            if (delCh != null)
            {
                if (TryAdvanceChannelPts(delCh.ChannelId, delCh.Pts, delCh.PtsCount, needsChannelDiff))
                {
                    emitted.Add(new RemoteMessageDeleted(PeerKey.ForChannel(delCh.ChannelId), delCh.MessageIds, _clock.UtcNow));
                }
                return;
            }

            UpdateUserStatus us = update as UpdateUserStatus;
            if (us != null)
            {
                emitted.Add(new RemoteUserStatusChanged(us.UserId, us.Status, us.WasOnline, _clock.UtcNow));
                return;
            }

            UpdateUserTyping ut = update as UpdateUserTyping;
            if (ut != null)
            {
                emitted.Add(new RemoteUserTypingChanged(PeerKey.ForUser(ut.UserId), ut.UserId, ut.Action, _clock.UtcNow));
                return;
            }

            UpdateChatUserTyping cut = update as UpdateChatUserTyping;
            if (cut != null)
            {
                emitted.Add(new RemoteUserTypingChanged(PeerKey.ForChat(cut.ChatId), cut.UserId, cut.Action, _clock.UtcNow));
                return;
            }

            UpdateChannelUserTyping chut = update as UpdateChannelUserTyping;
            if (chut != null)
            {
                emitted.Add(new RemoteUserTypingChanged(PeerKey.ForChannel(chut.ChannelId), chut.UserId, chut.Action, _clock.UtcNow));
                return;
            }

            UpdateReadHistoryInbox rIn = update as UpdateReadHistoryInbox;
            if (rIn != null)
            {
                if (TryAdvancePts(rIn.Pts, rIn.PtsCount, ref needsGetDiff))
                {
                    emitted.Add(new RemoteMessageRead(rIn.PeerKey, rIn.MaxId, byMe: true, timestampUtc: _clock.UtcNow));
                }
                return;
            }

            UpdateReadHistoryOutbox rOut = update as UpdateReadHistoryOutbox;
            if (rOut != null)
            {
                if (TryAdvancePts(rOut.Pts, rOut.PtsCount, ref needsGetDiff))
                {
                    emitted.Add(new RemoteMessageRead(rOut.PeerKey, rOut.MaxId, byMe: false, timestampUtc: _clock.UtcNow));
                }
                return;
            }

            UpdateReadChannelInbox rChIn = update as UpdateReadChannelInbox;
            if (rChIn != null)
            {
                emitted.Add(new RemoteMessageRead(PeerKey.ForChannel(rChIn.ChannelId), rChIn.MaxId, byMe: true, timestampUtc: _clock.UtcNow));
                return;
            }

            UpdateReadChannelOutbox rChOut = update as UpdateReadChannelOutbox;
            if (rChOut != null)
            {
                emitted.Add(new RemoteMessageRead(PeerKey.ForChannel(rChOut.ChannelId), rChOut.MaxId, byMe: false, timestampUtc: _clock.UtcNow));
                return;
            }

            UpdateNotifySettings ns = update as UpdateNotifySettings;
            if (ns != null)
            {
                emitted.Add(new RemoteNotifySettingsChanged(ns.PeerKey, ns.ShowPreviews, ns.Silent, ns.MuteUntil, _clock.UtcNow));
                return;
            }

            // Reactions on a message changed.
            UpdateMessageReactions umr = update as UpdateMessageReactions;
            if (umr != null)
            {
                emitted.Add(new RemoteMessageReactionsChanged(umr.PeerKey, umr.MessageId, _clock.UtcNow));
                return;
            }

            UpdateUserName un = update as UpdateUserName;
            if (un != null)
            {
                emitted.Add(new RemoteUserNameChanged(un.UserId, un.FirstName, un.LastName, un.Username, _clock.UtcNow));
                return;
            }

            UpdateUserPhone uph = update as UpdateUserPhone;
            if (uph != null)
            {
                emitted.Add(new RemoteUserPhoneChanged(uph.UserId, uph.Phone, _clock.UtcNow));
                return;
            }

            UpdateUserPhoto upo = update as UpdateUserPhoto;
            if (upo != null)
            {
                emitted.Add(new RemoteUserPhotoChanged(upo.UserId, upo.Photo, _clock.UtcNow));
                return;
            }

            UpdatePtsChanged ptsCh = update as UpdatePtsChanged;
            if (ptsCh != null)
            {
                // Server explicitly bumped our pts — must getDifference.
                needsGetDiff = true;
                return;
            }

            // updateChannel / updateChannelTooLong arrive without a message
            // attached; they tell us "channel <id> moved — fetch via
            // updates.getChannelDifference". Pushing the channel id
            // into needsChannelDiff makes SyncApplication kick off
            // the recovery RPC on the next pump, which is where the
            // actual messages come from.
            UpdateChannelTouched chTouched = update as UpdateChannelTouched;
            if (chTouched != null && chTouched.ChannelId > 0)
            {
                if (!needsChannelDiff.Contains(chTouched.ChannelId))
                {
                    needsChannelDiff.Add(chTouched.ChannelId);
                }
                return;
            }

            // UpdateConfig, UpdateChatParticipants, UpdateUnsupported: no-op for sync state.
            // (UpdateConfig is observable; downstream logic can subscribe via the future
            // RemoteConfigChanged event. UpdateChatParticipants is also a no-op here —
            // Chats context refetches on demand. Both intentionally produce no event
            // rather than adding undefined contracts.)
        }

        private void ApplyShortMessage(UpdatesEnvelopeShortMessage m, List<IDomainEvent> emitted, ref bool needsGetDiff)
        {
            if (!TryAdvancePts(m.Pts, m.PtsCount, ref needsGetDiff)) return;

            if (m.Date > _common.Date)
            {
                _common = _common.WithDate(m.Date);
            }

            string peerKey;
            if (m.Kind == ShortMessageKind.ChatMessage)
            {
                peerKey = PeerKey.ForChat(m.PeerOrChatId);
            }
            else
            {
                // Private. PeerOrChatId is the *other* user's id (the peer the
                // message is exchanged with). FromUserId is the sender — for an
                // outgoing private msg, FromUserId == self and PeerOrChatId == them.
                peerKey = PeerKey.ForUser(m.PeerOrChatId);
            }

            var dto = new MessageDto(
                id: m.MessageId,
                peerKey: peerKey,
                fromUserId: m.FromUserId,
                date: m.Date,
                message: m.Message,
                replyToMessageId: m.ReplyToMessageId,
                isOutgoing: m.IsOutgoing,
                isMediaUnread: false,
                isSilent: false,
                editDate: 0);

            emitted.Add(new RemoteMessageReceived(peerKey, dto, _clock.UtcNow));
        }

        private void ApplyShortSent(UpdatesEnvelopeShortSent s, ref bool needsGetDiff)
        {
            if (!TryAdvancePts(s.Pts, s.PtsCount, ref needsGetDiff)) return;
            if (s.Date > _common.Date)
            {
                _common = _common.WithDate(s.Date);
            }
            // Note: shortSent has no peer; the Messages context already knows the
            // peer from the in-flight send. We don't emit a derived event here —
            // the matching Update*Message will follow over the wire and produce
            // the canonical RemoteMessageReceived. This keeps single-source-of-truth.
        }

        // -----------------------------------------------------------------
        // pts/qts/seq/date arithmetic — the principle M5/M6 invariant.
        // Returns true if the update should be applied; false if it was a
        // duplicate or gap (in which case the cursor is unchanged and the
        // caller must NOT emit derived events for this update).
        // -----------------------------------------------------------------

        private bool TryAdvancePts(int pts, int ptsCount, ref bool needsGetDiff)
        {
            if (pts <= 0)
            {
                // Update has no pts (e.g. presence, typing) — applied unconditionally.
                return true;
            }

            int cur = _common.Pts;

            if (cur <= 0 || ptsCount <= 0)
            {
                // Cold cursor or unbounded update — accept any forward jump.
                if (pts > cur)
                {
                    _common = _common.WithPts(pts);
                    return true;
                }
                return false;
            }

            if (pts <= cur)
            {
                // Duplicate. Drop silently.
                return false;
            }

            if (pts == cur + ptsCount)
            {
                _common = _common.WithPts(pts);
                return true;
            }

            // pts > cur + ptsCount  OR  pts > cur but not contiguous — gap.
            needsGetDiff = true;
            return false;
        }

        private bool TryAdvanceChannelPts(long channelId, int pts, int ptsCount, IList<long> needsChannelDiff)
        {
            if (channelId <= 0) return false;
            if (pts <= 0) return true;

            ChannelCursor existing;
            int cur = 0;
            if (_channels.TryGetValue(channelId, out existing))
            {
                cur = existing.Pts;
            }

            if (cur <= 0 || ptsCount <= 0)
            {
                if (pts > cur)
                {
                    _channels[channelId] = new ChannelCursor(channelId, pts, _clock.UtcNow);
                    return true;
                }
                return false;
            }

            if (pts <= cur) return false;

            if (pts == cur + ptsCount)
            {
                _channels[channelId] = new ChannelCursor(channelId, pts, _clock.UtcNow);
                return true;
            }

            // Gap — request channel difference for proper catch-up but
            // ALSO emit the current message and force-advance the cursor.
            //
            // Why fail-OPEN: in practice the channel-difference recovery
            // requires the channel's access_hash to issue
            // updates.getChannelDifference. Sync.Domain has no peer
            // cache, so the application layer historically called the
            // RPC with access_hash=0 → server returns CHANNEL_INVALID
            // → cursor never recovers → ALL subsequent messages from
            // this channel get dropped here at "pts != cur + ptsCount".
            //
            // Trade-off: we may miss the few intermediate messages
            // that fell into the gap (Telegram's pts arithmetic is
            // strict), but the user keeps seeing CURRENT activity for
            // the channel — far better than the previous behaviour
            // where one missed message silenced the channel forever.
            if (!needsChannelDiff.Contains(channelId))
            {
                needsChannelDiff.Add(channelId);
            }
            _channels[channelId] = new ChannelCursor(channelId, pts, _clock.UtcNow);
            return true;
        }

        private void ApplyDateSeq(int date, int seq)
        {
            int newSeq = seq > _common.Seq ? seq : _common.Seq;
            int newDate = date > _common.Date ? date : _common.Date;
            if (newSeq != _common.Seq || newDate != _common.Date)
            {
                _common = _common.WithSeqAndDate(newSeq, newDate);
            }
        }
    }
}
