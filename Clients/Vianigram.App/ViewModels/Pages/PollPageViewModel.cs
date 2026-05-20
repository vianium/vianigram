// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PollPageViewModel.cs — poll-composer VM.
// CreateCommand calls IMessagesApi.SendPollAsync with the assembled PollSpec;
// peerKey arrives as a navigation parameter and is replayed on success.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class PollOptionVm : ObservableObject
    {
        private string _text;
        private bool _isCorrect;

        public string Text
        {
            get { return _text; }
            set { SetProperty(ref _text, value); }
        }

        public bool IsCorrect
        {
            get { return _isCorrect; }
            set { SetProperty(ref _isCorrect, value); }
        }

        public string PlaceholderText { get; set; }
    }

    public sealed class PollPageViewModel : ObservableObject
    {
        private const int MinOptions = 2;
        private const int MaxOptions = 10;

        private readonly IMessagesApi _messages;
        private readonly INavigationService _nav;

        private string _peerKey;
        private string _question;
        private bool _isAnonymous;
        private bool _multipleAnswers;
        private bool _isQuiz;
        private int _correctIndex;
        private string _statusText;
        private string _errorMessage;
        private bool _isBusy;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public PollPageViewModel()
            : this(null, null)
        {
        }

        public PollPageViewModel(IMessagesApi messages, INavigationService nav)
        {
            _messages = messages;
            _nav = nav;

            Options = new ObservableCollection<PollOptionVm>();
            _isAnonymous = true;
            _correctIndex = -1;

            AddOptionCommand = new RelayCommand(_ => AddOption(), _ => CanAddOption);
            RemoveOptionCommand = new RelayCommand(p => RemoveOption(p as PollOptionVm), _ => CanRemoveOption);
            CreateCommand = new AsyncCommand(_ => CreateAsync(), _ => CanCreate);

            Options.Add(new PollOptionVm { PlaceholderText = "Option 1" });
            Options.Add(new PollOptionVm { PlaceholderText = "Option 2" });
        }

        public ObservableCollection<PollOptionVm> Options { get; private set; }

        public ICommand AddOptionCommand { get; private set; }
        public ICommand RemoveOptionCommand { get; private set; }
        public AsyncCommand CreateCommand { get; private set; }

        public string Question
        {
            get { return _question; }
            set
            {
                if (SetProperty(ref _question, value))
                {
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsAnonymous
        {
            get { return _isAnonymous; }
            set { SetProperty(ref _isAnonymous, value); }
        }

        public bool MultipleAnswers
        {
            get { return _multipleAnswers; }
            set
            {
                if (SetProperty(ref _multipleAnswers, value))
                {
                    if (value && _isQuiz) IsQuiz = false;
                }
            }
        }

        public bool IsQuiz
        {
            get { return _isQuiz; }
            set
            {
                if (SetProperty(ref _isQuiz, value))
                {
                    if (value && _multipleAnswers) MultipleAnswers = false;
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public int CorrectIndex
        {
            get { return _correctIndex; }
            set
            {
                if (SetProperty(ref _correctIndex, value))
                {
                    for (int i = 0; i < Options.Count; i++)
                    {
                        var opt = Options[i];
                        if (opt != null) opt.IsCorrect = (i == value);
                    }
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
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

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public bool CanAddOption
        {
            get { return Options != null && Options.Count < MaxOptions; }
        }

        public bool CanRemoveOption
        {
            get { return Options != null && Options.Count > MinOptions; }
        }

        public bool CanCreate
        {
            get
            {
                if (_isBusy) return false;
                if (string.IsNullOrWhiteSpace(_question)) return false;

                int filled = 0;
                for (int i = 0; i < Options.Count; i++)
                {
                    var o = Options[i];
                    if (o != null && !string.IsNullOrWhiteSpace(o.Text)) filled++;
                }
                if (filled < MinOptions) return false;

                if (_isQuiz && (_correctIndex < 0 || _correctIndex >= Options.Count)) return false;
                return true;
            }
        }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            _peerKey = parameter as string;
            ErrorMessage = null;
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Behaviour ---------------------------------------------------

        private void AddOption()
        {
            if (!CanAddOption) return;
            int next = Options.Count + 1;
            Options.Add(new PollOptionVm
            {
                PlaceholderText = "Option " + next.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

            OnPropertyChanged("CanAddOption");
            OnPropertyChanged("CanRemoveOption");
        }

        private void RemoveOption(PollOptionVm option)
        {
            if (option == null) return;
            if (!CanRemoveOption) return;

            int idx = Options.IndexOf(option);
            if (idx < 0) return;

            Options.RemoveAt(idx);

            if (_correctIndex == idx) CorrectIndex = -1;
            else if (_correctIndex > idx) CorrectIndex = _correctIndex - 1;

            OnPropertyChanged("CanAddOption");
            OnPropertyChanged("CanRemoveOption");
            OnPropertyChanged("CanCreate");
            var cmd = CreateCommand;
            if (cmd != null) cmd.RaiseCanExecuteChanged();
        }

        private async Task CreateAsync()
        {
            if (!CanCreate) return;

            ErrorMessage = null;
            if (_messages == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (string.IsNullOrEmpty(_peerKey))
            {
                ErrorMessage = "No destination peer.";
                return;
            }

            // Project filled options into the PollSpec value object.
            var optionTexts = new List<string>(Options.Count);
            for (int i = 0; i < Options.Count; i++)
            {
                var o = Options[i];
                if (o == null) continue;
                if (string.IsNullOrWhiteSpace(o.Text)) continue;
                optionTexts.Add(o.Text);
            }

            PollSpec spec;
            try
            {
                spec = new PollSpec(_question, optionTexts, _isAnonymous, _multipleAnswers, _isQuiz, _correctIndex);
            }
            catch (ArgumentException ex)
            {
                ErrorMessage = ex.Message;
                return;
            }

            IsBusy = true;
            StatusText = "Creating poll...";

            try
            {
                Result<long, MessageError> result;
                try
                {
                    result = await _messages.SendPollAsync(_peerKey, spec, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.PollPage").Error("SendPollAsync threw: " + ex);
                    ErrorMessage = "Create failed: " + ex.GetType().Name;
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                StatusText = "Poll created";
                if (_nav != null) _nav.GoBack();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string FormatError(MessageError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Code)
            {
                case MessageErrorCode.NetworkFailed:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case MessageErrorCode.FloodWait:
                    return error.Message ?? "Too many attempts.";
                case MessageErrorCode.InvalidArgument:
                    return error.Message ?? "Invalid poll.";
                default:
                    return error.Message ?? error.Code.ToString();
            }
        }
    }
}
