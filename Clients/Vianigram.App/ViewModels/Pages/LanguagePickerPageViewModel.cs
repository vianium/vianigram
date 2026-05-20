// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LanguagePickerPageViewModel.cs
//
// Drives LanguagePickerPage. Surfaces the catalogue of supported UI
// languages with the active one flagged, and a SelectLanguageCommand that
// applies the choice and rolls the user back to the welcome page so the
// new strings appear immediately.

using System.Collections.Generic;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class LanguageOptionVm : ObservableObject
    {
        private bool _isSelected;

        public string Code { get; set; }
        public string NativeName { get; set; }
        public string EnglishName { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }

    public sealed class LanguagePickerPageViewModel : ObservableObject
    {
        private readonly INavigationService _nav;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public LanguagePickerPageViewModel()
            : this(null)
        {
        }

        public LanguagePickerPageViewModel(INavigationService nav)
        {
            _nav = nav;

            var languages = new List<LanguageOptionVm>();
            string current = LanguageService.CurrentCode;
            foreach (var lang in LanguageService.SupportedLanguages)
            {
                languages.Add(new LanguageOptionVm
                {
                    Code = lang.Code,
                    NativeName = lang.NativeName,
                    EnglishName = lang.EnglishName,
                    IsSelected = string.Equals(lang.Code, current, System.StringComparison.OrdinalIgnoreCase),
                });
            }
            Languages = languages;

            SelectLanguageCommand = new RelayCommand(OnSelectLanguage);
            BackCommand = new RelayCommand(_ => OnBack());
        }

        public IList<LanguageOptionVm> Languages { get; private set; }

        public ICommand SelectLanguageCommand { get; private set; }
        public ICommand BackCommand { get; private set; }

        private void OnSelectLanguage(object parameter)
        {
            var option = parameter as LanguageOptionVm;
            if (option == null) return;

            // Apply the new language first so the upcoming WelcomePage instance
            // resolves its x:Uid lookups against the freshly-loaded resource map.
            LanguageService.Apply(option.Code);

            // Reflect the selection in the in-memory list so a brief glimpse of
            // the picker (e.g. during the navigation transition) shows the
            // tick on the right row.
            for (int i = 0; i < Languages.Count; i++)
            {
                Languages[i].IsSelected = string.Equals(Languages[i].Code, option.Code, System.StringComparison.OrdinalIgnoreCase);
            }

            // Bounce to Welcome — the page has the default cache mode (Disabled)
            // so the constructor runs again and pulls the new strings.
            if (_nav != null) _nav.NavigateTo(Route.Welcome);
        }

        private void OnBack()
        {
            if (_nav == null) return;
            if (_nav.CanGoBack) _nav.GoBack();
            else _nav.NavigateTo(Route.Welcome);
        }
    }
}
