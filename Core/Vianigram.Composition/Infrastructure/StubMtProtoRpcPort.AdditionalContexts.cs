// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// StubMtProtoRpcPort.AdditionalContexts.cs
// Extends the no-DC stub to cover all nine additional bounded contexts plus
// the 18 typed methods on Account / Chats / Messages outbound. Every entry
// returns a "NotConnected" / "Service not available" failure so the App can
// boot to login UI without a live channel.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Vianigram.Kernel.Result;

namespace Vianigram.Composition.Infrastructure
{
    public sealed partial class StubMtProtoRpcPort
    {
        // ---------- Contacts ----------
        Task<Result<byte[], Vianigram.Contacts.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Contacts.CallAsync stubbed (no DC).");
            var err = new Vianigram.Contacts.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Contacts.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Notifications ----------
        Task<Result<byte[], Vianigram.Notifications.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Notifications.CallAsync stubbed (no DC).");
            var err = new Vianigram.Notifications.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Notifications.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Settings ----------
        Task<Result<byte[], Vianigram.Settings.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Settings.CallAsync stubbed (no DC).");
            var err = new Vianigram.Settings.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Settings.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Privacy ----------
        Task<Result<byte[], Vianigram.Privacy.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Privacy.CallAsync stubbed (no DC).");
            var err = new Vianigram.Privacy.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Privacy.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Search ----------
        Task<Result<byte[], Vianigram.Search.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Search.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Search.CallAsync stubbed (no DC).");
            var err = new Vianigram.Search.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Search.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Media (typed MediaError) ----------
        Task<Result<byte[], Vianigram.Media.Domain.MediaError>>
            Vianigram.Media.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] tlRequest, CancellationToken ct)
        {
            _log.Debug("Media.CallAsync stubbed (no DC).");
            var err = Vianigram.Media.Domain.MediaError.NetworkError(NotConnectedMessage);
            return FromResult(Result<byte[], Vianigram.Media.Domain.MediaError>.Fail(err));
        }

        // Buffer overload — same NotConnected stub for the zero-copy media
        // path. Real wiring lives in MtProtoChannelAdapter.
        Task<Result<IBuffer, Vianigram.Media.Domain.MediaError>>
            Vianigram.Media.Ports.Outbound.IMtProtoRpcPort.CallBufferAsync(
                IBuffer requestBuffer, CancellationToken ct)
        {
            _log.Debug("Media.CallBufferAsync stubbed (no DC).");
            var err = Vianigram.Media.Domain.MediaError.NetworkError(NotConnectedMessage);
            return FromResult(Result<IBuffer, Vianigram.Media.Domain.MediaError>.Fail(err));
        }

        // ---------- Stickers ----------
        Task<Result<byte[], Vianigram.Stickers.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Stickers.CallAsync stubbed (no DC).");
            var err = new Vianigram.Stickers.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Stickers.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- SecretChats ----------
        Task<Result<byte[], Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError>>
            Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("SecretChats.CallAsync stubbed (no DC).");
            var err = new Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ---------- Calls ----------
        Task<Result<byte[], Vianigram.Calls.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            _log.Debug("Calls.CallAsync stubbed (no DC).");
            var err = new Vianigram.Calls.Ports.Outbound.MtProtoRpcError
            {
                Kind = "NotConnected",
                Code = -1,
                Message = NotConnectedMessage,
                Parameter = 0
            };
            return FromResult(Result<byte[], Vianigram.Calls.Ports.Outbound.MtProtoRpcError>.Fail(err));
        }

        // ====================== Account typed stubs ======================

        Task<Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthExportLoginTokenAsync(
                int apiId, string apiHash, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthImportLoginTokenAsync(
                byte[] token, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.UsersGetFullUserAsync(
                Vianigram.Account.Ports.Outbound.InputUserSelf self, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AccountUpdateProfileAsync(
                string firstName, string lastName, string about, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<bool, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AccountCheckUsernameAsync(
                string username, CancellationToken ct)
        {
            return FromResult(Result<bool, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthExportAuthorizationAsync(
                int targetDcId, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthImportAuthorizationAsync(
                long id, byte[] bytes, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>
                .Fail(Vianigram.Account.Domain.Errors.AccountError.NetworkError(NotConnectedMessage)));
        }

        // ====================== Chats typed stubs ======================

        Task<Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.MessagesCreateChatAsync(
                string title, IList<long> userIds, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsCreateChannelAsync(
                string title, string description, bool isPublic, string username, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<bool, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsCheckUsernameAsync(
                string username, CancellationToken ct)
        {
            return FromResult(Result<bool, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.LeavePeerAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId peer, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.GetFullPeerAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId peer, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<IList<Vianigram.Chats.Ports.Outbound.RawForumTopic>, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsGetForumTopicsAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId channel, CancellationToken ct)
        {
            return FromResult(Result<IList<Vianigram.Chats.Ports.Outbound.RawForumTopic>, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsCreateForumTopicAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId channel, string title, string iconEmoji, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>
                .Fail(Vianigram.Chats.Domain.ChatError.NetworkError(NotConnectedMessage)));
        }

        // ====================== Messages typed stubs ======================

        Task<Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesForwardMessagesAsync(
                string sourcePeerKey, IList<long> msgIds, IList<string> destPeerKeys, string commentText, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>
                .Fail(Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage)));
        }

        Task<Result<long, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesSendMediaPollAsync(
                string peerKey, Vianigram.Messages.Domain.ValueObjects.PollSpec poll, CancellationToken ct)
        {
            return FromResult(Result<long, Vianigram.Messages.Domain.MessageError>
                .Fail(Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage)));
        }

        Task<Result<long, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesSendScheduledTextAsync(
                string peerKey, string text, DateTime sendAtUtc, CancellationToken ct)
        {
            return FromResult(Result<long, Vianigram.Messages.Domain.MessageError>
                .Fail(Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesGetScheduledHistoryAsync(
                string peerKey, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>
                .Fail(Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesSendScheduledMessagesAsync(
                string peerKey, long messageId, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>
                .Fail(Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage)));
        }

        Task<Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesDeleteScheduledMessagesAsync(
                string peerKey, long messageId, CancellationToken ct)
        {
            return FromResult(Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>
                .Fail(Vianigram.Messages.Domain.MessageError.NetworkFailed(NotConnectedMessage)));
        }
    }
}
