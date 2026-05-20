// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProxyPersistenceSmokeTest — exercises the disk-backed
// LocalSettingsPreferencesStore + the ProxyBootstrap.LoadAndApply
// path that runs at app start before the first MTProto dial.
//
// Together these two prove that a ProxyConfig saved on disk survives
// a relaunch + correctly arms the native MtProxyRuntime registry.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Composition.Infrastructure;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Infrastructure;

namespace Vianigram.SmokeTests.Tests
{
    public static class ProxyPersistenceSmokeTest
    {
        // Sentinel preference key used only by this smoke test. We
        // pick a namespaced key that no production code touches so
        // round-trips don't collide with real preferences.
        private const string TestKey = "smoketest.proxy.persist";

        public static async Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var entries = new List<TestEntry>();

            ILogger log = new DebugLogger();
            var store = new LocalSettingsPreferencesStore(log);

            // 1. Round-trip a string through the disk store.
            await RunCaseAsync(entries, "LocalSettingsPreferencesStore round-trip", async () =>
            {
                var setR = await store.SetRawAsync(TestKey, "hello-vianium", ct).ConfigureAwait(false);
                if (setR.IsFail) return "SetRawAsync failed: " + setR.Error;
                var getR = await store.GetRawAsync(TestKey, ct).ConfigureAwait(false);
                if (getR.IsFail) return "GetRawAsync failed";
                if (getR.Value != "hello-vianium") return "value mismatch: got '" + getR.Value + "'";
                return null;
            });

            // 2. Remove + re-read returns empty.
            await RunCaseAsync(entries, "LocalSettingsPreferencesStore remove", async () =>
            {
                var rmR = await store.RemoveAsync(TestKey, ct).ConfigureAwait(false);
                if (rmR.IsFail) return "RemoveAsync failed";
                var getR = await store.GetRawAsync(TestKey, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(getR.Value)) return "value should be empty after remove: '" + getR.Value + "'";
                return null;
            });

            // 3. ProxyBootstrap.LoadAndApply with NOTHING configured
            //    must clear the native runtime cleanly.
            RunCase(entries, "ProxyBootstrap clears runtime when no descriptor saved", () =>
            {
                try
                {
                    Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy();
                    // Ensure the key is absent before bootstrap.
                    var k = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
                    if (k.ContainsKey("network.proxy.mtproto")) k.Remove("network.proxy.mtproto");

                    ProxyBootstrap.LoadAndApply(log);
                    if (Vianigram.MTProto.MtProxyRuntime.IsActive())
                        return "runtime active after empty bootstrap";
                    return null;
                }
                catch (Exception ex)
                {
                    return ex.GetType().Name + ": " + ex.Message;
                }
            });

            // 4. ProxyBootstrap arms the runtime from a saved descriptor.
            RunCase(entries, "ProxyBootstrap arms runtime from saved descriptor", () =>
            {
                try
                {
                    // Write a ProxyConfig blob to LocalSettings directly,
                    // matching the PreferenceSerializer v1 wire format.
                    string blob = "v1|1|smoke.example.com|443|0|"
                        + "00112233445566778899aabbccddeeff" + "||smoke-test";
                    Windows.Storage.ApplicationData.Current.LocalSettings
                        .Values["network.proxy.mtproto"] = blob;

                    Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy();
                    ProxyBootstrap.LoadAndApply(log);

                    bool active = Vianigram.MTProto.MtProxyRuntime.IsActive();
                    return active ? null : "runtime not armed after bootstrap";
                }
                catch (Exception ex)
                {
                    return ex.GetType().Name + ": " + ex.Message;
                }
                finally
                {
                    try { Windows.Storage.ApplicationData.Current.LocalSettings
                        .Values.Remove("network.proxy.mtproto"); }
                    catch { }
                    try { Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy(); }
                    catch { }
                }
            });

            // 5. MtProxyRuntimeSink.Apply round-trips a ProxyConfig
            //    through the live sink path (the production hook).
            RunCase(entries, "MtProxyRuntimeSink.Apply arms then disarms", () =>
            {
                try
                {
                    var sink = new MtProxyRuntimeSink(log);
                    var cfg = new ProxyConfig(
                        enabled: true,
                        host: "sink.example.com",
                        port: 443,
                        secret: new byte[16],
                        mode: ProxySecretMode.Legacy,
                        fakeTlsDomain: string.Empty,
                        label: "sink-smoke");
                    sink.Apply(cfg);
                    if (!Vianigram.MTProto.MtProxyRuntime.IsActive())
                        return "runtime not active after sink Apply";

                    sink.Apply(ProxyConfig.Disabled);
                    if (Vianigram.MTProto.MtProxyRuntime.IsActive())
                        return "runtime still active after Disabled apply";
                    return null;
                }
                catch (Exception ex)
                {
                    return ex.GetType().Name + ": " + ex.Message;
                }
            });

            // Cleanup: scrub the sentinel + leave runtime clear.
            try { await store.RemoveAsync(TestKey, ct).ConfigureAwait(false); } catch { }
            try { Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy(); } catch { }

            return entries;
        }

        private static void RunCase(List<TestEntry> entries, string name, Func<string> body)
        {
            string fail = null;
            try { fail = body(); }
            catch (Exception ex) { fail = ex.GetType().Name + ": " + ex.Message; }
            entries.Add(new TestEntry
            {
                Suite  = "ProxyPersistence",
                Name   = name,
                Passed = fail == null,
                Detail = fail ?? "ok"
            });
        }

        private static async Task RunCaseAsync(
            List<TestEntry> entries, string name, Func<Task<string>> body)
        {
            string fail = null;
            try { fail = await body().ConfigureAwait(false); }
            catch (Exception ex) { fail = ex.GetType().Name + ": " + ex.Message; }
            entries.Add(new TestEntry
            {
                Suite  = "ProxyPersistence",
                Name   = name,
                Passed = fail == null,
                Detail = fail ?? "ok"
            });
        }
    }
}
