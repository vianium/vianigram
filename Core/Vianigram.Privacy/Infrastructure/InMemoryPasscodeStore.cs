// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Infrastructure
{
    /// <summary>
    /// In-memory <see cref="IPasscodeStore"/>: keeps the
    /// <see cref="PasscodeState"/> in process memory guarded by a private
    /// monitor. Lost on app launch.
    ///
    /// <para><b>Hot-swap point</b>: the host composition root replaces this
    /// with the persistent adapter that wraps <c>LocalFolder/privacy/</c> +
    /// <c>DataProtectionProvider</c> (see
    /// <c>docs/managed-architecture/13-privacy.md §9</c>). The application
    /// layer is unchanged because the port is identical.</para>
    /// </summary>
    public sealed class InMemoryPasscodeStore : IPasscodeStore
    {
        private readonly object _gate = new object();
        private PasscodeState _state = PasscodeState.Disabled;

        public Task<Result<PasscodeState, PrivacyError>> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(Result<PasscodeState, PrivacyError>.Ok(_state));
            }
        }

        public Task<Result<Unit, PrivacyError>> SaveAsync(PasscodeState state, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (state == null)
                return Task.FromResult(Result<Unit, PrivacyError>.Fail(PrivacyError.InvalidValue("state required")));
            lock (_gate)
            {
                _state = state;
            }
            return Task.FromResult(Result<Unit, PrivacyError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, PrivacyError>> ClearAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _state = PasscodeState.Disabled;
            }
            return Task.FromResult(Result<Unit, PrivacyError>.Ok(Unit.Value));
        }
    }
}
