// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Application.Handlers;
using Vianigram.Contacts.Application.UseCases;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.Events;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Contacts.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Contacts.Application
{
    /// <summary>
    /// <see cref="IContactsApi"/> implementation. Dispatches each public method
    /// to the matching handler, surfaces results as
    /// <c>Result&lt;T, ContactsError&gt;</c>, and re-broadcasts internal domain
    /// events on the kernel bus into a CLR event
    /// (<see cref="ContactsChanged"/>) so XAML/UI consumers don't need an
    /// <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="ContactsError"/>.
    /// </summary>
    public sealed class ContactsApplication : IContactsApi, IDisposable
    {
        private readonly SyncContactsHandler _sync;
        private readonly GetContactsHandler _getContacts;
        private readonly ImportContactsHandler _import;
        private readonly SearchContactsHandler _search;
        private readonly BlockUserHandler _block;
        private readonly UnblockUserHandler _unblock;
        private readonly GetBlockedListHandler _getBlocked;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<ContactsChangedEventArgs> ContactsChanged;

        public ContactsApplication(
            IMtProtoRpcPort rpc,
            IContactRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _sync = new SyncContactsHandler(repo, rpc, bus, logger, clock);
            _getContacts = new GetContactsHandler(repo, logger);
            _import = new ImportContactsHandler(repo, rpc, bus, logger, clock);
            _search = new SearchContactsHandler(rpc, logger);
            _block = new BlockUserHandler(repo, rpc, bus, logger, clock);
            _unblock = new UnblockUserHandler(repo, rpc, bus, logger, clock);
            _getBlocked = new GetBlockedListHandler(repo, rpc, bus, logger, clock);

            _subs = new IDisposable[]
            {
                bus.Subscribe<ContactImported>(OnContactImported),
                bus.Subscribe<ContactUpdated>(OnContactUpdated),
                bus.Subscribe<ContactRemoved>(OnContactRemoved),
                bus.Subscribe<UserBlocked>(OnUserBlocked),
                bus.Subscribe<UserUnblocked>(OnUserUnblocked),
                bus.Subscribe<ContactsSynced>(OnContactsSynced)
            };
        }

        // ---- IContactsApi ----------------------------------------------------

        public async Task<Result<IList<Contact>, ContactsError>> SyncContactsAsync(CancellationToken ct)
        {
            try
            {
                return await _sync.HandleAsync(SyncContactsCommand.Initial, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("SyncContactsAsync failed", ex));
            }
        }

        public async Task<Result<IList<Contact>, ContactsError>> GetContactsAsync(CancellationToken ct)
        {
            try
            {
                return await _getContacts.HandleAsync(GetContactsCommand.Default, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("GetContactsAsync failed", ex));
            }
        }

        public async Task<Result<IList<Contact>, ContactsError>> ImportContactsAsync(
            IList<ContactImportRequest> requests, CancellationToken ct)
        {
            try
            {
                if (requests == null)
                    return Result<IList<Contact>, ContactsError>.Fail(ContactsError.NotInExpectedState("requests required"));
                return await _import.HandleAsync(new ImportContactsCommand(requests), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("ImportContactsAsync failed", ex));
            }
        }

        public async Task<Result<IList<Contact>, ContactsError>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            try
            {
                if (query == null)
                    return Result<IList<Contact>, ContactsError>.Fail(ContactsError.NotInExpectedState("query required"));
                if (limit <= 0)
                    return Result<IList<Contact>, ContactsError>.Fail(ContactsError.NotInExpectedState("limit must be positive"));
                return await _search.HandleAsync(new SearchContactsCommand(query, limit), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("SearchAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ContactsError>> BlockAsync(long userId, CancellationToken ct)
        {
            try
            {
                if (userId <= 0)
                    return Result<Unit, ContactsError>.Fail(ContactsError.NotInExpectedState("userId must be positive"));
                var cmd = new BlockUserCommand(new UserId(userId), /*accessHash*/ 0L);
                return await _block.HandleAsync(cmd, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ContactsError>.Fail(ContactsError.Unknown("BlockAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ContactsError>> UnblockAsync(long userId, CancellationToken ct)
        {
            try
            {
                if (userId <= 0)
                    return Result<Unit, ContactsError>.Fail(ContactsError.NotInExpectedState("userId must be positive"));
                var cmd = new UnblockUserCommand(new UserId(userId), /*accessHash*/ 0L);
                return await _unblock.HandleAsync(cmd, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ContactsError>.Fail(ContactsError.Unknown("UnblockAsync failed", ex));
            }
        }

        public async Task<Result<IList<long>, ContactsError>> GetBlockedListAsync(CancellationToken ct)
        {
            try
            {
                return await _getBlocked.HandleAsync(GetBlockedListCommand.FirstPage, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<long>, ContactsError>.Fail(ContactsError.Unknown("GetBlockedListAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge ----------------------------------------

        private void OnContactImported(ContactImported e)
        {
            Raise(new ContactsChangedEventArgs(ContactsChangedEventArgs.ChangeReason.ContactImported, e.UserId, e.At));
        }

        private void OnContactUpdated(ContactUpdated e)
        {
            Raise(new ContactsChangedEventArgs(ContactsChangedEventArgs.ChangeReason.ContactUpdated, e.UserId, e.At));
        }

        private void OnContactRemoved(ContactRemoved e)
        {
            Raise(new ContactsChangedEventArgs(ContactsChangedEventArgs.ChangeReason.ContactRemoved, e.UserId, e.At));
        }

        private void OnUserBlocked(UserBlocked e)
        {
            Raise(new ContactsChangedEventArgs(ContactsChangedEventArgs.ChangeReason.UserBlocked, e.UserId, e.At));
        }

        private void OnUserUnblocked(UserUnblocked e)
        {
            Raise(new ContactsChangedEventArgs(ContactsChangedEventArgs.ChangeReason.UserUnblocked, e.UserId, e.At));
        }

        private void OnContactsSynced(ContactsSynced e)
        {
            Raise(new ContactsChangedEventArgs(ContactsChangedEventArgs.ChangeReason.ListSynced, null, e.At));
        }

        private void Raise(ContactsChangedEventArgs args)
        {
            var h = ContactsChanged;
            if (h == null) return;
            try
            {
                h(this, args);
            }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
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
