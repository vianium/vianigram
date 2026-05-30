// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Vianigram.App.Services;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed partial class PhotoBubble : UserControl
    {
        private bool _revealSpoilers;
        private bool _hasSpoilers;
        private int _imageLoadVersion;

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(string), typeof(PhotoBubble),
                new PropertyMetadata("", OnImageSourceChanged));

        public static readonly DependencyProperty ImagePathProperty =
            DependencyProperty.Register("ImagePath", typeof(string), typeof(PhotoBubble),
                new PropertyMetadata("", OnImageSourceChanged));

        public static readonly DependencyProperty PreviewBytesProperty =
            DependencyProperty.Register("PreviewBytes", typeof(byte[]), typeof(PhotoBubble),
                new PropertyMetadata(null, OnImageSourceChanged));

        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register("Caption", typeof(string), typeof(PhotoBubble),
                new PropertyMetadata("", OnCaptionChanged));

        public static readonly DependencyProperty CaptionEntitiesProperty =
            DependencyProperty.Register("CaptionEntities", typeof(object), typeof(PhotoBubble),
                new PropertyMetadata(null, OnCaptionChanged));

        public static readonly DependencyProperty IsOutgoingProperty =
            DependencyProperty.Register("IsOutgoing", typeof(bool), typeof(PhotoBubble),
                new PropertyMetadata(false, OnIsOutgoingChanged));

        public static readonly DependencyProperty PhotoWidthProperty =
            DependencyProperty.Register("PhotoWidth", typeof(double), typeof(PhotoBubble),
                new PropertyMetadata(double.NaN, OnSizeChanged));

        public static readonly DependencyProperty PhotoHeightProperty =
            DependencyProperty.Register("PhotoHeight", typeof(double), typeof(PhotoBubble),
                new PropertyMetadata(double.NaN, OnSizeChanged));

        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register("IsLoading", typeof(bool), typeof(PhotoBubble),
                new PropertyMetadata(false, OnLoadingChanged));

        public static readonly DependencyProperty HasFailedProperty =
            DependencyProperty.Register("HasFailed", typeof(bool), typeof(PhotoBubble),
                new PropertyMetadata(false, OnLoadingChanged));

        public static readonly DependencyProperty DownloadProgressProperty =
            DependencyProperty.Register("DownloadProgress", typeof(double), typeof(PhotoBubble),
                new PropertyMetadata(0.0, OnLoadingChanged));

        public static readonly DependencyProperty OpenPathProperty =
            DependencyProperty.Register("OpenPath", typeof(string), typeof(PhotoBubble),
                new PropertyMetadata("", OnLoadingChanged));

        public static readonly DependencyProperty OverlayTextProperty =
            DependencyProperty.Register("OverlayText", typeof(string), typeof(PhotoBubble),
                new PropertyMetadata("", OnOverlayChanged));

        public string ImageSource
        {
            get { return (string)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }

        public string ImagePath
        {
            get { return (string)GetValue(ImagePathProperty); }
            set { SetValue(ImagePathProperty, value); }
        }

        public byte[] PreviewBytes
        {
            get { return (byte[])GetValue(PreviewBytesProperty); }
            set { SetValue(PreviewBytesProperty, value); }
        }

        public string Caption
        {
            get { return (string)GetValue(CaptionProperty); }
            set { SetValue(CaptionProperty, value); }
        }

        public object CaptionEntities
        {
            get { return GetValue(CaptionEntitiesProperty); }
            set { SetValue(CaptionEntitiesProperty, value); }
        }

        public bool IsOutgoing
        {
            get { return (bool)GetValue(IsOutgoingProperty); }
            set { SetValue(IsOutgoingProperty, value); }
        }

        public double PhotoWidth
        {
            get { return (double)GetValue(PhotoWidthProperty); }
            set { SetValue(PhotoWidthProperty, value); }
        }

        public double PhotoHeight
        {
            get { return (double)GetValue(PhotoHeightProperty); }
            set { SetValue(PhotoHeightProperty, value); }
        }

        public bool IsLoading
        {
            get { return (bool)GetValue(IsLoadingProperty); }
            set { SetValue(IsLoadingProperty, value); }
        }

        public bool HasFailed
        {
            get { return (bool)GetValue(HasFailedProperty); }
            set { SetValue(HasFailedProperty, value); }
        }

        public double DownloadProgress
        {
            get { return (double)GetValue(DownloadProgressProperty); }
            set { SetValue(DownloadProgressProperty, value); }
        }

        public string OpenPath
        {
            get { return (string)GetValue(OpenPathProperty); }
            set { SetValue(OpenPathProperty, value); }
        }

        public string OverlayText
        {
            get { return (string)GetValue(OverlayTextProperty); }
            set { SetValue(OverlayTextProperty, value); }
        }

        public event EventHandler DownloadRequested;
        public event EventHandler OpenRequested;
        public event EventHandler ImageLoadFailed;
        public event EventHandler ImageLoaded;

        public PhotoBubble()
        {
            InitializeComponent();
            BubbleInteractionHelpers.EnableTextSelection(CaptionText);
            CaptionText.Tapped += CaptionText_Tapped;
            if (RootGrid != null)
            {
                RootGrid.Holding += OnBubbleHolding;
                RootGrid.RightTapped += OnBubbleRightTapped;
            }
            ApplyImage();
            ApplyCaption();
            ApplyAlignment();
            ApplySize();
            ApplyLoadingState();
            ApplyOverlay();
        }

        private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PhotoBubble b = d as PhotoBubble;
            if (b != null) b.ApplyImage();
        }

        private static void OnCaptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PhotoBubble b = d as PhotoBubble;
            if (b != null) b.ApplyCaption();
        }

        private static void OnIsOutgoingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PhotoBubble b = d as PhotoBubble;
            if (b != null) b.ApplyAlignment();
        }

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PhotoBubble b = d as PhotoBubble;
            if (b != null) b.ApplySize();
        }

        private static void OnLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PhotoBubble b = d as PhotoBubble;
            if (b != null) b.ApplyLoadingState();
        }

        private static void OnOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PhotoBubble b = d as PhotoBubble;
            if (b != null) b.ApplyOverlay();
        }

        private async void ApplyImage()
        {
            if (PhotoImage == null || PlaceholderGrid == null) return;

            int version = ++_imageLoadVersion;
            string src = !string.IsNullOrEmpty(ImageSource) ? ImageSource : ImagePath;
            if (!string.IsNullOrWhiteSpace(src))
            {
                await LoadImageSourceAsync(src, version);
                return;
            }

            byte[] preview = PreviewBytes;
            if (preview != null && preview.Length > 0)
            {
                await LoadPreviewBytesAsync(preview, version);
                return;
            }

            PhotoImage.Source = null;
            PhotoImage.Visibility = Visibility.Collapsed;
            PlaceholderGrid.Visibility = Visibility.Visible;
            IsLoading = false;
            HasFailed = false;
            ApplyLoadingState();
        }

        private async System.Threading.Tasks.Task LoadImageSourceAsync(string source, int version)
        {
            try
            {
                ShowLoadingPlaceholder();

                BitmapImage bitmap = new BitmapImage();
                if (IsFileSystemPath(source))
                {
                    StorageFile file = await StorageFile.GetFileFromPathAsync(source);
                    using (IRandomAccessStream stream = await file.OpenReadAsync())
                    {
                        await bitmap.SetSourceAsync(stream);
                    }
                }
                else
                {
                    Uri uri = CreateUri(source);
                    if (uri == null) throw new InvalidOperationException("Invalid image source.");
                    bitmap.UriSource = uri;
                }

                if (version != _imageLoadVersion) return;
                ShowBitmap(bitmap);
                if (!UsesDeferredBitmapOpen(source))
                    MarkImageLoaded();
            }
            catch
            {
                if (version != _imageLoadVersion) return;
                MarkImageFailed();
            }
        }

        private async System.Threading.Tasks.Task LoadPreviewBytesAsync(byte[] bytes, int version)
        {
            try
            {
                ShowLoadingPlaceholder();

                BitmapImage bitmap = await StrippedThumbExpander.ExpandToBitmapAsync(bytes);
                if (bitmap == null)
                    bitmap = await BitmapFromBytesAsync(bytes);

                if (version != _imageLoadVersion) return;
                if (bitmap == null) throw new InvalidOperationException("Invalid preview bytes.");

                ShowBitmap(bitmap);
                MarkImageLoaded();
            }
            catch
            {
                if (version != _imageLoadVersion) return;
                MarkImageFailed();
            }
        }

        private static async System.Threading.Tasks.Task<BitmapImage> BitmapFromBytesAsync(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            using (var ms = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(ms))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }

                ms.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(ms);
                return bitmap;
            }
        }

        private void ShowLoadingPlaceholder()
        {
            HasFailed = false;
            IsLoading = true;
            PhotoImage.Source = null;
            PhotoImage.Visibility = Visibility.Collapsed;
            PlaceholderGrid.Visibility = Visibility.Visible;
            ApplyLoadingState();
        }

        private void ShowBitmap(BitmapImage bitmap)
        {
            PhotoImage.Source = bitmap;
            PhotoImage.Visibility = Visibility.Visible;
            PlaceholderGrid.Visibility = Visibility.Collapsed;
            ApplyLoadingState();
        }

        private void MarkImageLoaded()
        {
            IsLoading = false;
            HasFailed = false;
            EventHandler loaded = ImageLoaded;
            if (loaded != null) loaded(this, EventArgs.Empty);
        }

        private void MarkImageFailed()
        {
            IsLoading = false;
            HasFailed = true;
            EventHandler failed = ImageLoadFailed;
            if (failed != null) failed(this, EventArgs.Empty);
        }

        private void ApplyCaption()
        {
            if (CaptionText == null) return;

            string caption = Caption;
            if (string.IsNullOrEmpty(caption))
            {
                CaptionText.Text = string.Empty;
                CaptionText.Inlines.Clear();
                CaptionText.Visibility = Visibility.Collapsed;
                return;
            }

            _hasSpoilers = BubbleRichTextRenderer.Render(CaptionText, caption, CaptionEntities, _revealSpoilers);
            CaptionText.Visibility = Visibility.Visible;
        }

        private void ApplyAlignment()
        {
            if (RootGrid != null)
                RootGrid.HorizontalAlignment = IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private void ApplySize()
        {
            double width = double.IsNaN(PhotoWidth) || PhotoWidth <= 0 ? 280 : Math.Min(280, PhotoWidth);
            double height = double.IsNaN(PhotoHeight) || PhotoHeight <= 0 ? 180 : Math.Min(240, PhotoHeight);

            if (PhotoHost != null)
            {
                PhotoHost.Width = width;
                PhotoHost.Height = height;
            }
        }

        private void ApplyLoadingState()
        {
            if (LoadingOverlay == null || FailureOverlay == null) return;

            bool hasProgress = DownloadProgress > 0 && DownloadProgress < 100;
            bool downloading = IsLoading || hasProgress;
            bool needsDownload = string.IsNullOrWhiteSpace(OpenPath);
            bool isPlayable = !string.IsNullOrWhiteSpace(OverlayText);
            LoadingOverlay.Visibility = IsLoading || hasProgress ? Visibility.Visible : Visibility.Collapsed;
            LoadingRing.IsActive = IsLoading && !hasProgress;
            LoadingRing.Visibility = LoadingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
            DownloadProgressBar.Value = Clamp(DownloadProgress, 0, 100);
            DownloadProgressBar.Visibility = hasProgress ? Visibility.Visible : Visibility.Collapsed;
            FailureOverlay.Visibility = HasFailed ? Visibility.Visible : Visibility.Collapsed;

            if (ActionBadge != null && ActionGlyph != null)
            {
                if (HasFailed)
                {
                    ActionBadge.Visibility = Visibility.Visible;
                    ActionGlyph.Text = "\uE72C";
                }
                else if (downloading)
                {
                    ActionBadge.Visibility = Visibility.Visible;
                    ActionGlyph.Text = "\uE711";
                }
                else if (needsDownload)
                {
                    ActionBadge.Visibility = Visibility.Visible;
                    ActionGlyph.Text = "\uE896";
                }
                else if (isPlayable)
                {
                    ActionBadge.Visibility = Visibility.Visible;
                    ActionGlyph.Text = "\uE102";
                }
                else
                {
                    ActionBadge.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ApplyOverlay()
        {
            if (MediaBadge == null || MediaBadgeText == null) return;

            string text = OverlayText ?? string.Empty;
            MediaBadgeText.Text = text;
            MediaBadge.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ApplyLoadingState();
        }

        private void CaptionText_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_hasSpoilers) return;
            _revealSpoilers = !_revealSpoilers;
            ApplyCaption();
        }

        private void OnPhotoTapped(object sender, TappedRoutedEventArgs e)
        {
            if (HasFailed)
            {
                EventHandler download = DownloadRequested;
                if (download != null) download(this, EventArgs.Empty);
                return;
            }

            string openPath = ResolveOpenPath();
            if (!string.IsNullOrEmpty(openPath))
            {
                EventHandler openHandler = OpenRequested;
                if (openHandler != null)
                {
                    openHandler(this, EventArgs.Empty);
                    return;
                }

                OpenMediaAsync(openPath);
                return;
            }

            if (PhotoImage.Source == null)
            {
                EventHandler download = DownloadRequested;
                if (download != null) download(this, EventArgs.Empty);
                return;
            }

            EventHandler open = OpenRequested;
            if (open != null) open(this, EventArgs.Empty);
        }

        private void OnBubbleHolding(object sender, HoldingRoutedEventArgs e)
        {
            if (e == null || e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            if (BubbleInteractionHelpers.IsFrom(CaptionText, e.OriginalSource)) return;

            ShowCopyCaptionMenu();
            e.Handled = true;
        }

        private void OnBubbleRightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ShowCopyCaptionMenu();
            if (e != null) e.Handled = true;
        }

        private void ShowCopyCaptionMenu()
        {
            string caption = Caption ?? string.Empty;
            if (string.IsNullOrWhiteSpace(caption)) return;

            BubbleInteractionHelpers.ShowCopyTextFlyout(
                BubbleBorder != null ? (FrameworkElement)BubbleBorder : this,
                caption,
                "Copy caption");
        }

        private string ResolveOpenPath()
        {
            if (!string.IsNullOrWhiteSpace(OpenPath)) return OpenPath;
            if (!string.IsNullOrWhiteSpace(ImagePath)) return ImagePath;
            if (!string.IsNullOrWhiteSpace(ImageSource)) return ImageSource;
            return string.Empty;
        }

        private async void OpenMediaAsync(string source)
        {
            try
            {
                Frame frame = Window.Current != null ? Window.Current.Content as Frame : null;
                if (frame != null && frame.Navigate(typeof(Vianigram.App.Pages.Media.MediaViewerPage), source))
                    return;

                await LaunchFileAsync(source);
            }
            catch
            {
                MarkImageFailed();
            }
        }

        private static async System.Threading.Tasks.Task LaunchFileAsync(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;

            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri) && !uri.IsFile)
            {
                await Launcher.LaunchUriAsync(uri);
                return;
            }

            string path = uri != null && uri.IsFile ? uri.LocalPath : source;
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            if (file != null) await Launcher.LaunchFileAsync(file);
        }

        private void OnImageOpened(object sender, RoutedEventArgs e)
        {
            MarkImageLoaded();
        }

        private void OnImageFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MarkImageFailed();
        }

        private static Uri CreateUri(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;

            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri)) return uri;
            if (source.Length > 2 && source[1] == ':')
            {
                if (Uri.TryCreate("file:///" + source.Replace('\\', '/'), UriKind.Absolute, out uri))
                    return uri;
            }

            if (Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out uri)) return uri;
            return null;
        }

        private static bool IsFileSystemPath(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (source.Length <= 2 || source[1] != ':') return false;
            return !source.StartsWith("ms-appdata:", StringComparison.OrdinalIgnoreCase)
                && !source.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UsesDeferredBitmapOpen(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            return !IsFileSystemPath(source);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
