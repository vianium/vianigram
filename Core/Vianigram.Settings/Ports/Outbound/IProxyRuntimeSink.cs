// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Ports.Outbound
{
    /// <summary>
    /// Outbound sink that propagates a persisted <see cref="ProxyConfig"/>
    /// to the live MTProto transport layer. The Settings bounded context
    /// owns the persisted descriptor; this port lets it nudge the
    /// runtime so subsequent socket dials (and existing channels'
    /// reconnect attempts) pick up the new state.
    ///
    /// Implementations live outside the Settings context to keep
    /// Vianigram.Settings free of MTProto / WinRT dependencies. The
    /// canonical impl is in
    /// <c>Vianigram.Composition.Infrastructure.MtProxyRuntimeSink</c> and
    /// bridges to <c>Vianigram.MTProto.MtProxyRuntime</c>.
    ///
    /// Apply MUST be idempotent and side-effect-free on failure (the
    /// settings save flow rolls back on a thrown exception). Stub
    /// implementations (used by the in-memory composition root) just
    /// no-op.
    /// </summary>
    public interface IProxyRuntimeSink
    {
        /// <summary>
        /// Install <paramref name="config"/> as the active proxy. Passing
        /// <see cref="ProxyConfig.Disabled"/> (or any descriptor with
        /// <c>Enabled=false</c>) clears the runtime — subsequent dials
        /// go direct.
        /// </summary>
        void Apply(ProxyConfig config);
    }
}
