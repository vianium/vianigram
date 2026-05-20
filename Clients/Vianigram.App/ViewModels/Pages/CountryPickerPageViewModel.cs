// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class CountryOptionVm : ObservableObject
    {
        private bool _isSelected;

        public CountryOptionVm(TelegramCountryEntry country)
        {
            Country = country;
        }

        public TelegramCountryEntry Country { get; private set; }

        public string DisplayName
        {
            get { return Country != null ? Country.DisplayName : string.Empty; }
        }

        public string DialCode
        {
            get { return Country != null ? Country.DialCode : string.Empty; }
        }

        public string Iso2
        {
            get { return Country != null ? Country.Iso2 : string.Empty; }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }

    public sealed class CountryPickerPageViewModel : ObservableObject
    {
        private readonly INavigationService _nav;
        private readonly List<CountryOptionVm> _allCountries = new List<CountryOptionVm>();
        private string _searchQuery;
        private bool _isLoading;
        private bool _hasNoResults;
        private string _errorMessage;

        public CountryPickerPageViewModel()
            : this(null)
        {
        }

        public CountryPickerPageViewModel(INavigationService nav)
        {
            _nav = nav;
            Countries = new ObservableCollection<CountryOptionVm>();
            SelectCountryCommand = new RelayCommand(OnSelectCountry);
            BackCommand = new RelayCommand(_ => OnBack());
        }

        public ObservableCollection<CountryOptionVm> Countries { get; private set; }

        public string SearchQuery
        {
            get { return _searchQuery; }
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    RefreshFilter();
                }
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public bool HasNoResults
        {
            get { return _hasNoResults; }
            private set { SetProperty(ref _hasNoResults, value); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    OnPropertyChanged("HasError");
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_errorMessage); }
        }

        public ICommand SelectCountryCommand { get; private set; }
        public ICommand BackCommand { get; private set; }

        public async Task LoadAsync()
        {
            if (_allCountries.Count > 0)
            {
                RefreshSelection();
                RefreshFilter();
                return;
            }

            IsLoading = true;
            ErrorMessage = null;

            try
            {
                IList<TelegramCountryEntry> entries =
                    await TelegramCountryCatalog.LoadAsync().ConfigureAwait(true);

                await CountrySelectionService.EnsureCurrentAsync().ConfigureAwait(true);

                _allCountries.Clear();
                for (int index = 0; index < entries.Count; index++)
                {
                    _allCountries.Add(new CountryOptionVm(entries[index]));
                }

                RefreshSelection();
                RefreshFilter();
            }
            catch
            {
                ErrorMessage = Strings.Get("CountryPickerLoadError");
                Countries.Clear();
                HasNoResults = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnSelectCountry(object parameter)
        {
            CountryOptionVm option = parameter as CountryOptionVm;
            if (option == null || option.Country == null)
            {
                return;
            }

            CountrySelectionService.Select(option.Country);
            RefreshSelection();

            if (_nav != null)
            {
                if (_nav.CanGoBack) _nav.GoBack();
                else _nav.NavigateTo(Route.PhoneNumber);
            }
        }

        private void OnBack()
        {
            if (_nav == null) return;
            if (_nav.CanGoBack) _nav.GoBack();
            else _nav.NavigateTo(Route.PhoneNumber);
        }

        private void RefreshSelection()
        {
            TelegramCountryEntry selected = CountrySelectionService.Current;
            for (int index = 0; index < _allCountries.Count; index++)
            {
                CountryOptionVm option = _allCountries[index];
                option.IsSelected = IsSameCountry(option.Country, selected);
            }
        }

        private void RefreshFilter()
        {
            string filter = (_searchQuery ?? string.Empty).Trim();
            Countries.Clear();

            for (int index = 0; index < _allCountries.Count; index++)
            {
                CountryOptionVm option = _allCountries[index];
                if (MatchesFilter(option, filter))
                {
                    Countries.Add(option);
                }
            }

            HasNoResults = !IsLoading && _allCountries.Count > 0 && Countries.Count == 0;
        }

        private static bool MatchesFilter(CountryOptionVm option, string filter)
        {
            if (option == null) return false;
            if (string.IsNullOrWhiteSpace(filter)) return true;

            if (Contains(option.DisplayName, filter)) return true;
            if (Contains(option.Iso2, filter)) return true;
            if (Contains(option.DialCode, filter)) return true;

            string digits = TelegramCountryCatalog.StripToDigits(filter);
            return digits.Length > 0 && Contains(option.DialCode, digits);
        }

        private static bool Contains(string value, string filter)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(filter)) return false;
            return value.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static bool IsSameCountry(TelegramCountryEntry left, TelegramCountryEntry right)
        {
            if (left == null || right == null) return false;
            return string.Equals(left.Code, right.Code, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.Iso2, right.Iso2, StringComparison.OrdinalIgnoreCase);
        }
    }
}
