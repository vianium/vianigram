// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BackgroundTaskRegistrar.cs — Vianigram.App.Services
//
// Registers the two Vianigram.App.BackgroundTasks entry points with the
// WP 8.1 BackgroundTaskBuilder so the OS will spawn the WinRT component
// process even when the foreground app is suspended or terminated:
//
//   - MtprotoMaintenanceTask: TimeTrigger(30, false) → "every 30 min"
//     safety net. The OS-imposed minimum is 15 min; we use 30 to
//     reduce battery drain.
//
//   - VoipKeepAliveTask: SystemTrigger on InternetAvailable +
//     TimeZoneChange + UserPresent → "wake on the user touching the
//     phone or reconnecting" so backlog flushes near-realtime when
//     the device is alive.
//
// Both registrations are idempotent — calling Register twice does
// not create duplicates because we look up by name first.
//
// We also expose Unregister for the logout flow so a signed-out
// account doesn't keep waking the device.

using System;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Windows.ApplicationModel.Background;

namespace Vianigram.App.Services
{
    public sealed class BackgroundTaskRegistrar
    {
        public const string PeriodicTaskName = "VianigramMtprotoMaintenance";
        public const string PeriodicTaskEntryPoint = "Vianigram.App.BackgroundTasks.MtprotoMaintenanceTask";
        public const uint PeriodicIntervalMinutes = 30;

        public const string VoipTaskName = "VianigramVoipKeepAlive";
        public const string VoipTaskEntryPoint = "Vianigram.App.BackgroundTasks.VoipKeepAliveTask";

        private readonly IComponentLogger _log;

        public BackgroundTaskRegistrar(IComponentLogger log)
        {
            if (log == null) throw new ArgumentNullException("log");
            _log = log;
        }

        public async Task RegisterAllAsync()
        {
            try
            {
                BackgroundAccessStatus access = await BackgroundExecutionManager.RequestAccessAsync().AsTask().ConfigureAwait(false);
                _log.Info("background-tasks: access status=" + access);
                if (access != BackgroundAccessStatus.AllowedMayUseActiveRealTimeConnectivity &&
                    access != BackgroundAccessStatus.AllowedWithAlwaysOnRealTimeConnectivity)
                {
                    _log.Warn("background-tasks: not allowed by user / OS — registration skipped");
                    return;
                }

                RegisterPeriodicTask();
                RegisterVoipKeepAliveTask();
            }
            catch (Exception ex)
            {
                _log.Warn("background-tasks: register threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public void UnregisterAll()
        {
            try
            {
                foreach (var kv in BackgroundTaskRegistration.AllTasks)
                {
                    IBackgroundTaskRegistration reg = kv.Value;
                    if (reg == null) continue;
                    string name = reg.Name ?? string.Empty;
                    if (name == PeriodicTaskName || name == VoipTaskName)
                    {
                        try
                        {
                            reg.Unregister(true);
                            _log.Info("background-tasks: unregistered " + name);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("background-tasks: unregister threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void RegisterPeriodicTask()
        {
            if (IsRegistered(PeriodicTaskName))
            {
                _log.Info("background-tasks: " + PeriodicTaskName + " already registered");
                return;
            }
            var builder = new BackgroundTaskBuilder();
            builder.Name = PeriodicTaskName;
            builder.TaskEntryPoint = PeriodicTaskEntryPoint;
            // 30-min cadence (OS-imposed minimum is 15 min on WP 8.1).
            builder.SetTrigger(new TimeTrigger(PeriodicIntervalMinutes, false));
            // Skip the tick if the device is on battery saver and the
            // foreground app isn't active — saves drain when the user
            // explicitly asked the OS to throttle.
            builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
            BackgroundTaskRegistration registration = builder.Register();
            _log.Info("background-tasks: registered " + PeriodicTaskName +
                " interval=" + PeriodicIntervalMinutes + "min id=" + registration.TaskId);
        }

        private void RegisterVoipKeepAliveTask()
        {
            if (IsRegistered(VoipTaskName))
            {
                _log.Info("background-tasks: " + VoipTaskName + " already registered");
                return;
            }
            var builder = new BackgroundTaskBuilder();
            builder.Name = VoipTaskName;
            builder.TaskEntryPoint = VoipTaskEntryPoint;
            // SystemTrigger on InternetAvailable: fires when the device
            // regains connectivity (WiFi back, 4G handoff, airplane
            // mode off). That's the moment any backlog is most likely
            // to have accumulated and the OS is clearly awake.
            builder.SetTrigger(new SystemTrigger(SystemTriggerType.InternetAvailable, false));
            builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));
            BackgroundTaskRegistration registration = builder.Register();
            _log.Info("background-tasks: registered " + VoipTaskName +
                " trigger=InternetAvailable id=" + registration.TaskId);
        }

        private static bool IsRegistered(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var kv in BackgroundTaskRegistration.AllTasks)
            {
                IBackgroundTaskRegistration reg = kv.Value;
                if (reg != null && string.Equals(reg.Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
