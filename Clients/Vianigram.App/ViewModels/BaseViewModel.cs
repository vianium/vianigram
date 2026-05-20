// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BaseViewModel.cs
//
// Local stand-in for the eventual Vianigram.ViewModels.BaseViewModel that
// will live in the Core/Vianigram.ViewModels assembly. Today that assembly
// is empty, so the App project owns its base class. When the dedicated VM
// assembly ships, this type can be deleted and a using-alias points the
// page-VMs at the real one — surface stays identical.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Vianigram.App.ViewModels
{
    public abstract class BaseViewModel : ObservableObject
    {
        // Inherits SetProperty/OnPropertyChanged/DispatchOnUiAsync from
        // ObservableObject. Kept as a separate type so page-VMs can be
        // declared as `: BaseViewModel` per the team-wide MVVM convention
        // even before the Core assembly is populated.
    }
}
