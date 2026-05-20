// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Result;

namespace Vianigram.Messages.Domain
{
    /// <summary>
    /// Errors emitted from the Messages bounded context. Wraps a Kernel
    /// <see cref="Error"/> with a typed code so the API surface stays
    /// non-throwing even across async send/edit paths.
    /// </summary>
    public sealed class MessageError
    {
        public MessageError(MessageErrorCode code, string message, Error inner = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            Inner = inner;
        }

        public MessageErrorCode Code { get; private set; }
        public string Message { get; private set; }
        public Error Inner { get; private set; }

        public static MessageError InvalidArgument(string message)
        {
            return new MessageError(MessageErrorCode.InvalidArgument, message);
        }

        public static MessageError NotFound(string message)
        {
            return new MessageError(MessageErrorCode.NotFound, message);
        }

        public static MessageError NetworkFailed(string message, Error inner = null)
        {
            return new MessageError(MessageErrorCode.NetworkFailed, message, inner);
        }

        public static MessageError ProtocolError(string message, Error inner = null)
        {
            return new MessageError(MessageErrorCode.ProtocolError, message, inner);
        }

        public static MessageError FloodWait(int seconds)
        {
            return new MessageError(MessageErrorCode.FloodWait, "FLOOD_WAIT_" + seconds);
        }

        public static MessageError Unauthorized(string message)
        {
            return new MessageError(MessageErrorCode.Unauthorized, message);
        }

        public static MessageError InvalidState(string message)
        {
            return new MessageError(MessageErrorCode.InvalidState, message);
        }

        public override string ToString()
        {
            return "messages." + Code + ": " + Message;
        }
    }

    public enum MessageErrorCode
    {
        Unknown = 0,
        InvalidArgument = 1,
        NotFound = 2,
        NetworkFailed = 3,
        ProtocolError = 4,
        FloodWait = 5,
        Unauthorized = 6,
        InvalidState = 7,
        Cancelled = 8
    }
}
