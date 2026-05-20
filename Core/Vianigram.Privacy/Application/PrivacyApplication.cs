// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Privacy.Application.Handlers;
using Vianigram.Privacy.Application.UseCases;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.Entities;
using Vianigram.Privacy.Domain.Events;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Ports.Inbound;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Application
{
    /// <summary>
    /// <see cref="IPrivacyApi"/> implementation. Dispatches each public method
    /// to the matching handler, surfaces results as
    /// <c>Result&lt;T, PrivacyError&gt;</c>, and re-broadcasts the
    /// <see cref="PrivacyRuleChanged"/> / <see cref="SessionTerminated"/>
    /// domain events on the kernel bus into CLR events so XAML / UI consumers
    /// don't need an <see cref="IEventBus"/> dependency.
    ///
    /// <para>All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="PrivacyError"/>.</para>
    /// </summary>
    public sealed class PrivacyApplication : IPrivacyApi, IDisposable
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IPasscodeStore _store;
        private readonly IPasscodeHasher _hasher;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;
        private readonly PrivacyProfile _profile;

        private readonly GetPrivacyRulesHandler _getRule;
        private readonly SetPrivacyRuleHandler _setRule;
        private readonly GetActiveSessionsHandler _getSessions;
        private readonly TerminateSessionHandler _terminateSession;
        private readonly TerminateAllOtherSessionsHandler _terminateAll;
        private readonly EnablePasscodeHandler _enablePasscode;
        private readonly VerifyPasscodeHandler _verifyPasscode;
        private readonly DisablePasscodeHandler _disablePasscode;
        private readonly ChangePasscodeHandler _changePasscode;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<PrivacyRuleChangedEventArgs> RuleChanged;
        public event EventHandler<SessionTerminatedEventArgs> SessionTerminated;

        public PrivacyApplication(
            IMtProtoRpcPort rpc,
            IPasscodeStore store,
            IPasscodeHasher hasher,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (store == null) throw new ArgumentNullException("store");
            if (hasher == null) throw new ArgumentNullException("hasher");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _rpc = rpc;
            _store = store;
            _hasher = hasher;
            _bus = bus;
            _log = new TimestampedLogger(logger, "Privacy.Application");
            _clock = clock;
            _profile = new PrivacyProfile();

            _getRule = new GetPrivacyRulesHandler(rpc, _profile, bus, logger, clock);
            _setRule = new SetPrivacyRuleHandler(rpc, _profile, bus, logger, clock);
            _getSessions = new GetActiveSessionsHandler(rpc, _profile, bus, logger, clock);
            _terminateSession = new TerminateSessionHandler(rpc, _profile, bus, logger, clock);
            _terminateAll = new TerminateAllOtherSessionsHandler(rpc, _profile, bus, logger, clock);

            _enablePasscode = new EnablePasscodeHandler(store, hasher, _profile, bus, logger, clock);
            _verifyPasscode = new VerifyPasscodeHandler(store, hasher, _profile, bus, logger, clock);
            _disablePasscode = new DisablePasscodeHandler(store, _verifyPasscode, _profile, bus, logger, clock);
            _changePasscode = new ChangePasscodeHandler(_verifyPasscode, _enablePasscode, logger, clock);

            _subs = new IDisposable[]
            {
                bus.Subscribe<PrivacyRuleChanged>(OnRuleChanged),
                bus.Subscribe<Domain.Events.SessionTerminated>(OnSessionTerminated)
            };

            // Best-effort: hydrate the in-memory passcode state from the
            // store synchronously through a Task.Run pattern. We don't block
            // the constructor; a stale `IsPasscodeEnabled` flag for the first
            // few millis after construction is acceptable. Callers that need
            // the canonical flag at boot await any IPrivacyApi method first.
            HydrateFromStoreAsync();
        }

        public bool IsPasscodeEnabled
        {
            get { return _profile.IsPasscodeEnabled; }
        }

        public async Task<Result<PrivacyRule, PrivacyError>> GetRuleAsync(PrivacyKey key, CancellationToken ct)
        {
            try
            {
                return await _getRule.HandleAsync(new GetPrivacyRulesCommand(key), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<PrivacyRule, PrivacyError>.Fail(PrivacyError.Unknown("GetRuleAsync failed", ex));
            }
        }

        public async Task<Result<Unit, PrivacyError>> SetRuleAsync(PrivacyKey key, PrivacyRule rule, CancellationToken ct)
        {
            try
            {
                if (rule == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.InvalidValue("rule required"));
                return await _setRule.HandleAsync(new SetPrivacyRuleCommand(key, rule), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("SetRuleAsync failed", ex));
            }
        }

        public async Task<Result<IList<ActiveSession>, PrivacyError>> GetSessionsAsync(CancellationToken ct)
        {
            try
            {
                return await _getSessions.HandleAsync(GetActiveSessionsCommand.Instance, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<ActiveSession>, PrivacyError>.Fail(PrivacyError.Unknown("GetSessionsAsync failed", ex));
            }
        }

        public async Task<Result<Unit, PrivacyError>> TerminateSessionAsync(long hash, CancellationToken ct)
        {
            try
            {
                return await _terminateSession.HandleAsync(new TerminateSessionCommand(hash), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("TerminateSessionAsync failed", ex));
            }
        }

        public async Task<Result<Unit, PrivacyError>> TerminateAllOtherSessionsAsync(CancellationToken ct)
        {
            try
            {
                return await _terminateAll.HandleAsync(TerminateAllOtherSessionsCommand.Instance, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("TerminateAllOtherSessionsAsync failed", ex));
            }
        }

        public async Task<Result<Unit, PrivacyError>> EnablePasscodeAsync(string pin, CancellationToken ct)
        {
            try
            {
                return await _enablePasscode.HandleAsync(new EnablePasscodeCommand(pin), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("EnablePasscodeAsync failed", ex));
            }
        }

        public async Task<Result<Unit, PrivacyError>> DisablePasscodeAsync(string pin, CancellationToken ct)
        {
            try
            {
                return await _disablePasscode.HandleAsync(new DisablePasscodeCommand(pin), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("DisablePasscodeAsync failed", ex));
            }
        }

        public async Task<Result<bool, PrivacyError>> VerifyPasscodeAsync(string pin, CancellationToken ct)
        {
            try
            {
                return await _verifyPasscode.HandleAsync(new VerifyPasscodeCommand(pin), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<bool, PrivacyError>.Fail(PrivacyError.Unknown("VerifyPasscodeAsync failed", ex));
            }
        }

        public async Task<Result<Unit, PrivacyError>> ChangePasscodeAsync(string oldPin, string newPin, CancellationToken ct)
        {
            try
            {
                return await _changePasscode.HandleAsync(new ChangePasscodeCommand(oldPin, newPin), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("ChangePasscodeAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge -----------------------------------------

        private void OnRuleChanged(PrivacyRuleChanged e)
        {
            var h = RuleChanged;
            if (h == null) return;
            try { h(this, new PrivacyRuleChangedEventArgs(e.Key, e.Rule, e.At)); }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        private void OnSessionTerminated(Domain.Events.SessionTerminated e)
        {
            var h = SessionTerminated;
            if (h == null) return;
            try { h(this, new SessionTerminatedEventArgs(e.Hash, e.At)); }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        private async void HydrateFromStoreAsync()
        {
            try
            {
                var loaded = await _store.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                if (loaded.IsOk && loaded.Value != null && loaded.Value.Enabled)
                {
                    _profile.RecordPasscode(loaded.Value, _clock.UtcNow);
                    // Drain on the bus thread; fire-and-forget.
                    HandlerEventBridge.Drain(_profile, _bus);
                }
            }
            catch (Exception ex)
            {
                _log.Info("hydrate from store failed (treated as disabled): " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }
    }
}
