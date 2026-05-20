// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Update the auto-download policy for one network classification (carried
    /// by <see cref="Policy"/>'s <see cref="DataUsagePolicy.Network"/>). The
    /// handler persists the composite value under the matching
    /// <c>network.auto_download.*</c> key and stages a
    /// <c>DataPolicyChanged</c> domain event.
    /// </summary>
    public sealed class UpdateDataUsageCommand
    {
        public DataUsagePolicy Policy { get; private set; }

        public UpdateDataUsageCommand(DataUsagePolicy policy)
        {
            Policy = policy;
        }
    }
}
