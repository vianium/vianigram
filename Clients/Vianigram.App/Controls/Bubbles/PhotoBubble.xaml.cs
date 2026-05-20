// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.Storage;
using Windows.Storage.Streams;
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

        public event EventHandler DownloadRequested;
        public event EventHandler OpenRequested;
        public event EventHandler ImageLoadFailed;
        public event EventHandler ImageLoaded;

        public PhotoBubble()
        {
            InitializeComponent();
            CaptionText.Tapped += CaptionText_Tapped;
            ApplyImage();
            ApplyCaption();
            ApplyAlignment();
            ApplySize();
            ApplyLoadingState();
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
            LoadingOverlay.Visibility = IsLoading || hasProgress ? Visibility.Visible : Visibility.Collapsed;
            LoadingRing.IsActive = IsLoading && !hasProgress;
            LoadingRing.Visibility = LoadingRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
            DownloadProgressBar.Value = Clamp(DownloadProgress, 0, 100);
            DownloadProgressBar.Visibility = hasProgress ? Visibility.Visible : Visibility.Collapsed;
            FailureOverlay.Visibility = HasFailed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CaptionText_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_hasSpoilers) return;
            _revealSpoilers = !_revealSpoilers;
            ApplyCaption();
        }

        private void OnPhotoTapped(object sender, TappedRoutedEventArgs e)
        {
            if (HasFailed || PhotoImage.Source == null)
            {
                EventHandler download = DownloadRequested;
                if (download != null) download(this, EventArgs.Empty);
                return;
            }

            EventHandler open = OpenRequested;
            if (open != null) open(this, EventArgs.Empty);
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
