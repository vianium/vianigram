// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// VoipKeepAliveTask.cs — Vianigram.App.BackgroundTasks
//
// "VoIP-style" background task. Vianigram has native voice/video calls
// (VianiumVoIP) which qualifies the app for the
// `windows.backgroundTasks` Extension with task type `audio` — the WP
// 8.1 task category that survives normal suspension cycles, with
// CPU/network access until the OS forcibly evicts under memory
// pressure or extreme battery saver.
//
// In a fully-built v2, this task would:
//   - Reproduce the storage bootstrap (load auth_key, home DC).
//   - Open its own MtProtoChannel (since the foreground is suspended).
//   - Subscribe to native push updates via the channel's Subscribe()
//     handler.
//   - For each MessageReceived push: decode peer + body and toast.
//   - Stay alive until the OS suspends the host process.
//
// In v1 (this file) the task plays the same shape as the periodic
// task: it reads the unread summary the foreground app maintains and
// toasts. The difference is registration cadence + manifest type:
// the OS may invoke us on system events (network change, time
// trigger, etc.) more often than the strict 30-min PeriodicTask, so
// the user gets faster recovery on reconnect.
//
// We deliberately do NOT block on a long socket here in v1. WP 8.1
// will kill the task if it consumes more than ~25 s of CPU in one
// invocation. Long-lived MtProto channel work belongs in the dedicated
// background coordination engine.

using Windows.ApplicationModel.Background;

namespace Vianigram.App.BackgroundTasks
{
    public sealed class VoipKeepAliveTask : IBackgroundTask
    {
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            BackgroundTaskDeferral deferral = null;
            try
            {
                if (taskInstance != null) deferral = taskInstance.GetDeferral();

                BackgroundTaskRunner runner = new BackgroundTaskRunner();
                runner.RunTick("voip");
            }
            catch
            {
                // Same defensive swallow as MtprotoMaintenanceTask —
                // a throwing IBackgroundTask gets unregistered.
            }
            finally
            {
                if (deferral != null) deferral.Complete();
            }
        }
    }
}
