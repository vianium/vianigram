// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtprotoMaintenanceTask.cs — Vianigram.App.BackgroundTasks
//
// Periodic-trigger background task. Registered with TimeTrigger(30, false),
// so the WP 8.1 OS guarantees execution at least every 30 minutes
// (subject to battery saver / memory pressure throttling). Per-tick CPU
// budget is ~25 seconds.
//
// Responsibilities:
//   - Quick exit if the foreground heartbeat is fresh (BackgroundTaskHeartbeat
//     covers the coordination invariant; we never compete with the UI for
//     the auth_key).
//   - Read the unread summary the foreground app maintains in
//     LocalSettings (peer + last body excerpt + count).
//   - Show a toast and update the live tile so the user sees that
//     messages arrived even if the app was fully suspended.
//
// This is the SAFETY NET in the layered notification design (per docs):
// when the VoIP keep-alive task is throttled or fails, this 30-min
// floor still ensures the user is never silently behind for more than
// ~30 min. Cheap (<1 s per tick, <1 KB of state).
//
// MTProxy follow-up (when v2 opens its own channel from this task):
// before any dial, run
//   Vianigram.Composition.Infrastructure.ProxyBootstrap.LoadAndApply(logger)
// so the obfuscated transport is armed from the persisted descriptor.
// That requires this project to grow ProjectReferences to Composition +
// Settings + Kernel + the native Vianium.MTProto winmd; deferred until
// the v2 background channel work lands (see runner header notes for
// the v1 storage-only path rationale).

using Windows.ApplicationModel.Background;

namespace Vianigram.App.BackgroundTasks
{
    public sealed class MtprotoMaintenanceTask : IBackgroundTask
    {
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            BackgroundTaskDeferral deferral = null;
            try
            {
                if (taskInstance != null) deferral = taskInstance.GetDeferral();

                BackgroundTaskRunner runner = new BackgroundTaskRunner();
                runner.RunTick("periodic");
            }
            catch
            {
                // A throwing background task can be unregistered by the
                // OS — swallow defensively. Toast / tile failures are
                // already handled inside RunTick.
            }
            finally
            {
                if (deferral != null) deferral.Complete();
            }
        }
    }
}
