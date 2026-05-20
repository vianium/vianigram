// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// InlineUiDispatcher.cs — Vianigram.Kernel.Concurrency
//
// Pass-through IUiDispatcher implementation used by tests, smoke runs, and
// background tasks where there is no XAML dispatcher available. Runs the
// scheduled action synchronously on the calling thread.

using System;
using System.Threading.Tasks;

namespace Vianigram.Kernel.Concurrency
{
    /// <summary>
    /// Pass-through dispatcher: actions execute synchronously on the
    /// caller's thread. Use when there is no UI thread to marshal onto
    /// (tests, background tasks).
    /// </summary>
    public sealed class InlineUiDispatcher : IUiDispatcher
    {
        public static readonly InlineUiDispatcher Instance = new InlineUiDispatcher();

        public bool HasUiThreadAccess { get { return true; } }

        public Task RunOnUiAsync(Action action)
        {
            if (action == null) return CompletedTask;
            try { action(); }
            catch
            {
                // Swallow — match the IEventBus contract: a single broken
                // subscriber doesn't poison the chain.
            }
            return CompletedTask;
        }

        // Pre-completed task helper for the WP 8.1 / pre-net46 surface
        // that does not have Task.CompletedTask.
        private static readonly Task CompletedTask = MakeCompleted();
        private static Task MakeCompleted()
        {
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
