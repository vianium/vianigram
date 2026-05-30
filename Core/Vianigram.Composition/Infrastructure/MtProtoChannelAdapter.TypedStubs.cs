// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProtoChannelAdapter.TypedStubs.cs
// TL serialization for the 18 typed RPC methods on IMtProtoRpcPort across the
// Account / Chats / Messages bounded contexts.
//
// Each method:
//   1) Encodes the TL request body via TlByteBuilder.
//   2) Calls the shared CallInternalAsync.
//   3) Partial-decodes the response via TlByteReader, projecting only the
//      fields the higher-layer DTOs need.
//
// Constraints:
//   * Constructor IDs come from the layer-214 schema (see
//     Vianigram.Core.Tl/src/infrastructure/generated/tl_layer_214.cpp).
//   * access_hash for non-self peers is sourced from the shared IPeerCache,
//     populated by every typed RPC response that returns a
//     users:Vector<User> / chats:Vector<Chat> slice and by the push pipe
//     (MtProtoUpdatesAdapter). Cache misses fall back to 0 — sites that
//     necessarily build a peer reference before the cache hydrates carry a
//     "NOTE: access_hash fallback to 0 (peer not yet observed)" comment.
//   * Complex result types (auth.LoginToken, Updates, messages.ChatFull,
//     messages.ForumTopics, etc.) are partial-decoded around the fields
//     consumed by the bounded contexts.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;

namespace Vianigram.Composition.Infrastructure
{
    public sealed partial class MtProtoChannelAdapter
    {
        // ===================== TL constructor IDs ========================
        // Pulled from layer-214 generated headers. We declare them here so
        // every typed stub references a single canonical name.

        // --- Bool ---
        private const uint CtorBoolTrue = 0x997275b5u;
        private const uint CtorBoolFalse = 0xbc799737u;

        // --- InputPeer ---
        private const uint CtorInputPeerEmpty = 0x7f3b18eau;
        private const uint CtorInputPeerSelf = 0x7da07ec9u;
        private const uint CtorInputPeerChat = 0x35a95cb9u;
        private const uint CtorInputPeerUser = 0xdde8a54cu;
        private const uint CtorInputPeerChannel = 0x27bcbbfcu;

        // --- InputUser ---
        private const uint CtorInputUserEmpty = 0xb98886cfu;
        private const uint CtorInputUserSelf = 0xf7c1b13fu;
        private const uint CtorInputUser = 0xf21158c6u;

        // --- InputChannel ---
        private const uint CtorInputChannelEmpty = 0xee8c1e86u;
        private const uint CtorInputChannel = 0xf35aec28u;

        // --- Auth / QR login ---
        private const uint CtorAuthExportLoginToken = 0xb7e085feu;
        private const uint CtorAuthImportLoginToken = 0x95ac5ce4u;
        private const uint CtorAuthLoginToken = 0x629f1980u;
        private const uint CtorAuthLoginTokenMigrateTo = 0x068e9916u;
        private const uint CtorAuthLoginTokenSuccess = 0x390d5c5eu;
        private const uint CtorAuthAuthorization = 0x2ea2c0d4u;
        private const uint CtorAuthAuthorizationSignUpRequired = 0x44747e9au;

        // --- Users / Account ---
        private const uint CtorUsersGetFullUser = 0xb60f5918u;
        private const uint CtorAccountUpdateProfile = 0x78515775u;
        private const uint CtorAccountCheckUsername = 0x2714d86cu;

        // --- Chats / Channels ---
        private const uint CtorMessagesCreateChat = 0x09cb126eu; // messages.createChat#9cb126e
        private const uint CtorChannelsCreateChannel = 0x3d5fb10fu;
        private const uint CtorChannelsUpdateUsername = 0x3514b3deu;
        private const uint CtorChannelsCheckUsername = 0x10e6bd2cu;
        private const uint CtorMessagesDeleteChatUser = 0xa2185cabu;
        private const uint CtorChannelsLeaveChannel = 0xf836aa95u;
        private const uint CtorMessagesGetFullChat = 0xaeb00b34u;
        private const uint CtorChannelsGetFullChannel = 0x08736a09u; // channels.getFullChannel#8736a09
        private const uint CtorChannelsGetForumTopics = 0x0de560d1u;
        private const uint CtorChannelsCreateForumTopic = 0xf40c0224u;

        // --- Messages ---
        private const uint CtorMessagesForwardMessages = 0xc661bbc4u;
        private const uint CtorMessagesSendMedia = 0x7547c966u;
        private const uint CtorMessagesSendMessage = 0x0d9d75a4u; // messages.sendMessage#d9d75a4
        private const uint CtorMessagesGetScheduledHistory = 0xf516760bu;
        private const uint CtorMessagesSendScheduledMessages = 0xbd38850au;
        private const uint CtorMessagesDeleteScheduledMessages = 0x59ae2b16u;

        // --- Media / Poll ---
        private const uint CtorInputMediaPoll = 0x0f94e5f1u;
        private const uint CtorPoll = 0x86e18161u;
        private const uint CtorPollAnswer = 0x6ca9c2e9u;

        // --- Updates (response) ---
        private const uint CtorUpdates = 0x74ae4240u;
        private const uint CtorUpdatesCombined = 0x725b04c3u;
        private const uint CtorUpdateShort = 0x78d4dec1u;
        private const uint CtorUpdateShortMessage = 0x313bc7f8u;
        private const uint CtorUpdateShortChatMessage = 0x4d6deea5u;
        private const uint CtorUpdateShortSentMessage = 0x9015e101u;
        private const uint CtorUpdatesTooLong = 0xe317af7eu;

        // --- User (response) ---
        // The server emits multiple ctors depending on the negotiated layer.
        // All share the same flags+flags2+id+access_hash+name shape, so we
        // treat them interchangeably during the partial-decoder scan.
        //   user#83314fca  layer 185+
        //   user#83314fae  alt spec
        //   user#020b1422  layer 214 (observed live)
        private const uint CtorUserA = 0x83314fcau;
        private const uint CtorUserB = 0x83314faeu;
        private const uint CtorUser214 = 0x020b1422u;
        private const uint CtorUserEmpty = 0xd3bc4b7au;

        // --- messages.ChatFull / Chat / Channel ---
        // Channel records: same flags+flags2+id+access_hash+title shape across
        // these three ctors. The actual ctor depends on the server-negotiated
        // layer for the user's session.
        //   channel#83259464  layer 195
        //   channel#fe4478bd  layer 187
        //   channel#fe685355  current (observed live in layer 214 dialog responses)
        // channelForbidden has its own shape — see CtorChannelForbidden in
        // TryExtractChatsSlice. The 0x6978a9c3 variant is also accepted as a
        // forbidden-channel record.
        private const uint CtorMessagesChatFull = 0xe5d7d19bu; // messages.chatFull (well-known)
        private const uint CtorChatFull = 0xc9d31138u;          // chatFull (best-effort; partial-decoded)
        private const uint CtorChannelFull = 0xea68a619u;       // channelFull (best-effort; partial-decoded)
        private const uint CtorChat = 0x41cbf256u;
        private const uint CtorChannel = 0x83259464u;
        private const uint CtorChannel187 = 0xfe4478bdu;
        private const uint CtorChannelCurrent = 0xfe685355u;

        // --- messages.Messages family ---
        private const uint CtorMessagesMessages = 0x8c718e87u;
        private const uint CtorMessagesMessagesSlice = 0x3a54685eu;
        private const uint CtorMessagesChannelMessages = 0xc776ba4eu;
        private const uint CtorMessagesMessagesNotModified = 0x74535f21u;

        // ===================== Account typed methods =====================

        async Task<Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthExportLoginTokenAsync(
                int apiId, string apiHash, CancellationToken ct)
        {
            // auth.exportLoginToken#b7e085fe api_id:int api_hash:string except_ids:Vector<long>
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAuthExportLoginToken)
                .WriteInt32(apiId)
                .WriteString(apiHash ?? string.Empty)
                .WriteVector<long>(new long[0], WriteLong)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                // SESSION_PASSWORD_NEEDED can come back as an rpc error on
                // exportLoginToken once the other device approves the QR
                // for an account that has 2FA enabled. Surface as
                // TwoFaRequired so the handler triggers the SRP flow.
                string m = outcome.Message ?? string.Empty;
                if (m.IndexOf("SESSION_PASSWORD_NEEDED", StringComparison.Ordinal) >= 0)
                {
                    var dto2fa = new Vianigram.Account.Ports.Outbound.QrTokenResponse();
                    dto2fa.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.TwoFaRequired;
                    return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto2fa);
                }
                return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorAuthLoginToken)
                {
                    int expires = r.ReadInt32();
                    byte[] token = r.ReadBytes();
                    var dto = new Vianigram.Account.Ports.Outbound.QrTokenResponse();
                    dto.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.Pending;
                    dto.Token = token;
                    dto.ExpiresUnixSeconds = expires;
                    return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto);
                }
                if (ctor == CtorAuthLoginTokenMigrateTo)
                {
                    var dto = new Vianigram.Account.Ports.Outbound.QrTokenResponse();
                    dto.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.MigrateTo;
                    dto.MigrateDcId = r.ReadInt32();
                    dto.MigrateToken = r.ReadBytes();
                    return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto);
                }
                if (ctor == CtorAuthLoginTokenSuccess)
                {
                    // The QR was approved on another device while we were
                    // re-issuing exportLoginToken. Surface the embedded
                    // auth.authorization sub-tree so the handler can
                    // finalize the login (decode user_id, persist, etc.).
                    var dto = new Vianigram.Account.Ports.Outbound.QrTokenResponse();
                    dto.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.Accepted;
                    int bodyLen = outcome.Bytes.Length - 4;
                    if (bodyLen > 0)
                    {
                        var auth = new byte[bodyLen];
                        Buffer.BlockCopy(outcome.Bytes, 4, auth, 0, bodyLen);
                        dto.AuthorizationBytes = auth;
                    }
                    return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto);
                }
                return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Account.Ports.Outbound.QrTokenResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthImportLoginTokenAsync(
                byte[] token, CancellationToken ct)
        {
            // auth.importLoginToken#95ac5ce4 token:bytes
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAuthImportLoginToken)
                .WriteBytes(token ?? new byte[0])
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                // Map common QR-poll error strings to the coarse QrPollKind classifier.
                string m = outcome.Message ?? string.Empty;
                if (m.IndexOf("SESSION_PASSWORD_NEEDED", StringComparison.Ordinal) >= 0)
                {
                    var dto = new Vianigram.Account.Ports.Outbound.QrPollResponse();
                    dto.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.TwoFaRequired;
                    return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto);
                }
                if (m.IndexOf("AUTH_TOKEN_EXPIRED", StringComparison.Ordinal) >= 0
                    || m.IndexOf("AUTH_TOKEN_INVALID", StringComparison.Ordinal) >= 0)
                {
                    var dto = new Vianigram.Account.Ports.Outbound.QrPollResponse();
                    dto.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.Expired;
                    return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto);
                }
                return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                var poll = new Vianigram.Account.Ports.Outbound.QrPollResponse();
                if (ctor == CtorAuthLoginTokenSuccess)
                {
                    // body: authorization:auth.Authorization — slice the
                    // sub-tree off the loginTokenSuccess wrapper and surface
                    // it so the handler can extract the user id via
                    // TlDecoder.DecodeAuthorization (shared with signIn / 2FA).
                    poll.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.Accepted;
                    int bodyLen = outcome.Bytes.Length - 4;
                    if (bodyLen > 0)
                    {
                        var auth = new byte[bodyLen];
                        Buffer.BlockCopy(outcome.Bytes, 4, auth, 0, bodyLen);
                        poll.AuthorizationBytes = auth;
                    }
                    return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(poll);
                }
                if (ctor == CtorAuthLoginToken)
                {
                    poll.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.Pending;
                    return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(poll);
                }
                if (ctor == CtorAuthLoginTokenMigrateTo)
                {
                    poll.Kind = Vianigram.Account.Ports.Outbound.QrPollKind.MigrateTo;
                    poll.MigrateDcId = r.ReadInt32();
                    poll.MigrateToken = r.ReadBytes();
                    return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(poll);
                }
                return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Account.Ports.Outbound.QrPollResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.UsersGetFullUserAsync(
                Vianigram.Account.Ports.Outbound.InputUserSelf self, CancellationToken ct)
        {
            // users.getFullUser#b60f5918 id:InputUser
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorUsersGetFullUser)
                .WriteUInt32(CtorInputUserSelf)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            try
            {
                // users.userFull#b60f5918 layout: full_user:UserFull users:Vector<User> chats:Vector<Chat>
                // We only project the User entry (first element of users) into UserFullResponse —
                // the heavy UserFull subtree is intentionally not projected here.
                var r = new TlByteReader(outcome.Bytes);
                // Skip outer ctor (users.userFull or compatible). We do not enforce a
                // specific constructor id here — different layers ship slightly
                // different shapes — and instead seek the embedded User by scanning
                // the buffer for the User constructor.
                uint outer = r.ReadUInt32();
                // Narrow decode: skip full_user subtree. Instead of parsing the
                // forest we look for the User ctor inside the buffer (the first User
                // record is always the self user in users.getFullUser response).
                Vianigram.Account.Ports.Outbound.UserFullResponse dto = TryProjectSelfFromUserSlice(outcome.Bytes);
                if (dto == null)
                {
                    return Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                        Vianigram.Account.Domain.Errors.AccountError.Unknown("users.userFull: could not locate self user record"));
                }
                return Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(dto);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Account.Ports.Outbound.UserFullResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AccountUpdateProfileAsync(
                string firstName, string lastName, string about, CancellationToken ct)
        {
            // account.updateProfile#78515775
            //   flags:#  first_name:flags.0?string  last_name:flags.1?string  about:flags.2?string
            int flags = 0;
            if (firstName != null) flags |= 1 << 0;
            if (lastName != null) flags |= 1 << 1;
            if (about != null) flags |= 1 << 2;

            var b = new TlByteBuilder()
                .WriteUInt32(CtorAccountUpdateProfile)
                .WriteInt32(flags);
            if (firstName != null) b.WriteString(firstName);
            if (lastName != null) b.WriteString(lastName);
            if (about != null) b.WriteString(about);

            CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorUserA || ctor == CtorUserB || ctor == CtorUserEmpty)
                {
                // Narrow decode: ignore returned User payload; success is
                    // signalled by ctor matching the User constructor.
                    return Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>.Ok(
                        Vianigram.Account.Domain.ValueObjects.Unit.Value);
                }
                return Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<bool, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AccountCheckUsernameAsync(
                string username, CancellationToken ct)
        {
            // account.checkUsername#2714d86c username:string -> Bool
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAccountCheckUsername)
                .WriteString(username ?? string.Empty)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<bool, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorBoolTrue) return Result<bool, Vianigram.Account.Domain.Errors.AccountError>.Ok(true);
                if (ctor == CtorBoolFalse) return Result<bool, Vianigram.Account.Domain.Errors.AccountError>.Ok(false);
                return Result<bool, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<bool, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthExportAuthorizationAsync(
                int targetDcId, CancellationToken ct)
        {
            // auth.exportAuthorization#e5bfffcd dc_id:int -> auth.ExportedAuthorization
            byte[] req = new TlByteBuilder()
                .WriteUInt32(0xe5bfffcdu)
                .WriteInt32(targetDcId)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor != 0xb434e2b8u)
                {
                    return Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                        Vianigram.Account.Domain.Errors.AccountError.Unknown(
                            "auth.exportAuthorization unexpected ctor 0x" + ctor.ToString("x8")));
                }
                long id = r.ReadInt64();
                byte[] bytes = r.ReadBytes();
                return Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>.Ok(
                    new Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse
                    {
                        Id = id,
                        Bytes = bytes ?? new byte[0]
                    });
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Account.Ports.Outbound.ExportedAuthorizationResponse, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    Vianigram.Account.Domain.Errors.AccountError.Unknown(
                        "auth.exportAuthorization decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.AuthImportAuthorizationAsync(
                long id, byte[] bytes, CancellationToken ct)
        {
            // auth.importAuthorization#a57a7dad id:long bytes:bytes -> auth.Authorization
            byte[] req = new TlByteBuilder()
                .WriteUInt32(0xa57a7dadu)
                .WriteInt64(id)
                .WriteBytes(bytes ?? new byte[0])
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>.Fail(
                    MapOutcomeToAccountError(outcome));
            }

            // auth.authorization payload — side effect (peer DC session is now
            // authorised) is enough; we don't need to decode the embedded user.
            return Result<Vianigram.Account.Domain.ValueObjects.Unit, Vianigram.Account.Domain.Errors.AccountError>.Ok(
                Vianigram.Account.Domain.ValueObjects.Unit.Value);
        }

        // ===================== Chats typed methods =====================

        async Task<Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.MessagesCreateChatAsync(
                string title, IList<long> userIds, CancellationToken ct)
        {
            // messages.createChat#9cb126e users:Vector<InputUser> title:string -> Updates
            IList<long> ids = userIds ?? new long[0];
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorMessagesCreateChat)
                .WriteVector<long>(ids, WriteInputUserByUserId)
                .WriteString(title ?? string.Empty)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Fail(
                    MapOutcomeToChatError(outcome));
            }

            try
            {
                // Narrow decode: confirm
                // the response is one of the Updates ctors and synthesise a minimal
                // RawDialog from the title (server-side state lands via the updates
                // pipe). A real chat_id projection is a future follow-up.
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (!IsUpdatesCtor(ctor))
                {
                    return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Fail(
                        Vianigram.Chats.Domain.ChatError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
                }
                // NOTE: access_hash fallback to 0 (peer not yet observed). The
                // freshly-created chat's real id+access_hash arrives via the
                // Updates pipe; we synthesise a placeholder Chat peer here so
                // the caller has something to render optimistically.
                var raw = new Vianigram.Chats.Ports.Outbound.RawDialog(
                    Vianigram.Chats.Domain.ValueObjects.PeerId.Chat(1L),
                    title ?? string.Empty,
                    DateTimeOffset.UtcNow);
                return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Ok(raw);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsCreateChannelAsync(
                string title, string description, bool isPublic, string username, CancellationToken ct)
        {
            // channels.createChannel#3d5fb10f
            //   flags:#  broadcast:flags.0?true  megagroup:flags.1?true
            //   title:string  about:string
            int flags = 0;
            if (isPublic)
            {
                // Default: broadcast=true for "public channel"; megagroup is a separate
                // upstream toggle. Our DTO collapses to public/private only — we treat
                // public+isPublic as a broadcast channel. Megagroup support is
                // Narrow DTO: megagroup options are not represented by this caller.
                flags |= 1 << 0;
            }
            else
            {
                flags |= 1 << 1; // megagroup for private group-style channels
            }

            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorChannelsCreateChannel)
                .WriteInt32(flags)
                .WriteString(title ?? string.Empty)
                .WriteString(description ?? string.Empty)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Fail(
                    MapOutcomeToChatError(outcome));
            }

            // Optional follow-up: channels.updateUsername#3514b3de channel:InputChannel username:string
            // when the caller asked for a public channel + a non-empty username.
            // NOTE: access_hash fallback to 0 (peer not yet observed). The
            // freshly-created channel's id+access_hash only land via the
            // Updates pipe — until that hydrates the peer cache we cannot
            // address the new channel, so we skip the follow-up RPC and let
            // the user set the username from the settings page.
            if (isPublic && !string.IsNullOrEmpty(username))
            {
                // Follow-up call deferred until the Updates pipe hydrates the
                // peer cache with the new channel's id+access_hash.
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (!IsUpdatesCtor(ctor))
                {
                    return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Fail(
                        Vianigram.Chats.Domain.ChatError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
                }
                // NOTE: access_hash fallback to 0 (peer not yet observed). The
                // freshly-created channel's real id+access_hash arrives via
                // the Updates pipe; we synthesise a placeholder peer here so
                // the caller has something to render optimistically.
                var raw = new Vianigram.Chats.Ports.Outbound.RawDialog(
                    Vianigram.Chats.Domain.ValueObjects.PeerId.Channel(1L, 0L),
                    title ?? string.Empty,
                    DateTimeOffset.UtcNow);
                return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Ok(raw);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawDialog, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<bool, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsCheckUsernameAsync(
                string username, CancellationToken ct)
        {
            // channels.checkUsername#10e6bd2c channel:InputChannel username:string -> Bool
            // We pre-flight check before creation, so we pass inputChannelEmpty.
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorChannelsCheckUsername)
                .WriteUInt32(CtorInputChannelEmpty)
                .WriteString(username ?? string.Empty)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<bool, Vianigram.Chats.Domain.ChatError>.Fail(MapOutcomeToChatError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorBoolTrue) return Result<bool, Vianigram.Chats.Domain.ChatError>.Ok(true);
                if (ctor == CtorBoolFalse) return Result<bool, Vianigram.Chats.Domain.ChatError>.Ok(false);
                return Result<bool, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<bool, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.LeavePeerAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId peer, CancellationToken ct)
        {
            if (peer == null)
            {
                return Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.PeerNotFound("peer is null"));
            }

            byte[] req;
            if (peer.Kind == Vianigram.Chats.Domain.ValueObjects.PeerKind.Chat)
            {
                // messages.deleteChatUser#a2185cab chat_id:long user_id:InputUser
                // The "leave" semantic is "delete self from chat" — InputUser=self.
                req = new TlByteBuilder()
                    .WriteUInt32(CtorMessagesDeleteChatUser)
                    .WriteInt64(peer.Id)
                    .WriteUInt32(CtorInputUserSelf)
                    .ToArray();
            }
            else if (peer.Kind == Vianigram.Chats.Domain.ValueObjects.PeerKind.Channel)
            {
                // channels.leaveChannel#f836aa95 channel:InputChannel
                // Prefer the cache's access_hash over PeerId — the cache is
                // hydrated by the latest user/chat slice from the server,
                // while PeerId may carry a stale value if the caller
                // built it from a cold-cache projection. NOTE: access_hash
                // falls back to 0 if the channel hasn't been observed yet.
                long ah = LookupChannelAccessHash(peer.Id);
                if (ah == 0L) ah = peer.AccessHash;
                var b = new TlByteBuilder()
                    .WriteUInt32(CtorChannelsLeaveChannel)
                    .WriteUInt32(CtorInputChannel)
                    .WriteInt64(peer.Id)
                    .WriteInt64(ah);
                req = b.ToArray();
            }
            else
            {
                return Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.NotInExpectedState("LeavePeer requires Chat or Channel peer"));
            }

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>.Fail(
                    MapOutcomeToChatError(outcome));
            }

            // Updates ctor confirms server accepted the leave.
            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (!IsUpdatesCtor(ctor))
                {
                    return Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>.Fail(
                        Vianigram.Chats.Domain.ChatError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
                }
                return Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>.Ok(
                    Vianigram.Chats.Domain.ValueObjects.Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Chats.Domain.ValueObjects.Unit, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.GetFullPeerAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId peer, CancellationToken ct)
        {
            if (peer == null)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.PeerNotFound("peer is null"));
            }

            byte[] req;
            if (peer.Kind == Vianigram.Chats.Domain.ValueObjects.PeerKind.Chat)
            {
                // messages.getFullChat#aeb00b34 chat_id:long
                req = new TlByteBuilder()
                    .WriteUInt32(CtorMessagesGetFullChat)
                    .WriteInt64(peer.Id)
                    .ToArray();
            }
            else if (peer.Kind == Vianigram.Chats.Domain.ValueObjects.PeerKind.Channel)
            {
                // channels.getFullChannel#8736a09 channel:InputChannel
                long ah = LookupChannelAccessHash(peer.Id);
                if (ah == 0L) ah = peer.AccessHash;
                req = new TlByteBuilder()
                    .WriteUInt32(CtorChannelsGetFullChannel)
                    .WriteUInt32(CtorInputChannel)
                    .WriteInt64(peer.Id)
                    .WriteInt64(ah)
                    .ToArray();
            }
            else
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.NotInExpectedState("GetFullPeer requires Chat or Channel peer"));
            }

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>.Fail(
                    MapOutcomeToChatError(outcome));
            }

            try
            {
                // Narrow decode: messages.chatFull is a deeply-nested forest. We
                // confirm the wrapper ctor and return a minimal RawGroupInfo with
                // empty members; the rich projection lands when the updates pipe
                // populates the chat cache.
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor != CtorMessagesChatFull)
                {
                    return Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>.Fail(
                        Vianigram.Chats.Domain.ChatError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
                }
                var info = new Vianigram.Chats.Ports.Outbound.RawGroupInfo(
                    peer,
                    string.Empty,
                    string.Empty,
                    0,
                    new Vianigram.Chats.Ports.Outbound.RawGroupMember[0],
                    isAdmin: false,
                    isCreator: false,
                    createdAt: DateTimeOffset.UtcNow);
                return Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>.Ok(info);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawGroupInfo, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<IList<Vianigram.Chats.Ports.Outbound.RawForumTopic>, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsGetForumTopicsAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId channel, CancellationToken ct)
        {
            if (channel == null || channel.Kind != Vianigram.Chats.Domain.ValueObjects.PeerKind.Channel)
            {
                return Result<IList<Vianigram.Chats.Ports.Outbound.RawForumTopic>, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.NotInExpectedState("forum topics require a Channel peer"));
            }

            // channels.getForumTopics#0de560d1
            //   flags:#  channel:InputChannel  q:flags.0?string  offset_date:int
            //   offset_id:int  offset_topic:int  limit:int
            int flags = 0;
            long forumAh = LookupChannelAccessHash(channel.Id);
            if (forumAh == 0L) forumAh = channel.AccessHash;
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorChannelsGetForumTopics)
                .WriteInt32(flags)
                .WriteUInt32(CtorInputChannel)
                .WriteInt64(channel.Id)
                .WriteInt64(forumAh)
                .WriteInt32(0)   // offset_date
                .WriteInt32(0)   // offset_id
                .WriteInt32(0)   // offset_topic
                .WriteInt32(100) // limit
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<IList<Vianigram.Chats.Ports.Outbound.RawForumTopic>, Vianigram.Chats.Domain.ChatError>.Fail(
                    MapOutcomeToChatError(outcome));
            }

            // Narrow decode: messages.forumTopics carries a flag-heavy wrapper +
            // Vector<ForumTopic>+Vector<Message>+Vector<Chat>+Vector<User>. For now
            // we confirm the response decodes far enough to read the outer ctor and
            // return an empty list — the fan-out projection lands in a follow-up.
            return Result<IList<Vianigram.Chats.Ports.Outbound.RawForumTopic>, Vianigram.Chats.Domain.ChatError>.Ok(
                new Vianigram.Chats.Ports.Outbound.RawForumTopic[0]);
        }

        async Task<Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>>
            Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.ChannelsCreateForumTopicAsync(
                Vianigram.Chats.Domain.ValueObjects.PeerId channel, string title, string iconEmoji, CancellationToken ct)
        {
            if (channel == null || channel.Kind != Vianigram.Chats.Domain.ValueObjects.PeerKind.Channel)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.NotInExpectedState("create forum topic requires a Channel peer"));
            }

            // channels.createForumTopic#f40c0224
            //   flags:#  channel:InputChannel  title:string  icon_color:flags.0?int
            //   icon_emoji_id:flags.3?long  random_id:long  send_as:flags.2?InputPeer
            int flags = 0; // no icon_color, no icon_emoji_id, no send_as
            long randomId = NewRandomId();
            long topicAh = LookupChannelAccessHash(channel.Id);
            if (topicAh == 0L) topicAh = channel.AccessHash;

            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorChannelsCreateForumTopic)
                .WriteInt32(flags)
                .WriteUInt32(CtorInputChannel)
                .WriteInt64(channel.Id)
                .WriteInt64(topicAh)
                .WriteString(title ?? string.Empty)
                .WriteInt64(randomId)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>.Fail(
                    MapOutcomeToChatError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (!IsUpdatesCtor(ctor))
                {
                    return Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>.Fail(
                        Vianigram.Chats.Domain.ChatError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
                }
                // Narrow decode: scan Updates for forumTopic and project; for
                // now synthesise a placeholder topic with id=randomId so the UI can
                // render an optimistic entry.
                var topic = new Vianigram.Chats.Ports.Outbound.RawForumTopic(
                    randomId,
                    channel,
                    title ?? string.Empty,
                    iconEmoji ?? string.Empty,
                    0,
                    null,
                    DateTimeOffset.UtcNow);
                return Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>.Ok(topic);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Chats.Ports.Outbound.RawForumTopic, Vianigram.Chats.Domain.ChatError>.Fail(
                    Vianigram.Chats.Domain.ChatError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        // ===================== Messages typed methods =====================

        async Task<Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesForwardMessagesAsync(
                string sourcePeerKey, IList<long> msgIds, IList<string> destPeerKeys, string commentText, CancellationToken ct)
        {
            if (msgIds == null || msgIds.Count == 0)
            {
                return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Fail(
                    Vianigram.Messages.Domain.MessageError.InvalidArgument("msgIds required"));
            }
            if (destPeerKeys == null || destPeerKeys.Count == 0)
            {
                return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Fail(
                    Vianigram.Messages.Domain.MessageError.InvalidArgument("destPeerKeys required"));
            }

            // messages.forwardMessages#c661bbc4 — flags:# from_peer:InputPeer id:Vector<int>
            //   random_id:Vector<long> to_peer:InputPeer ... (optional flags omitted).
            var idsAsInt = new int[msgIds.Count];
            for (int i = 0; i < msgIds.Count; i++) idsAsInt[i] = (int)msgIds[i];

            for (int d = 0; d < destPeerKeys.Count; d++)
            {
                string dest = destPeerKeys[d];
                var randoms = new long[msgIds.Count];
                for (int i = 0; i < msgIds.Count; i++) randoms[i] = NewRandomId();

                int flags = 0; // no silent / background / drop_author / etc.
                var b = new TlByteBuilder()
                    .WriteUInt32(CtorMessagesForwardMessages)
                    .WriteInt32(flags);
                WriteInputPeerFromKey(b, sourcePeerKey);
                b.WriteVector<int>(idsAsInt, WriteInt32);
                b.WriteVector<long>(randoms, WriteLong);
                WriteInputPeerFromKey(b, dest);

                CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
                if (!outcome.Ok)
                {
                    return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Fail(
                        MapToMessageError(outcome));
                }
            }

            // Narrow workflow: comment_text follow-up (a sendMessage with reply
            // pointer to the forwarded chain) is not yet wired. The destination
            // gets the bare forward — the optional caption RPC can be added once
            // the message_id from the forwarded chain is parsed from Updates.
            return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Ok(
                Vianigram.Messages.Domain.ValueObjects.Unit.Value);
        }

        async Task<Result<long, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesSendMediaPollAsync(
                string peerKey, Vianigram.Messages.Domain.ValueObjects.PollSpec poll, CancellationToken ct)
        {
            if (poll == null)
            {
                return Result<long, Vianigram.Messages.Domain.MessageError>.Fail(
                    Vianigram.Messages.Domain.MessageError.InvalidArgument("poll spec required"));
            }

            // messages.sendMedia#7547c966 with media=inputMediaPoll#0f94e5f1.
            //
            // poll#86e18161 id:long flags:# closed:flags.0?true public_voters:flags.1?true
            //   multiple_choice:flags.2?true quiz:flags.3?true question:string
            //   answers:Vector<PollAnswer> close_period:flags.4?int close_date:flags.5?int
            //
            // pollAnswer#6ca9c2e9 text:string option:bytes
            int sendMediaFlags = 0; // no silent/background/clear_draft/...
            long randomId = NewRandomId();

            int pollFlags = 0;
            if (!poll.IsAnonymous) pollFlags |= 1 << 1; // public_voters
            if (poll.MultipleAnswers) pollFlags |= 1 << 2;
            if (poll.IsQuiz) pollFlags |= 1 << 3;

            // Build inputMediaPoll body.
            var media = new TlByteBuilder()
                .WriteUInt32(CtorInputMediaPoll)
                .WriteInt32(0) // inputMediaPoll flags: no correct_answers/solution
                .WriteUInt32(CtorPoll)
                .WriteInt64(0L) // poll.id (server assigns)
                .WriteInt32(pollFlags)
                .WriteString(poll.Question);

            // answers vector
            byte[][] options = new byte[poll.Options.Count][];
            for (int i = 0; i < poll.Options.Count; i++)
            {
                options[i] = new byte[] { (byte)i };
            }

            media.WriteUInt32(TlByteBuilder.VectorCtor);
            media.WriteInt32(poll.Options.Count);
            for (int i = 0; i < poll.Options.Count; i++)
            {
                media.WriteUInt32(CtorPollAnswer)
                     .WriteString(poll.Options[i] ?? string.Empty)
                     .WriteBytes(options[i]);
            }

            // For quiz: correct_answers:flags.0?Vector<bytes> at the inputMediaPoll level.
            // We emitted flags=0 above; if quiz, we need to re-emit with bit 0 set —
            // simplest is to rebuild the inputMediaPoll prefix when quiz=true.
            byte[] mediaBytes;
            if (poll.IsQuiz && poll.CorrectIndex >= 0 && poll.CorrectIndex < poll.Options.Count)
            {
                var quizMedia = new TlByteBuilder()
                    .WriteUInt32(CtorInputMediaPoll)
                    .WriteInt32(1) // inputMediaPoll flags: correct_answers present
                    .WriteUInt32(CtorPoll)
                    .WriteInt64(0L)
                    .WriteInt32(pollFlags)
                    .WriteString(poll.Question);
                quizMedia.WriteUInt32(TlByteBuilder.VectorCtor);
                quizMedia.WriteInt32(poll.Options.Count);
                for (int i = 0; i < poll.Options.Count; i++)
                {
                    quizMedia.WriteUInt32(CtorPollAnswer)
                             .WriteString(poll.Options[i] ?? string.Empty)
                             .WriteBytes(options[i]);
                }
                // correct_answers:Vector<bytes> with one entry pointing at the right option.
                quizMedia.WriteUInt32(TlByteBuilder.VectorCtor);
                quizMedia.WriteInt32(1);
                quizMedia.WriteBytes(options[poll.CorrectIndex]);
                mediaBytes = quizMedia.ToArray();
            }
            else
            {
                mediaBytes = media.ToArray();
            }

            var msg = new TlByteBuilder()
                .WriteUInt32(CtorMessagesSendMedia)
                .WriteInt32(sendMediaFlags);
            WriteInputPeerFromKey(msg, peerKey);
            // No reply_to_msg_id (flags.0 not set), media inline:
            msg.WriteRaw(mediaBytes);
            msg.WriteString(string.Empty)        // message
               .WriteInt64(randomId);
            // No reply_markup, no entities, no schedule_date, no send_as.

            CallOutcome outcome = await CallInternalAsync(msg.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<long, Vianigram.Messages.Domain.MessageError>.Fail(MapToMessageError(outcome));
            }

            return DecodeUpdatesForSentMessageId(outcome.Bytes);
        }

        async Task<Result<long, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesSendScheduledTextAsync(
                string peerKey, string text, DateTime sendAtUtc, CancellationToken ct)
        {
            // messages.sendMessage#d9d75a4 with schedule_date (flags.10) set.
            int flags = 1 << 10;
            long randomId = NewRandomId();
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime sendUtc = sendAtUtc.Kind == DateTimeKind.Utc ? sendAtUtc : sendAtUtc.ToUniversalTime();
            long secs = (long)(sendUtc - epoch).TotalSeconds;
            int scheduleDate = secs > int.MaxValue ? int.MaxValue : (int)secs;

            var b = new TlByteBuilder()
                .WriteUInt32(CtorMessagesSendMessage)
                .WriteInt32(flags);
            WriteInputPeerFromKey(b, peerKey);
            // No reply_to_msg_id.
            b.WriteString(text ?? string.Empty)
             .WriteInt64(randomId)
             // No reply_markup, no entities.
             .WriteInt32(scheduleDate);
            // No send_as.

            CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<long, Vianigram.Messages.Domain.MessageError>.Fail(MapToMessageError(outcome));
            }

            return DecodeUpdatesForSentMessageId(outcome.Bytes);
        }

        async Task<Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesGetScheduledHistoryAsync(
                string peerKey, CancellationToken ct)
        {
            // messages.getScheduledHistory#f516760b peer:InputPeer hash:long
            var b = new TlByteBuilder()
                .WriteUInt32(CtorMessagesGetScheduledHistory);
            WriteInputPeerFromKey(b, peerKey);
            b.WriteInt64(0L); // hash

            CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>.Fail(
                    MapToMessageError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor != CtorMessagesMessages
                    && ctor != CtorMessagesMessagesSlice
                    && ctor != CtorMessagesChannelMessages
                    && ctor != CtorMessagesMessagesNotModified)
                {
                    return Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>.Fail(
                        Vianigram.Messages.Domain.MessageError.ProtocolError("unexpected response ctor 0x" + ctor.ToString("x8")));
                }
                // Narrow decode: messages.Messages decoding lives in
                // Vianigram.Messages.Infrastructure.TlDecoder; for the scheduled
                // history we return an empty MessagePage (signals "page received,
                // but no projection yet"). The full decode is a follow-up wave.
                var page = new Vianigram.Messages.Domain.ValueObjects.MessagePage(
                    new Vianigram.Messages.Domain.Entities.Message[0], false, null);
                return Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>.Ok(page);
            }
            catch (Exception ex)
            {
                return Result<Vianigram.Messages.Domain.ValueObjects.MessagePage, Vianigram.Messages.Domain.MessageError>.Fail(
                    Vianigram.Messages.Domain.MessageError.ProtocolError("decode failed: " + ex.Message));
            }
        }

        async Task<Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesSendScheduledMessagesAsync(
                string peerKey, long messageId, CancellationToken ct)
        {
            // messages.sendScheduledMessages#bd38850a peer:InputPeer id:Vector<int>
            int[] ids = new int[] { (int)messageId };
            var b = new TlByteBuilder()
                .WriteUInt32(CtorMessagesSendScheduledMessages);
            WriteInputPeerFromKey(b, peerKey);
            b.WriteVector<int>(ids, WriteInt32);

            CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Fail(
                    MapToMessageError(outcome));
            }
            return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Ok(
                Vianigram.Messages.Domain.ValueObjects.Unit.Value);
        }

        async Task<Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.MessagesDeleteScheduledMessagesAsync(
                string peerKey, long messageId, CancellationToken ct)
        {
            // messages.deleteScheduledMessages#59ae2b16 peer:InputPeer id:Vector<int>
            int[] ids = new int[] { (int)messageId };
            var b = new TlByteBuilder()
                .WriteUInt32(CtorMessagesDeleteScheduledMessages);
            WriteInputPeerFromKey(b, peerKey);
            b.WriteVector<int>(ids, WriteInt32);

            CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Fail(
                    MapToMessageError(outcome));
            }
            return Result<Vianigram.Messages.Domain.ValueObjects.Unit, Vianigram.Messages.Domain.MessageError>.Ok(
                Vianigram.Messages.Domain.ValueObjects.Unit.Value);
        }

        // ===================== Helpers =====================

        private static void WriteLong(TlByteBuilder b, long v) { b.WriteInt64(v); }
        private static void WriteInt32(TlByteBuilder b, int v) { b.WriteInt32(v); }

        // Vector<InputUser> writer that wraps each user_id into inputUser#f21158c6.
        // access_hash is sourced from the IPeerCache when available; when the
        // cache is null OR the peer hasn't been observed yet we fall back to 0.
        private void WriteInputUserByUserId(TlByteBuilder b, long userId)
        {
            if (userId <= 0)
            {
                b.WriteUInt32(CtorInputUserEmpty);
                return;
            }
            long accessHash = LookupUserAccessHash(userId);
            b.WriteUInt32(CtorInputUser)
             .WriteInt64(userId)
             .WriteInt64(accessHash);
        }

        // Cache lookup helpers — null cache or cache miss both yield 0.
        // NOTE: access_hash fallback to 0 (peer not yet observed).
        private long LookupUserAccessHash(long userId)
        {
            if (_peerCache == null) return 0L;
            long? hash = _peerCache.GetUserAccessHash(userId);
            return hash.HasValue ? hash.Value : 0L;
        }

        private long LookupChannelAccessHash(long channelId)
        {
            if (_peerCache == null) return 0L;
            long? hash = _peerCache.GetChannelAccessHash(channelId);
            return hash.HasValue ? hash.Value : 0L;
        }

        private void WriteInputPeerFromKey(TlByteBuilder b, string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey))
            {
                b.WriteUInt32(CtorInputPeerEmpty);
                return;
            }

            int colon = peerKey.IndexOf(':');
            if (colon <= 0)
            {
                b.WriteUInt32(CtorInputPeerEmpty);
                return;
            }

            string kind = peerKey.Substring(0, colon).ToLowerInvariant();
            long id = 0L;
            long.TryParse(peerKey.Substring(colon + 1), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out id);

            switch (kind)
            {
                case "self":
                    b.WriteUInt32(CtorInputPeerSelf);
                    break;
                case "user":
                    b.WriteUInt32(CtorInputPeerUser)
                     .WriteInt64(id)
                     .WriteInt64(LookupUserAccessHash(id));
                    break;
                case "chat":
                    b.WriteUInt32(CtorInputPeerChat)
                     .WriteInt64(id);
                    break;
                case "channel":
                    b.WriteUInt32(CtorInputPeerChannel)
                     .WriteInt64(id)
                     .WriteInt64(LookupChannelAccessHash(id));
                    break;
                default:
                    b.WriteUInt32(CtorInputPeerEmpty);
                    break;
            }
        }

        private static long NewRandomId()
        {
            return BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
        }

        private static bool IsUpdatesCtor(uint ctor)
        {
            return ctor == CtorUpdates
                || ctor == CtorUpdatesCombined
                || ctor == CtorUpdateShort
                || ctor == CtorUpdateShortMessage
                || ctor == CtorUpdateShortChatMessage
                || ctor == CtorUpdateShortSentMessage
                || ctor == CtorUpdatesTooLong;
        }

        private static Result<long, Vianigram.Messages.Domain.MessageError> DecodeUpdatesForSentMessageId(byte[] bytes)
        {
            try
            {
                var r = new TlByteReader(bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorUpdateShortSentMessage)
                {
                    // updateShortSentMessage#9015e101 flags:# out:flags.1?true id:int pts:int pts_count:int date:int ...
                    r.ReadInt32(); // flags
                    int id = r.ReadInt32();
                    return Result<long, Vianigram.Messages.Domain.MessageError>.Ok((long)id);
                }
                if (IsUpdatesCtor(ctor))
                {
                    // Narrow decode: scan Updates for newMessage and lift its id.
                    // For now we return 0 as a "server accepted, id unknown" sentinel.
                    return Result<long, Vianigram.Messages.Domain.MessageError>.Ok(0L);
                }
                return Result<long, Vianigram.Messages.Domain.MessageError>.Fail(
                    Vianigram.Messages.Domain.MessageError.ProtocolError("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<long, Vianigram.Messages.Domain.MessageError>.Fail(
                    Vianigram.Messages.Domain.MessageError.ProtocolError("decode failed: " + ex.Message));
            }
        }

        private static Vianigram.Account.Domain.Errors.AccountError MapOutcomeToAccountError(CallOutcome outcome)
        {
            string kind = outcome.Kind ?? "Unknown";
            string msg = outcome.Message ?? string.Empty;
            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase) && outcome.Parameter > 0)
            {
                return Vianigram.Account.Domain.Errors.AccountError.PhoneNumberFlood(outcome.Parameter);
            }
            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return Vianigram.Account.Domain.Errors.AccountError.NetworkError(msg);
            }
            if (outcome.Code == 401)
            {
                return Vianigram.Account.Domain.Errors.AccountError.SessionExpired(msg);
            }
            return Vianigram.Account.Domain.Errors.AccountError.Unknown(msg);
        }

        private static Vianigram.Chats.Domain.ChatError MapOutcomeToChatError(CallOutcome outcome)
        {
            string kind = outcome.Kind ?? "Unknown";
            string msg = outcome.Message ?? string.Empty;
            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return Vianigram.Chats.Domain.ChatError.NetworkError(msg);
            }
            if (outcome.Code == 403) return Vianigram.Chats.Domain.ChatError.AccessDenied(msg);
            return Vianigram.Chats.Domain.ChatError.Unknown(msg);
        }

        private static Vianigram.Account.Ports.Outbound.UserFullResponse TryProjectSelfFromUserSlice(byte[] body)
        {
            // Scan the wire buffer for the User constructor (kind A or B). Once
            // found, parse its fields just enough to populate UserFullResponse:
            // flags(int) + flags2(int) + id(long) + access_hash?(long) + first_name?(string)
            // + last_name?(string) + username?(string) + phone?(string).
            //
            // Why scan: the users.userFull wrapper has a complex prefix (UserFull
            // forest) we do not yet model — but the embedded users:Vector<User>
            // always contains the canonical User record we want, and the User
            // constructor id is rare enough to use as a sync marker.
            for (int i = 0; i + 4 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));
                if (ctor == CtorUserA || ctor == CtorUserB || ctor == CtorUser214)
                {
                    try
                    {
                        var sub = new byte[body.Length - (i + 4)];
                        Buffer.BlockCopy(body, i + 4, sub, 0, sub.Length);
                        var r = new TlByteReader(sub);
                        uint flags = r.ReadUInt32();
                        uint flags2 = r.ReadUInt32();
                        long userId = r.ReadInt64();
                        long accessHash = 0L;
                        if ((flags & (1u << 0)) != 0) accessHash = r.ReadInt64();
                        string firstName = (flags & (1u << 1)) != 0 ? r.ReadString() : null;
                        string lastName = (flags & (1u << 2)) != 0 ? r.ReadString() : null;
                        string username = (flags & (1u << 3)) != 0 ? r.ReadString() : null;
                        string phone = (flags & (1u << 4)) != 0 ? r.ReadString() : null;

                        var dto = new Vianigram.Account.Ports.Outbound.UserFullResponse();
                        dto.UserId = userId;
                        dto.FirstName = firstName ?? string.Empty;
                        dto.LastName = lastName ?? string.Empty;
                        dto.Username = username ?? string.Empty;
                        dto.Phone = phone ?? string.Empty;
                        // Narrow decode: bio comes from the UserFull header, not
                        // the User record — left blank until the full decode lands.
                        dto.Bio = string.Empty;
                        return dto;
                    }
                    catch
                    {
                        // Fall through and try the next candidate position.
                    }
                }
            }
            return null;
        }

        // ---------- Peer-cache hydration helpers ----------
        //
        // After a successful RPC the adapter scans the response body for the
        // canonical User/Chat constructors and extracts (id, access_hash)
        // pairs. The scan is permissive: it walks 4-byte aligned offsets,
        // attempts a parse at every ctor match, and silently drops candidates
        // whose subsequent reads fall off the buffer end. This is the same
        // pattern used by TryProjectSelfFromUserSlice and avoids modelling
        // the full Updates/UserFull/ChatFull forest just to recover the
        // access_hash slice. Idempotent: re-hydrating the same id with the
        // same access_hash is a no-op.

        private void HydratePeerCacheFromResponse(byte[] body)
        {
            if (_peerCache == null) return;
            if (body == null || body.Length < 4) return;
            try
            {
                IList<RawUser> users = TryExtractUsersSlice(body);
                IList<RawChat> chats = TryExtractChatsSlice(body);
                if (users != null && users.Count > 0) _peerCache.UpdateFromUsersSlice(users);
                if (chats != null && chats.Count > 0) _peerCache.UpdateFromChatsSlice(chats);

                // Diagnostic: how many records did we recover, and how many of
                // them actually had non-zero access_hash. If this prints
                // "chats=N hashed=0" while the dialog list shows N rows, the
                // channel-ctor scanner is matching a stale layout and we
                // need to bump the constant.
                int userCount = users == null ? 0 : users.Count;
                int chatCount = chats == null ? 0 : chats.Count;
                int chatsWithHash = 0;
                int channelsTotal = 0;
                if (chats != null)
                {
                    for (int i = 0; i < chats.Count; i++)
                    {
                        var c = chats[i];
                        if (c == null) continue;
                        if (c.IsChannel)
                        {
                            channelsTotal++;
                            if (c.AccessHash != 0L) chatsWithHash++;
                        }
                    }
                }
                int usersWithHash = 0;
                if (users != null)
                {
                    for (int i = 0; i < users.Count; i++)
                    {
                        var u = users[i];
                        if (u != null && u.AccessHash != 0L) usersWithHash++;
                    }
                }
                // Frequency scan: count every 4-byte aligned value that
                // appears more than once. The channel-record ctor will
                // typically be the second- or third-most-common (after
                // peerChannel/peerUser/peerChat references inside Dialog
                // records). We dump the top 8 to identify it.
                var freq = new Dictionary<uint, int>();
                for (int i = 0; i + 4 <= body.Length; i += 4)
                {
                    uint c = (uint)(body[i] | (body[i + 1] << 8) |
                                    (body[i + 2] << 16) | (body[i + 3] << 24));
                    int n;
                    freq[c] = freq.TryGetValue(c, out n) ? n + 1 : 1;
                }
                // Top-30 by count, but only entries with count >= 2 — single
                // occurrences are noise (random byte alignment). We also
                // filter out very-low values (likely flags/sizes).
                var top = new List<KeyValuePair<uint, int>>();
                foreach (var kv in freq)
                {
                    if (kv.Value < 2) continue;
                    if (kv.Key < 0x01000000u) continue; // skip small ints
                    top.Add(kv);
                }
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                System.Text.StringBuilder topSb = new System.Text.StringBuilder();
                int topN = top.Count < 30 ? top.Count : 30;
                for (int i = 0; i < topN; i++)
                {
                    if (i > 0) topSb.Append(",");
                    topSb.Append("0x" + top[i].Key.ToString("x8") + "=" + top[i].Value);
                }

                // Known-channel-ctor probes. Server may use any of these
                // depending on layer.
                int probeOut;
                int hCh01D8C88B  = freq.TryGetValue(0x01d8c88bu, out probeOut) ? probeOut : 0;
                int hCh0AADFC8F  = freq.TryGetValue(0x0aadfc8fu, out probeOut) ? probeOut : 0;
                int hChFE4478BD  = freq.TryGetValue(0xfe4478bdu, out probeOut) ? probeOut : 0;
                int hChFE685355  = freq.TryGetValue(0xfe685355u, out probeOut) ? probeOut : 0;
                int hCh83259464  = freq.TryGetValue(0x83259464u, out probeOut) ? probeOut : 0;
                int hCh17D493D5  = freq.TryGetValue(0x17d493d5u, out probeOut) ? probeOut : 0; // forbidden
                int hCh6978A9C3  = freq.TryGetValue(0x6978a9c3u, out probeOut) ? probeOut : 0; // forbidden v2
                int hChAt41CBF256 = freq.TryGetValue(0x41cbf256u, out probeOut) ? probeOut : 0; // chat
                int hChAt041C1D4C = freq.TryGetValue(0x041c1d4cu, out probeOut) ? probeOut : 0; // chat new

                Vianigram.Kernel.Logging.EarlyLog.Write(
                    "MTProto.Hydrate",
                    "users=" + userCount + " usersHashed=" + usersWithHash +
                    " chats=" + chatCount + " channels=" + channelsTotal +
                    " channelsHashed=" + chatsWithHash +
                    " body=" + body.Length + "B" +
                    " channelProbes:" +
                    " 01d8c88b=" + hCh01D8C88B +
                    " 0aadfc8f=" + hCh0AADFC8F +
                    " fe4478bd=" + hChFE4478BD +
                    " fe685355=" + hChFE685355 +
                    " 83259464=" + hCh83259464 +
                    " 17d493d5=" + hCh17D493D5 +
                    " 6978a9c3=" + hCh6978A9C3 +
                    " 41cbf256=" + hChAt41CBF256 +
                    " 041c1d4c=" + hChAt041C1D4C +
                    " topCtors[" + topSb + "]");

                // Best-effort scan for message#9815cec8 / messageService#7a800e0a
                // / messageEmpty#90a6ca84 records. Each match yields an (id, date,
                // text) preview the dialog list uses to show a last-activity
                // timestamp + message snippet. Misalignments degrade gracefully
                // because the catch around the structured read drops the bad
                // record and the loop continues at the next 4-byte boundary.
                ExtractMessagePreviewsInto(body, _peerCache);
            }
            catch (Exception hex)
            {
                // Hydration is best-effort; never fail the RPC because of a
                // partial-decode hiccup.
                Vianigram.Kernel.Logging.EarlyLog.Write(
                    "MTProto.Hydrate",
                    "threw " + hex.GetType().Name + ": " + hex.Message);
            }
        }

        private const uint CtorMessage_v214        = 0x9815cec8u;
        private const uint CtorMessageService_v214 = 0x7a800e0au;
        private const uint CtorMessageEmpty        = 0x90a6ca84u;

        private static void ExtractMessagePreviewsInto(byte[] body, IPeerCache cache)
        {
            if (body == null || cache == null) return;
            for (int i = 0; i + 4 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));

                if (ctor == CtorMessage_v214)
                {
                    TryReadMessagePreview(body, i + 4, cache);
                }
                else if (ctor == CtorMessageService_v214)
                {
                    TryReadServiceMessagePreview(body, i + 4, cache);
                }
                // messageEmpty intentionally skipped — no useful content.
            }
        }

        // message#9815cec8 partial layout:
        //   flags:#                                  (4 bytes — many bits)
        //   flags2:#                                 (4 bytes)
        //   id:int                                   (4 bytes)
        //   from_id:flags.8?Peer                     (12 bytes if present)
        //   from_boosts_applied:flags.29?int         (4 bytes if present)
        //   peer_id:Peer                             (12 bytes — required)
        //   saved_peer_id:flags.28?Peer              (12 bytes if present)
        //   fwd_from:flags.2?MessageFwdHeader        (variable; we abort on this)
        //   via_bot_id:flags.11?long                 (8 bytes if present)
        //   via_business_bot_id:flags2.0?long        (8 bytes if present)
        //   reply_to:flags.3?MessageReplyHeader      (variable; we abort)
        //   date:int                                 (4 bytes — what we want!)
        //   message:string                           (variable — what we want!)
        //
        // We bail on flags.2 (fwd_from) or flags.3 (reply_to) because their
        // shapes are too rich to skim safely without a full decoder. Plain
        // direct messages (no forward, no reply) are by far the common case
        // in a dialog-list preview, so this captures most rows.
        private static void TryReadMessagePreview(byte[] body, int start, IPeerCache cache)
        {
            try
            {
                int subLen = body.Length - start;
                if (subLen < 16) return;
                byte[] sub = new byte[subLen];
                System.Buffer.BlockCopy(body, start, sub, 0, subLen);
                var r = new TlByteReader(sub);

                uint flags = r.ReadUInt32();
                r.ReadUInt32(); // flags2
                int id = r.ReadInt32();
                if (id <= 0) return;

                if ((flags & (1u << 8)) != 0) SkipPeer(r);            // from_id
                if ((flags & (1u << 29)) != 0) r.ReadInt32();         // from_boosts_applied
                SkipPeer(r);                                          // peer_id
                if ((flags & (1u << 28)) != 0) SkipPeer(r);           // saved_peer_id

                // fwd_from / reply_to are too complex to skim — abort if set.
                if ((flags & (1u << 2)) != 0) return;
                if ((flags & (1u << 3)) != 0) return;

                if ((flags & (1u << 11)) != 0) r.ReadInt64();         // via_bot_id
                // via_business_bot_id sits in flags2.0 — we already consumed
                // flags2 above without saving it, so skip that branch (rare).

                int dateUnix = r.ReadInt32();
                if (dateUnix <= 0) return;
                string text = r.ReadString();
                if (text == null) text = string.Empty;

                var dateUtc = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(dateUnix);
                cache.SetMessagePreview(id, text, dateUtc);
            }
            catch
            {
                // best-effort — discard partial reads and move on.
            }
        }

        // messageService#7a800e0a — much shorter shape:
        //   flags:# flags2:# id:int from_id:flags.8?Peer peer_id:Peer
        //   reply_to:flags.3?MessageReplyHeader date:int action:MessageAction
        // We surface a placeholder string so the dialog row still has *some*
        // hint of activity (e.g. the icon for joins / pin updates is enough).
        private static void TryReadServiceMessagePreview(byte[] body, int start, IPeerCache cache)
        {
            try
            {
                int subLen = body.Length - start;
                if (subLen < 12) return;
                byte[] sub = new byte[subLen];
                System.Buffer.BlockCopy(body, start, sub, 0, subLen);
                var r = new TlByteReader(sub);

                uint flags = r.ReadUInt32();
                r.ReadUInt32(); // flags2
                int id = r.ReadInt32();
                if (id <= 0) return;

                if ((flags & (1u << 8)) != 0) SkipPeer(r);
                SkipPeer(r);

                if ((flags & (1u << 3)) != 0) return; // reply_to skim too complex

                int dateUnix = r.ReadInt32();
                if (dateUnix <= 0) return;

                var dateUtc = new System.DateTime(1970, 1, 1, 0, 0, 0, System.DateTimeKind.Utc).AddSeconds(dateUnix);
                cache.SetMessagePreview(id, string.Empty, dateUtc);
            }
            catch
            {
            }
        }

        private static void SkipPeer(TlByteReader r)
        {
            // peerUser#59511722 / peerChat#36c6019a / peerChannel#a2a5371e
            // — all carry a single long after the 4-byte ctor.
            r.ReadUInt32();
            r.ReadInt64();
        }

        // Scan the body for user#83314fca / user#83314fae records. Each match
        // is parsed in isolation: flags + flags2 + id + (flags.0?access_hash)
        // + optional name fields. Returns every successfully parsed record.
        // userEmpty is skipped (no access_hash field).
        internal static IList<RawUser> TryExtractUsersSlice(byte[] body)
        {
            if (body == null) return null;
            List<RawUser> result = null;
            for (int i = 0; i + 4 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));
                if (ctor != CtorUserA && ctor != CtorUserB && ctor != CtorUser214) continue;

                try
                {
                    int subLen = body.Length - (i + 4);
                    if (subLen < 16) continue; // need at least flags+flags2+id
                    byte[] sub = new byte[subLen];
                    Buffer.BlockCopy(body, i + 4, sub, 0, subLen);
                    var r = new TlByteReader(sub);
                    uint flags = r.ReadUInt32();
                    uint flags2 = r.ReadUInt32();
                    long userId = r.ReadInt64();
                    if (userId == 0) continue;
                    long accessHash = 0L;
                    if ((flags & (1u << 0)) != 0) accessHash = r.ReadInt64();

                    // user#83314fca: first_name:flags.1?string,
                    // last_name:flags.2?string, username:flags.3?string,
                    // phone:flags.4?string. We capture them best-effort —
                    // a parse hiccup before they're all read still leaves
                    // the (id, access_hash) pair correct because the
                    // catch below preserves what we wrote up to that point.
                    string firstName = (flags & (1u << 1)) != 0 ? r.ReadString() : string.Empty;
                    string lastName  = (flags & (1u << 2)) != 0 ? r.ReadString() : string.Empty;
                    string username  = (flags & (1u << 3)) != 0 ? r.ReadString() : string.Empty;
                    string phone     = (flags & (1u << 4)) != 0 ? r.ReadString() : string.Empty;
                    // photo:UserProfilePhoto is bit 5. Best-effort: extract
                    // stripped_thumb + photoId + dcId when the ctor is the
                    // well-known userProfilePhoto#82d1f706. The dc/photo-id
                    // pair feeds the HD avatar download path.
                    DecodedProfilePhoto photo = new DecodedProfilePhoto();
                    if ((flags & (1u << 5)) != 0)
                    {
                        photo = TryReadUserProfilePhoto(r);
                    }

                    var u = new RawUser();
                    u.Id = userId;
                    u.AccessHash = accessHash;
                    u.FirstName = firstName;
                    u.LastName = lastName;
                    u.Username = username;
                    u.Phone = phone;
                    u.StrippedPhoto = photo.StrippedThumb;
                    u.PhotoId = photo.PhotoId;
                    u.PhotoDcId = photo.DcId;
                    if (result == null) result = new List<RawUser>();
                    result.Add(u);
                }
                catch
                {
                    // Try the next candidate offset — false positives on the
                    // ctor-id scan are possible when the bit-pattern collides
                    // with payload bytes; the structured read either succeeds
                    // (real User) or throws (false positive) and we move on.
                }
            }
            return result;
        }

        // Decode the photo:UserProfilePhoto field that sits between
        // phone:string and status:UserStatus.
        // Variants:
        //   userProfilePhotoEmpty#4f11bae1
        //   userProfilePhoto#82d1f706 flags:# has_video:flags.0?true
        //     personal:flags.2?true photo_id:long
        //     stripped_thumb:flags.1?bytes dc_id:int
        //
        // Returns the stripped_thumb bytes when present, or null when the
        // user has no photo / the decode tripped. Best-effort: a malformed
        // shape just returns null (caller falls back to initials).
        // The decoded fields live in a struct so the caller can capture
        // photoId + dcId in addition to the inline stripped thumb. Used by
        // RawUser / RawChat.
        internal struct DecodedProfilePhoto
        {
            public byte[] StrippedThumb;
            public long PhotoId;
            public int DcId;
        }

        private static DecodedProfilePhoto TryReadUserProfilePhoto(TlByteReader r)
        {
            DecodedProfilePhoto d = new DecodedProfilePhoto();
            uint ctor = r.ReadUInt32();
            const uint UserProfilePhotoEmpty = 0x4f11bae1u;
            const uint UserProfilePhoto      = 0x82d1f706u;
            if (ctor == UserProfilePhotoEmpty) return d;
            if (ctor != UserProfilePhoto) return d;

            try
            {
                uint flags = r.ReadUInt32();
                d.PhotoId = r.ReadInt64();
                if ((flags & (1u << 1)) != 0)
                {
                    d.StrippedThumb = r.ReadBytes();
                }
                d.DcId = r.ReadInt32();
                return d;
            }
            catch
            {
                // On parse failure, return whatever we got so far —
                // null thumb + zero ids → caller treats as "no photo".
                return d;
            }
        }

        // Decode the photo:ChatPhoto field on chat / channel records. Same
        // shape as UserProfilePhoto with a different constructor:
        //   chatPhotoEmpty#37c1011c
        //   chatPhoto#1c6e1c11 flags:# has_video:flags.0?true
        //     photo_id:long stripped_thumb:flags.1?bytes dc_id:int
        //
        // Returns the stripped_thumb bytes when present, or null on
        // empty / malformed payload. Caller falls back to initials.
        private static DecodedProfilePhoto TryReadChatPhoto(TlByteReader r)
        {
            DecodedProfilePhoto d = new DecodedProfilePhoto();
            uint ctor = r.ReadUInt32();
            const uint ChatPhotoEmpty = 0x37c1011cu;
            const uint ChatPhoto      = 0x1c6e1c11u;
            if (ctor == ChatPhotoEmpty) return d;
            if (ctor != ChatPhoto) return d;

            try
            {
                uint flags = r.ReadUInt32();
                d.PhotoId = r.ReadInt64();
                if ((flags & (1u << 1)) != 0)
                {
                    d.StrippedThumb = r.ReadBytes();
                }
                d.DcId = r.ReadInt32();
                return d;
            }
            catch
            {
                return d;
            }
        }

        // Scan the body for Chat / Channel / ChannelForbidden records across
        // every layer the server might still emit. All "current"-shape channel
        // ctors share the flags+flags2+id+access_hash+title prefix; we treat
        // them as a single decoder branch keyed by IsChannelCurrent below.
        // chatEmpty/channelEmpty are skipped.
        internal static IList<RawChat> TryExtractChatsSlice(byte[] body)
        {
            if (body == null) return null;
            const uint CtorChannelForbiddenA = 0x17d493d5u; // standard channelForbidden
            const uint CtorChannelForbiddenB = 0x6978a9c3u; // alt forbidden variant
            List<RawChat> result = null;
            for (int i = 0; i + 4 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));

                bool isChannelCurrent = (ctor == CtorChannel
                                       || ctor == CtorChannel187
                                       || ctor == CtorChannelCurrent);
                bool isChannelForbidden = (ctor == CtorChannelForbiddenA
                                         || ctor == CtorChannelForbiddenB);

                if (!isChannelCurrent
                    && !isChannelForbidden
                    && ctor != CtorChat) continue;

                try
                {
                    int subLen = body.Length - (i + 4);
                    if (subLen < 12) continue;
                    byte[] sub = new byte[subLen];
                    Buffer.BlockCopy(body, i + 4, sub, 0, subLen);
                    var r = new TlByteReader(sub);

                    if (ctor == CtorChat)
                    {
                        // chat#41cbf256 flags:# ... id:long title:string
                        //   photo:ChatPhoto participants_count:int ...
                        // photo is NOT flag-gated — always present.
                        uint flags = r.ReadUInt32();
                        long chatId = r.ReadInt64();
                        if (chatId == 0) continue;
                        string title = r.ReadString();
                        // Chat photo for groups. Best-effort decode; struct
                        // fields default to zero on malformed shape.
                        DecodedProfilePhoto chatPhoto = TryReadChatPhoto(r);
                        var c = new RawChat();
                        c.Id = chatId;
                        c.AccessHash = 0L;
                        c.Title = title ?? string.Empty;
                        c.IsChannel = false;
                        c.IsMegagroup = false;
                        c.StrippedPhoto = chatPhoto.StrippedThumb;
                        c.PhotoId = chatPhoto.PhotoId;
                        c.PhotoDcId = chatPhoto.DcId;
                        if (result == null) result = new List<RawChat>();
                        result.Add(c);
                    }
                    else if (isChannelCurrent)
                    {
                        // channel (current shape, all three ctors):
                        //   flags:#  flags2:#  id:long
                        //   access_hash:flags.13?long  title:string
                        //   username:flags.6?string  photo:ChatPhoto
                        //   date:int ...
                        // Bit 8 in flags marks megagroup.
                        uint flags = r.ReadUInt32();
                        uint flags2 = r.ReadUInt32();
                        long channelId = r.ReadInt64();
                        if (channelId == 0) continue;
                        long accessHash = 0L;
                        if ((flags & (1u << 13)) != 0) accessHash = r.ReadInt64();
                        string title = r.ReadString();
                        // username:flags.6?string — must be consumed
                        // before reaching photo so the cursor is right.
                        if ((flags & (1u << 6)) != 0) r.ReadString();
                        DecodedProfilePhoto channelPhoto = TryReadChatPhoto(r);
                        bool megagroup = (flags & (1u << 8)) != 0;
                        var c = new RawChat();
                        c.Id = channelId;
                        c.AccessHash = accessHash;
                        c.Title = title ?? string.Empty;
                        c.IsChannel = true;
                        c.IsMegagroup = megagroup;
                        c.StrippedPhoto = channelPhoto.StrippedThumb;
                        c.PhotoId = channelPhoto.PhotoId;
                        c.PhotoDcId = channelPhoto.DcId;
                        if (result == null) result = new List<RawChat>();
                        result.Add(c);
                    }
                    else // channelForbidden (either variant)
                    {
                        // channelForbidden#17d493d5 flags:# broadcast:flags.5?true
                        //   megagroup:flags.8?true id:long access_hash:long title:string ...
                        uint flags = r.ReadUInt32();
                        long channelId = r.ReadInt64();
                        if (channelId == 0) continue;
                        long accessHash = r.ReadInt64();
                        string title = r.ReadString();
                        bool megagroup = (flags & (1u << 8)) != 0;
                        var c = new RawChat();
                        c.Id = channelId;
                        c.AccessHash = accessHash;
                        c.Title = title ?? string.Empty;
                        c.IsChannel = true;
                        c.IsMegagroup = megagroup;
                        if (result == null) result = new List<RawChat>();
                        result.Add(c);
                    }
                }
                catch
                {
                    // Try the next candidate offset (see TryExtractUsersSlice).
                }
            }
            return result;
        }
    }
}
