// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Application.Commands;
using Vianigram.Account.Domain.Entities;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;

namespace Vianigram.Account.Application.Handlers
{
    /// <summary>
    /// Best-effort server-side logout (auth.logOut #5717da40 returns Bool) +
    /// local auth_key wipe. Per principles.md §10 (Logout latency) we do not
    /// block forever on the network: if the server call fails the local wipe
    /// proceeds anyway.
    /// </summary>
    internal sealed class LogoutHandler
    {
        // auth.logOut#3e72ba19 = Bool;  (layer 158+: just int constructor, no body).
        private const uint AuthLogOut = 0x3e72ba19;

        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IAuthKeyStore _authKeyStore;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly int _activeDcId;

        public LogoutHandler(
            AccountIdentity aggregate,
            IMtProtoRpcPort rpc,
            IAuthKeyStore authKeyStore,
            ILogger log,
            ITelemetry telemetry,
            int activeDcId,
            IPreferredDcStore preferredDcStore = null)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _aggregate = aggregate;
            _rpc = rpc;
            _authKeyStore = authKeyStore;
            _preferredDcStore = preferredDcStore;
            _log = new TimestampedLogger(log, "Account.Logout");
            _telemetry = telemetry;
            _activeDcId = activeDcId;
        }

        public async Task<Result<Unit, AccountError>> HandleAsync(
            LogoutCommand command,
            CancellationToken ct)
        {
            byte[] request = new byte[4];
            request[0] = (byte)(AuthLogOut & 0xff);
            request[1] = (byte)((AuthLogOut >> 8) & 0xff);
            request[2] = (byte)((AuthLogOut >> 16) & 0xff);
            request[3] = (byte)((AuthLogOut >> 24) & 0xff);

            try
            {
                var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
                if (rpcResult.IsFail)
                {
                    _log.Warn("auth.logOut returned error: " + rpcResult.Error);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("auth.logOut threw; proceeding with local wipe: " + ex.Message);
            }

            try
            {
                await _authKeyStore.DeleteAsync(GetCurrentDcId(), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn("auth_key delete failed: " + ex.Message);
            }

            // Forget the persisted home DC so the next login (typically a
            // different account) doesn't pre-warm against the prior user's DC.
            if (_preferredDcStore != null)
            {
                try { _preferredDcStore.Clear(); }
                catch (Exception ex)
                {
                    _log.Warn("home DC clear failed: " + ex.Message);
                }
            }

            _telemetry.Track("account.logout.count", 1);
            return _aggregate.ApplyLogout();
        }

        private int GetCurrentDcId()
        {
            var dcProvider = _rpc as IMtProtoDcProvider;
            if (dcProvider != null && dcProvider.CurrentDcId > 0)
            {
                return dcProvider.CurrentDcId;
            }
            return _activeDcId;
        }
    }
}
