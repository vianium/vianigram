// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MediaViewerPageViewModel.cs
//
// Drives MediaViewerPage: full-screen image / video viewer. Holds the
// media URI plus header / footer state and exposes Save / Forward / Share
// / Close commands. Forward and Close drive INavigationService directly;
// Save / Share remain VM-local (filesystem + DataTransferManager).

using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.ViewModels;
using Vianigram.Media.Ports.Inbound;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class MediaViewerPageViewModel : ObservableObject
    {
        private readonly IMediaApi _media;
        private readonly INavigationService _nav;

        private BitmapImage _source;
        private Uri _mediaUri;
        private bool _isVideo;
        private string _senderName;
        private string _dateText;
        private string _caption;
        private string _statusText;
        private string _errorMessage;
        private bool _isBusy;
        private string _filePath;
        private string _sourcePeerKey;
        private long _sourceMessageId;

        // Design-time / degraded-mode ctor — delegates to the DI ctor with nulls.
        public MediaViewerPageViewModel() : this(null, null)
        {
        }

        public MediaViewerPageViewModel(IMediaApi media, INavigationService nav)
        {
            // Null-tolerant: composition may have failed; commands surface the
            // condition through ErrorMessage on first invocation.
            _media = media;
            _nav = nav;

            CloseCommand = new RelayCommand(_ => OnClose(), _ => true);
            SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => !_isBusy && _filePath != null);
            ForwardCommand = new RelayCommand(_ => OnForward(), _ => !_isBusy && _filePath != null);
            ShareCommand = new AsyncCommand(_ => ShareAsync(), _ => !_isBusy && _filePath != null);
        }

        // ---- Commands ----------------------------------------------------

        public ICommand CloseCommand { get; private set; }
        public AsyncCommand SaveCommand { get; private set; }
        public ICommand ForwardCommand { get; private set; }
        public AsyncCommand ShareCommand { get; private set; }

        // ---- Properties --------------------------------------------------

        public BitmapImage Source
        {
            get { return _source; }
            private set { SetProperty(ref _source, value); }
        }

        public Uri MediaUri
        {
            get { return _mediaUri; }
            private set { SetProperty(ref _mediaUri, value); }
        }

        public bool IsVideo
        {
            get { return _isVideo; }
            private set
            {
                if (SetProperty(ref _isVideo, value))
                    OnPropertyChanged("IsImage");
            }
        }

        public bool IsImage
        {
            get { return !_isVideo; }
        }

        public string SenderName
        {
            get { return _senderName; }
            set { SetProperty(ref _senderName, value); }
        }

        public string DateText
        {
            get { return _dateText; }
            set { SetProperty(ref _dateText, value); }
        }

        public string Caption
        {
            get { return _caption; }
            set
            {
                if (SetProperty(ref _caption, value))
                    OnPropertyChanged("HasCaption");
            }
        }

        public bool HasCaption
        {
            get { return !string.IsNullOrEmpty(_caption); }
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

        public string SourcePeerKey
        {
            get { return _sourcePeerKey; }
            set { SetProperty(ref _sourcePeerKey, value); }
        }

        public long SourceMessageId
        {
            get { return _sourceMessageId; }
            set { SetProperty(ref _sourceMessageId, value); }
        }

        // ---- Lifecycle ---------------------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            // The parameter is a string path/URI today. When
            // IMediaApi.DownloadAsync is wired it will become a structured
            // payload carrying FileLocation + caption metadata.
            string path = parameter as string;
            if (!string.IsNullOrEmpty(path)) SetUri(path);
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Behaviour ---------------------------------------------------

        public async void SetUri(string pathOrUri)
        {
            if (string.IsNullOrEmpty(pathOrUri)) return;
            _filePath = pathOrUri;
            ErrorMessage = null;

            bool video = IsVideoPath(pathOrUri);
            IsVideo = video;
            if (video) Source = null;

            try
            {
                if (pathOrUri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || pathOrUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(pathOrUri, UriKind.Absolute);
                    MediaUri = uri;
                    if (!video) Source = new BitmapImage(uri);
                    return;
                }

                if (pathOrUri.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase)
                    || pathOrUri.StartsWith("ms-appdata:", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(pathOrUri, UriKind.RelativeOrAbsolute);
                    MediaUri = uri;
                    if (!video) Source = new BitmapImage(uri);
                    return;
                }

                StorageFile file = await StorageFile.GetFileFromPathAsync(pathOrUri).AsTask().ConfigureAwait(true);
                if (video)
                {
                    MediaUri = new Uri(pathOrUri, UriKind.Absolute);
                    return;
                }

                var bmp = new BitmapImage();
                using (IRandomAccessStream stream = await file.OpenReadAsync().AsTask().ConfigureAwait(true))
                {
                    await bmp.SetSourceAsync(stream).AsTask().ConfigureAwait(true);
                }
                Source = bmp;
            }
            catch (Exception ex)
            {
                ErrorMessage = "Failed to open media: " + ex.GetType().Name;
            }
        }

        private static bool IsVideoPath(string pathOrUri)
        {
            if (string.IsNullOrWhiteSpace(pathOrUri)) return false;

            string lower = pathOrUri.ToLowerInvariant();
            return lower.EndsWith(".mp4") ||
                   lower.EndsWith(".m4v") ||
                   lower.EndsWith(".mov") ||
                   lower.EndsWith(".avi") ||
                   lower.EndsWith(".wmv") ||
                   lower.EndsWith(".3gp") ||
                   lower.EndsWith(".3g2") ||
                   lower.EndsWith(".mkv") ||
                   lower.EndsWith(".webm");
        }

        // ---- Command handlers --------------------------------------------

        private void OnClose()
        {
            if (_nav == null) return;
            _nav.GoBack();
        }

        private void OnForward()
        {
            if (_nav == null)
            {
                ErrorMessage = "Navigation service not available";
                return;
            }
            // Hand the source peer + message id to ForwardPage; once Forward
            // accepts media references the payload becomes a richer object.
            object parameter = new ForwardNavigationArgs(_sourcePeerKey, _sourceMessageId, _filePath);
            _nav.NavigateTo(Route.Forward, parameter);
        }

        private async Task SaveAsync()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            IsBusy = true;
            StatusText = "Saving...";
            try
            {
                StorageFile source = await StorageFile.GetFileFromPathAsync(_filePath).AsTask().ConfigureAwait(true);
                StorageFolder pictures = KnownFolders.SavedPictures;
                await source.CopyAsync(pictures, source.Name, NameCollisionOption.GenerateUniqueName)
                    .AsTask().ConfigureAwait(true);
                StatusText = "Saved to Saved Pictures";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Save failed: " + ex.GetType().Name;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ShareAsync()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            IsBusy = true;
            StatusText = "Sharing...";
            try
            {
                // DataTransferManager.ShowShareUI() is invoked by the page —
                // staying here keeps the VM ICommand bindable. The page hooks
                // PropertyChanged for StatusText to know when to fire the UI.
                // NOTE: Windows.ApplicationModel.DataTransfer is a platform API,
                // not a port — keep page-local.
                StatusText = "Share sheet";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Share failed: " + ex.GetType().Name;
            }
            finally
            {
                IsBusy = false;
            }
            await Task.FromResult<object>(null).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Strongly-typed nav payload handed to ForwardPage when MediaViewer's
    /// Forward button fires. Public + sealed so .NET Native (Release) can
    /// reflect it.
    /// </summary>
    public sealed class ForwardNavigationArgs
    {
        public ForwardNavigationArgs(string sourcePeerKey, long sourceMessageId, string filePath)
        {
            SourcePeerKey = sourcePeerKey ?? string.Empty;
            SourceMessageId = sourceMessageId;
            FilePath = filePath ?? string.Empty;
        }

        public string SourcePeerKey { get; private set; }
        public long SourceMessageId { get; private set; }
        public string FilePath { get; private set; }
    }
}
