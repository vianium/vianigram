// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Api.V1
{
    /// <summary>
    /// V1 surface of the Messages bounded context. The actual interface lives
    /// in <c>Ports.Inbound.IMessagesApi</c>; this namespace is the
    /// versioned alias other contexts and the Composition root use to bind.
    ///
    /// Future breaking changes get a parallel <c>Api.V2</c> namespace with a
    /// new interface; V1 stays untouched.
    /// </summary>
    public static class MessagesApiV1
    {
        /// <summary>Stable identifier for capability registry / feature flags.</summary>
        public const string ApiVersion = "messages.v1";
    }
}
