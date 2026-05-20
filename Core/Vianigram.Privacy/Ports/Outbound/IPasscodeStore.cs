// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the on-device passcode material store.
    ///
    /// <para><see cref="Infrastructure.InMemoryPasscodeStore"/>
    /// keeps the state in process memory (lost across app launches). The
    /// production adapter wraps
    /// <c>Windows.Storage.ApplicationData.Current.LocalFolder</c> +
    /// <c>DataProtectionProvider</c> and lives in <c>Vianigram.Storage</c>
    /// (or the App composition root) where the WinRT dependency is acceptable.</para>
    ///
    /// <para><b>Contract</b>: never throws across the port. Read / write /
    /// clear faults map to <see cref="PrivacyError.StorageError"/>; a
    /// non-existent passcode (i.e. "feature disabled") is surfaced as
    /// <see cref="PasscodeState.Disabled"/> from <see cref="LoadAsync"/>, NOT
    /// as an error.</para>
    /// </summary>
    public interface IPasscodeStore
    {
        /// <summary>Read the current passcode state. Returns <see cref="PasscodeState.Disabled"/> when no passcode has been configured.</summary>
        Task<Result<PasscodeState, PrivacyError>> LoadAsync(CancellationToken ct);

        /// <summary>Persist a new passcode state (upsert).</summary>
        Task<Result<Unit, PrivacyError>> SaveAsync(PasscodeState state, CancellationToken ct);

        /// <summary>Drop the persisted passcode (i.e. disable). Idempotent — clearing an empty store is success.</summary>
        Task<Result<Unit, PrivacyError>> ClearAsync(CancellationToken ct);
    }
}
