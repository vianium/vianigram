// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Windows.Foundation;

namespace Vianigram.SmokeTests.Tests
{
    internal static class LiveSmokeTestSupport
    {
        private const string Component = "Live.Diag";

        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        public static void Diag(string message)
        {
            EarlyLog.Write(Component, message);
        }

        public static async Task<T> AwaitAsync<T>(
            IAsyncOperation<T> operation,
            TimeSpan timeout,
            string step,
            Stopwatch stopwatch,
            CancellationToken ct,
            Action<T> lateSuccessCleanup = null)
        {
            if (operation == null)
                throw new InvalidOperationException(step + " returned null IAsyncOperation.");

            long stepStartMs = stopwatch.ElapsedMilliseconds;
            Diag(step + " begin");

            var task = operation.AsTask();
            var timeoutTask = Task.Delay(timeout, ct);
            var completed = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);

            if (completed == task)
            {
                T result = await task.ConfigureAwait(false);
                long totalMs = stopwatch.ElapsedMilliseconds;
                long stepMs = totalMs - stepStartMs;
                Diag(step + " returned in " +
                     stepMs.ToString(CultureInfo.InvariantCulture) +
                     " ms (total=" +
                     totalMs.ToString(CultureInfo.InvariantCulture) + " ms)");
                return result;
            }

            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);

            TryCancel(operation, step);
            ObserveLateCompletion(task, step, lateSuccessCleanup);

            throw new TimeoutException(
                step + " timed out after " +
                ((long)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture) + "s.");
        }

        private static void TryCancel<T>(IAsyncOperation<T> operation, string step)
        {
            try
            {
                operation.Cancel();
                Diag(step + " timeout - cancellation requested");
            }
            catch (Exception ex)
            {
                Diag(step + " timeout - Cancel threw " +
                     ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void ObserveLateCompletion<T>(
            Task<T> task,
            string step,
            Action<T> lateSuccessCleanup)
        {
            task.ContinueWith(t =>
            {
                try
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception == null ? null : t.Exception.GetBaseException();
                        Diag(step + " completed after timeout with fault: " +
                             (ex == null ? "<unknown>" : ex.GetType().Name + ": " + ex.Message));
                        return;
                    }

                    if (t.IsCanceled)
                    {
                        Diag(step + " completed after timeout as canceled");
                        return;
                    }

                    Diag(step + " completed after timeout; cleaning up late result");
                    if (lateSuccessCleanup != null)
                        lateSuccessCleanup(t.Result);
                }
                catch (Exception ex)
                {
                    Diag(step + " late completion observer threw " +
                         ex.GetType().Name + ": " + ex.Message);
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
