// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Application.Commands;
using Vianigram.Account.Application.Handlers;
using Vianigram.Account.Domain.Entities;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.Events;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;

namespace Vianigram.Account.Application
{
    /// <summary>
    /// Concrete implementation of <see cref="IAccountApi"/>. Owns the single
    /// <see cref="AccountIdentity"/> aggregate, builds the per-flow handlers,
    /// dispatches commands to them, and re-broadcasts domain events as a CLR
    /// <see cref="StateChanged"/> event for non-event-bus consumers (XAML
    /// view-models that prefer EventHandler{T}).
    ///
    /// The composition root constructs this object directly via
    /// <see cref="Vianigram.Account.Composition.AccountCompositionRoot.Register"/>;
    /// no other entry-point should be needed.
    /// </summary>
    public sealed class AccountApplication : IAccountApi, IPhoneLoginPreparationApi, IDisposable
    {
        // Default DC (Telegram production DC2). Composition can override
        // by constructing AccountApplication via the explicit overload.
        private const int DefaultActiveDcId = 2;

        // Placeholder API credentials. Production must inject real values via
        // the explicit-arguments constructor; the public Register(...) signature
        // is held to the spec and uses these defaults so unit tests of the
        // command-dispatch layer still compile and the surface stays stable.
        private const int DefaultApiId = 0;
        private const string DefaultApiHash = "";

        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly IComponentLogger _appLog;
        private readonly SendPhoneCodeHandler _send;
        private readonly ResendPhoneCodeHandler _resend;
        private readonly VerifyPhoneCodeHandler _verify;
        private readonly SignUpHandler _signUp;
        private readonly SubmitTwoFaPasswordHandler _twoFa;
        private readonly LogoutHandler _logout;
        private readonly PollQrLoginHandler _qrLogin;
        private readonly GetSelfHandler _getSelf;
        private readonly UpdateProfileHandler _updateProfile;
        private readonly CheckUsernameHandler _checkUsername;
        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<AccountStateChanged> StateChanged;

        /// <summary>
        /// Composition-root constructor matching the spec'd
        /// <c>AccountCompositionRoot.Register</c> signature. Builds the
        /// aggregate, the four flow handlers, and the bus-to-CLR-event bridge.
        /// </summary>
        public AccountApplication(
            IMtProtoRpcPort rpc,
            IAuthKeyStore keyStore,
            IAuthKeyGeneratorPort keyGen,
            ISrpClientPort srp,
            IEventBus bus,
            ILogger logger,
            IClock clock)
            : this(rpc, keyStore, keyGen, srp, bus, logger, clock,
                   Vianigram.Kernel.Telemetry.NullTelemetry.Instance, DefaultApiId, DefaultApiHash, DefaultActiveDcId, null)
        {
        }

        /// <summary>
        /// Explicit-arguments constructor for callers that need to inject real
        /// API credentials, an active DC, or telemetry.
        /// </summary>
        public AccountApplication(
            IMtProtoRpcPort rpc,
            IAuthKeyStore keyStore,
            IAuthKeyGeneratorPort keyGen,
            ISrpClientPort srp,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            ITelemetry telemetry,
            int apiId,
            string apiHash,
            int activeDcId,
            IPreferredDcStore preferredDcStore = null)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (keyStore == null) throw new ArgumentNullException("keyStore");
            if (keyGen == null) throw new ArgumentNullException("keyGen");
            if (srp == null) throw new ArgumentNullException("srp");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            if (apiHash == null) throw new ArgumentNullException("apiHash");

            _aggregate = new AccountIdentity(bus, clock);
            _rpc = rpc;
            _preferredDcStore = preferredDcStore;
            _appLog = new TimestampedLogger(logger, "Account.Application");
            _send = new SendPhoneCodeHandler(_aggregate, rpc, logger, telemetry, apiId, apiHash);
            _resend = new ResendPhoneCodeHandler(_aggregate, rpc, logger, telemetry);
            _verify = new VerifyPhoneCodeHandler(_aggregate, rpc, keyStore, logger, telemetry, activeDcId, preferredDcStore);
            _signUp = new SignUpHandler(_aggregate, rpc, keyStore, logger, telemetry, activeDcId, preferredDcStore);
            _twoFa = new SubmitTwoFaPasswordHandler(_aggregate, rpc, srp, keyStore, logger, telemetry, activeDcId, preferredDcStore);
            _logout = new LogoutHandler(_aggregate, rpc, keyStore, logger, telemetry, activeDcId, preferredDcStore);
            _qrLogin = new PollQrLoginHandler(
                _aggregate, rpc, keyStore, logger, telemetry, activeDcId, apiId, apiHash, preferredDcStore);
            _getSelf = new GetSelfHandler(rpc, logger, telemetry);
            _updateProfile = new UpdateProfileHandler(rpc, logger, telemetry);
            _checkUsername = new CheckUsernameHandler(rpc, logger, telemetry);

            _subs = new IDisposable[]
            {
                bus.Subscribe<PhoneAuthInitiated>(_ => RaiseStateChanged()),
                bus.Subscribe<CodeSent>(_ => RaiseStateChanged()),
                bus.Subscribe<TwoFaRequired>(_ => RaiseStateChanged()),
                bus.Subscribe<AuthSucceeded>(_ => RaiseStateChanged()),
                bus.Subscribe<AuthFailed>(_ => RaiseStateChanged()),
                bus.Subscribe<LoggedOut>(_ => RaiseStateChanged())
            };
        }

        public AccountStateSnapshot CurrentState
        {
            get { return _aggregate.Snapshot(); }
        }

        /// <summary>
        /// Boot-time rehydration. Reads the persisted (homeDcId, userId)
        /// pair from <see cref="IPreferredDcStore"/> and lifts the aggregate
        /// to <see cref="AuthState.AuthStateKind.Authorized"/> so
        /// the host's PickInitialPage routes the user straight into the
        /// chat list instead of re-asking for the phone number. The MTProto
        /// channel loads/decrypts the auth_key lazily before the first RPC.
        ///
        /// Idempotent and best-effort: any storage failure leaves the
        /// aggregate in its existing state (Anonymous on first launch),
        /// which is the safe default — the user will simply re-login.
        /// </summary>
        public Task<bool> RehydrateFromPersistenceAsync(CancellationToken ct)
        {
            if (_preferredDcStore == null)
            {
                _appLog.Info("rehydrate skipped: no IPreferredDcStore wired");
                return BoolTask(false);
            }

            int dcId;
            long userId;
            try
            {
                dcId = _preferredDcStore.GetHomeDcId();
                userId = _preferredDcStore.GetUserId();
            }
            catch (Exception ex)
            {
                _appLog.Warn("rehydrate read threw: " + ex.GetType().Name + ": " + ex.Message);
                return BoolTask(false);
            }

            if (dcId <= 0 || userId <= 0L)
            {
                _appLog.Info("rehydrate skipped: no persisted session (dc=" + dcId + " userId=" + userId + ")");
                return BoolTask(false);
            }

            ct.ThrowIfCancellationRequested();
            var apply = _aggregate.RestoreAuthorizedMarker(new UserId(userId), dcId);
            if (apply.IsFail)
            {
                _appLog.Warn("rehydrate restore failed: " + apply.Error);
                return BoolTask(false);
            }

            // Surface the restored state to whoever is listening to
            // StateChanged (PickInitialPage doesn't subscribe — it polls
            // CurrentState directly — but ChatListPage / Settings VMs may).
            RaiseStateChanged();
            _appLog.Info("rehydrate ok: dc=" + dcId + " userId=" + userId + " key=deferred");
            return BoolTask(true);
        }

        public async Task<Result<Unit, AccountError>> SendPhoneCodeAsync(string phoneE164, CancellationToken ct)
        {
            var phoneResult = PhoneNumber.TryParse(phoneE164);
            if (phoneResult.IsFail)
            {
                return Result<Unit, AccountError>.Fail(phoneResult.Error);
            }

            var cmd = new SendPhoneCodeCommand(phoneResult.Value);
            return await _send.HandleAsync(cmd, ct).ConfigureAwait(false);
        }

        public async Task PreparePhoneLoginAsync(string phoneE164, CancellationToken ct)
        {
            var phoneResult = PhoneNumber.TryParse(phoneE164);
            if (phoneResult.IsFail)
            {
                return;
            }

            var dcPreference = _rpc as IPhoneLoginDcPreferencePort;
            int hintedDc = 0;
            if (dcPreference != null)
            {
                hintedDc = dcPreference.PreferDcForPhone(phoneResult.Value.E164);
            }

            var warmup = _rpc as ILoginConnectionWarmupPort;
            if (warmup == null)
            {
                return;
            }

            try
            {
                _appLog.Info("prepare phone login begin phone=" + phoneResult.Value.E164 + " dc_hint=" + hintedDc);
                await warmup.WarmUpAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _appLog.Info("prepare phone login cancelled");
            }
            catch (Exception ex)
            {
                _appLog.Warn("prepare phone login ignored: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static Task<bool> BoolTask(bool value)
        {
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        public async Task<Result<Unit, AccountError>> ResendPhoneCodeAsync(CancellationToken ct)
        {
            return await _resend.HandleAsync(ResendPhoneCodeCommand.Instance, ct).ConfigureAwait(false);
        }

        public async Task<Result<AuthOutcome, AccountError>> VerifyCodeAsync(string code, CancellationToken ct)
        {
            if (_aggregate.Phone == null)
            {
                return Result<AuthOutcome, AccountError>.Fail(
                    AccountError.NotInExpectedState("VerifyCode requires SendPhoneCode first"));
            }

            var cmd = new VerifyPhoneCodeCommand(_aggregate.Phone, code);
            return await _verify.HandleAsync(cmd, ct).ConfigureAwait(false);
        }

        public async Task<Result<AuthOutcome, AccountError>> SignUpAsync(string firstName, string lastName, CancellationToken ct)
        {
            var cmd = new SignUpCommand(firstName, lastName);
            return await _signUp.HandleAsync(cmd, ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, AccountError>> SubmitTwoFaPasswordAsync(string password, CancellationToken ct)
        {
            var cmd = new SubmitTwoFaPasswordCommand(password);
            return await _twoFa.HandleAsync(cmd, ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, AccountError>> LogoutAsync(CancellationToken ct)
        {
            return await _logout.HandleAsync(LogoutCommand.Instance, ct).ConfigureAwait(false);
        }

        public async Task<Result<QrLoginPoll, AccountError>> RequestQrTokenAsync(CancellationToken ct)
        {
            return await _qrLogin.HandleAsync(ct).ConfigureAwait(false);
        }

        public async Task<Result<QrLoginPoll, AccountError>> PollQrLoginAsync(QrLoginToken token, CancellationToken ct)
        {
            // Token argument is intentionally ignored — per Telegram's QR
            // protocol the unauthenticated client polls by re-issuing
            // exportLoginToken, not by importing the prior token. Both
            // entry points resolve to the same wire call.
            return await _qrLogin.HandleAsync(ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, AccountError>> RegisterPushDeviceAsync(
            int tokenType, string token, byte[] secret, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("RegisterPushDevice requires a non-empty token"));
            }
            byte[] req = Vianigram.Account.Infrastructure.TlEncoder.EncodeAccountRegisterDevice(
                tokenType, token, appSandbox: false, secret: secret ?? new byte[0],
                otherUids: null, noMuted: false);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _appLog.Warn("account.registerDevice failed: " + rpcResult.Error);
                // Translate generic rpc error into AccountError.
                return Result<Unit, AccountError>.Fail(
                    AccountError.Unknown("account.registerDevice rpc failed: " + rpcResult.Error.Message));
            }
            _appLog.Info("account.registerDevice ok tokenType=" + tokenType + " tokenLen=" + token.Length);
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        public async Task<Result<Unit, AccountError>> UnregisterPushDeviceAsync(
            int tokenType, string token, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(token))
            {
                return Result<Unit, AccountError>.Ok(Unit.Value); // nothing to unregister
            }
            byte[] req = Vianigram.Account.Infrastructure.TlEncoder.EncodeAccountUnregisterDevice(
                tokenType, token, otherUids: null);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _appLog.Warn("account.unregisterDevice failed: " + rpcResult.Error);
                return Result<Unit, AccountError>.Fail(
                    AccountError.Unknown("account.unregisterDevice rpc failed: " + rpcResult.Error.Message));
            }
            _appLog.Info("account.unregisterDevice ok");
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        public async Task PrewarmQrLoginDcsAsync(CancellationToken ct)
        {
            var migrationPort = _rpc as IQrLoginMigrationPort;
            if (migrationPort == null)
            {
                _appLog.Info("PrewarmQrLoginDcs skipped: transport doesn't implement IQrLoginMigrationPort");
                return;
            }

            // DC#1 is the dominant migrate target for the Americas user
            // base; we warm it unconditionally. Other DCs aren't pre-warmed
            // because (a) they're rare destinations and (b) speculative
            // handshakes burn ~12 s of CPU + radio per DC on slow phones.
            // After the first successful login the persisted login DC hint
            // means subsequent launches boot directly on the user's home
            // DC and never need a migrate.
            try
            {
                await migrationPort.PrewarmAuthKeyAsync(1, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal — page navigated away mid-prewarm.
            }
            catch (Exception ex)
            {
                _appLog.Warn("PrewarmQrLoginDcs ignored: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public async Task<Result<SelfProfile, AccountError>> GetSelfAsync(CancellationToken ct)
        {
            return await _getSelf.HandleAsync(ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, AccountError>> UpdateProfileAsync(string firstName, string lastName, string username, string bio, CancellationToken ct)
        {
            return await _updateProfile.HandleAsync(firstName, lastName, username, bio, ct).ConfigureAwait(false);
        }

        public async Task<Result<bool, AccountError>> CheckUsernameAsync(string username, CancellationToken ct)
        {
            return await _checkUsername.HandleAsync(username, ct).ConfigureAwait(false);
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

        private void RaiseStateChanged()
        {
            var handler = StateChanged;
            if (handler == null) return;
            try
            {
                handler(this, new AccountStateChanged(_aggregate.Snapshot()));
            }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

    }
}
