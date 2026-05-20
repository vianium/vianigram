// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ObservableObject.cs
//
// Minimal INotifyPropertyChanged base that the page ViewModels inherit
// from. If a dedicated Vianigram.ViewModels assembly ships a
// <c>BaseViewModel</c>, this can switch to that. For now the App owns its
// own base class so it can build standalone.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace Vianigram.App.ViewModels
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Marshals the supplied work onto the UI dispatcher. Safe to call
        /// from any thread; if already on the UI thread, runs inline.
        /// </summary>
        protected Task DispatchOnUiAsync(System.Action work)
        {
            CoreDispatcher dispatcher = null;
            try
            {
                var view = CoreApplication.MainView;
                if (view != null && view.CoreWindow != null) dispatcher = view.CoreWindow.Dispatcher;
            }
            catch
            {
                dispatcher = null;
            }

            if (dispatcher == null || dispatcher.HasThreadAccess)
            {
                if (work != null) work();
                var tcs = new TaskCompletionSource<bool>();
                tcs.SetResult(true);
                return tcs.Task;
            }

            return dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (work != null) work();
            }).AsTask();
        }
    }
}
