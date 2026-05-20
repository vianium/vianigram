// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IUiDispatcher.cs — Vianigram.Kernel.Concurrency
//
// Marshals work onto the UI thread so handlers wired to events on the
// IEventBus (which delivers synchronously on the publisher's thread) can
// safely touch ObservableCollection / TextBlock / Frame state without
// crashing. WP 8.1 throws InvalidOperationException when XAML state is
// mutated from a non-UI thread; every subscriber that updates view-model
// properties from a domain event needs this.
//
// This abstraction lives in the Kernel because Messages, Chats, Calls,
// Contacts and Notifications all need to raise CLR events that land on
// the UI thread when there is one — but the same code must also work in
// background-task contexts (RawNotificationTask, smoke tests) where there
// is no XAML dispatcher. The implementations decide how to handle
// "no UI thread present"; the contract is that the work always runs.
//
// Implementations expected:
//   - WindowsPhoneUiDispatcher: wraps CoreApplication.MainView.CoreWindow.Dispatcher.
//   - BackgroundTaskUiDispatcher: runs synchronously inline (no UI present).
//   - InlineUiDispatcher: pass-through for tests.

using System;
using System.Threading.Tasks;

namespace Vianigram.Kernel.Concurrency
{
    /// <summary>
    /// Cross-context UI marshaler. Every consumer that updates XAML state
    /// from a domain event handler should route through here; the
    /// composition root selects the right implementation for the host
    /// process (foreground app vs background task vs tests).
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>
        /// True when the calling thread is the UI thread itself. Useful
        /// for fast paths (skip the round-trip when we're already on it).
        /// </summary>
        bool HasUiThreadAccess { get; }

        /// <summary>
        /// Schedule <paramref name="action"/> on the UI thread. Returns
        /// when the work has completed (or scheduled, depending on impl).
        /// Safe to call from any thread. Null actions are no-ops.
        /// </summary>
        Task RunOnUiAsync(Action action);
    }
}
