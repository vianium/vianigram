// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed partial class DocumentBubble : UserControl
    {
        private bool _revealSpoilers;
        private bool _hasSpoilers;

        public static readonly DependencyProperty FileNameProperty =
            DependencyProperty.Register("FileName", typeof(string), typeof(DocumentBubble),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty FileSizeProperty =
            DependencyProperty.Register("FileSize", typeof(string), typeof(DocumentBubble),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty MimeTypeProperty =
            DependencyProperty.Register("MimeType", typeof(string), typeof(DocumentBubble),
                new PropertyMetadata("", OnMimeChanged));

        public static readonly DependencyProperty DownloadProgressProperty =
            DependencyProperty.Register("DownloadProgress", typeof(double), typeof(DocumentBubble),
                new PropertyMetadata(0.0, OnProgressChanged));

        public static readonly DependencyProperty DownloadedBytesProperty =
            DependencyProperty.Register("DownloadedBytes", typeof(long), typeof(DocumentBubble),
                new PropertyMetadata(0L, OnProgressChanged));

        public static readonly DependencyProperty TotalBytesProperty =
            DependencyProperty.Register("TotalBytes", typeof(long), typeof(DocumentBubble),
                new PropertyMetadata(0L, OnProgressChanged));

        public static readonly DependencyProperty IsDownloadedProperty =
            DependencyProperty.Register("IsDownloaded", typeof(bool), typeof(DocumentBubble),
                new PropertyMetadata(false, OnStateChanged));

        public static readonly DependencyProperty IsDownloadingProperty =
            DependencyProperty.Register("IsDownloading", typeof(bool), typeof(DocumentBubble),
                new PropertyMetadata(false, OnStateChanged));

        public static readonly DependencyProperty HasFailedProperty =
            DependencyProperty.Register("HasFailed", typeof(bool), typeof(DocumentBubble),
                new PropertyMetadata(false, OnStateChanged));

        public static readonly DependencyProperty FilePathProperty =
            DependencyProperty.Register("FilePath", typeof(string), typeof(DocumentBubble),
                new PropertyMetadata(""));

        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register("Caption", typeof(string), typeof(DocumentBubble),
                new PropertyMetadata("", OnCaptionChanged));

        public static readonly DependencyProperty CaptionEntitiesProperty =
            DependencyProperty.Register("CaptionEntities", typeof(object), typeof(DocumentBubble),
                new PropertyMetadata(null, OnCaptionChanged));

        public static readonly DependencyProperty IsOutgoingProperty =
            DependencyProperty.Register("IsOutgoing", typeof(bool), typeof(DocumentBubble),
                new PropertyMetadata(false, OnIsOutgoingChanged));

        public string FileName
        {
            get { return (string)GetValue(FileNameProperty); }
            set { SetValue(FileNameProperty, value); }
        }

        public string FileSize
        {
            get { return (string)GetValue(FileSizeProperty); }
            set { SetValue(FileSizeProperty, value); }
        }

        public string MimeType
        {
            get { return (string)GetValue(MimeTypeProperty); }
            set { SetValue(MimeTypeProperty, value); }
        }

        public double DownloadProgress
        {
            get { return (double)GetValue(DownloadProgressProperty); }
            set { SetValue(DownloadProgressProperty, value); }
        }

        public long DownloadedBytes
        {
            get { return (long)GetValue(DownloadedBytesProperty); }
            set { SetValue(DownloadedBytesProperty, value); }
        }

        public long TotalBytes
        {
            get { return (long)GetValue(TotalBytesProperty); }
            set { SetValue(TotalBytesProperty, value); }
        }

        public bool IsDownloaded
        {
            get { return (bool)GetValue(IsDownloadedProperty); }
            set { SetValue(IsDownloadedProperty, value); }
        }

        public bool IsDownloading
        {
            get { return (bool)GetValue(IsDownloadingProperty); }
            set { SetValue(IsDownloadingProperty, value); }
        }

        public bool HasFailed
        {
            get { return (bool)GetValue(HasFailedProperty); }
            set { SetValue(HasFailedProperty, value); }
        }

        public string FilePath
        {
            get { return (string)GetValue(FilePathProperty); }
            set { SetValue(FilePathProperty, value); }
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

        public event EventHandler DownloadRequested;
        public event EventHandler CancelRequested;
        public event EventHandler OpenRequested;

        public DocumentBubble()
        {
            InitializeComponent();
            CaptionText.Tapped += CaptionText_Tapped;
            ApplyText();
            ApplyMime();
            ApplyState();
            ApplyCaption();
            ApplyAlignment();
        }

        public void SetDownloading(bool isDownloading)
        {
            IsDownloading = isDownloading;
            HasFailed = false;
        }

        public void SetDownloadProgress(long downloadedBytes, long totalBytes)
        {
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            DownloadProgress = totalBytes > 0 ? Clamp((downloadedBytes * 100.0) / totalBytes, 0, 100) : 0;
            IsDownloading = true;
            HasFailed = false;
        }

        public void SetCanceling()
        {
            IsDownloading = true;
            if (DownloadRing != null)
            {
                DownloadRing.Visibility = Visibility.Visible;
                DownloadRing.IsActive = true;
            }
            if (FileSizeText != null) FileSizeText.Text = "Canceling...";
        }

        public void SetCanceled()
        {
            IsDownloading = false;
            IsDownloaded = false;
            HasFailed = false;
            DownloadProgress = 0;
        }

        public void SetFailed()
        {
            IsDownloading = false;
            IsDownloaded = false;
            HasFailed = true;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DocumentBubble b = d as DocumentBubble;
            if (b != null) b.ApplyText();
        }

        private static void OnMimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DocumentBubble b = d as DocumentBubble;
            if (b != null) b.ApplyMime();
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DocumentBubble b = d as DocumentBubble;
            if (b != null) b.ApplyState();
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DocumentBubble b = d as DocumentBubble;
            if (b != null) b.ApplyState();
        }

        private static void OnCaptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DocumentBubble b = d as DocumentBubble;
            if (b != null) b.ApplyCaption();
        }

        private static void OnIsOutgoingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            DocumentBubble b = d as DocumentBubble;
            if (b != null) b.ApplyAlignment();
        }

        private void ApplyText()
        {
            if (FileNameText != null) FileNameText.Text = string.IsNullOrEmpty(FileName) ? "Document" : FileName;
            ApplyState();
        }

        private void ApplyMime()
        {
            if (FileGlyph == null) return;

            string mime = MimeType ?? string.Empty;
            if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) FileGlyph.Text = "\uE8B9";
            else if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) FileGlyph.Text = "\uE714";
            else if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) FileGlyph.Text = "\uE8D6";
            else if (mime.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0) FileGlyph.Text = "\uE8A5";
            else if (mime.IndexOf("zip", StringComparison.OrdinalIgnoreCase) >= 0) FileGlyph.Text = "\uF012";
            else FileGlyph.Text = "\uE160";
        }

        private void ApplyState()
        {
            if (DownloadProgressBar == null || DownloadRing == null || ActionGlyph == null) return;

            bool downloading = IsDownloading || (!IsDownloaded && DownloadProgress > 0 && DownloadProgress < 100);
            double progress = Clamp(DownloadProgress, 0, 100);
            if (TotalBytes > 0 && DownloadedBytes > 0)
                progress = Clamp((DownloadedBytes * 100.0) / TotalBytes, 0, 100);

            DownloadProgressBar.Value = progress;
            DownloadProgressBar.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            DownloadRing.IsActive = downloading && progress <= 0;
            DownloadRing.Visibility = DownloadRing.IsActive ? Visibility.Visible : Visibility.Collapsed;

            if (FileGlyph != null) FileGlyph.Visibility = IsDownloaded ? Visibility.Visible : Visibility.Collapsed;
            if (DownloadGlyph != null) DownloadGlyph.Visibility = !IsDownloaded && !downloading ? Visibility.Visible : Visibility.Collapsed;
            if (CancelGlyph != null) CancelGlyph.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;

            if (HasFailed)
            {
                ActionGlyph.Text = "\uE72C";
                if (FileSizeText != null) FileSizeText.Text = "Failed - tap to retry";
            }
            else if (downloading)
            {
                ActionGlyph.Text = "\uE711";
                if (FileSizeText != null)
                    FileSizeText.Text = FormatProgress(progress);
            }
            else if (IsDownloaded)
            {
                ActionGlyph.Text = "\uE8A7";
                if (FileSizeText != null) FileSizeText.Text = FileSize ?? string.Empty;
            }
            else
            {
                ActionGlyph.Text = "\uE896";
                if (FileSizeText != null) FileSizeText.Text = FileSize ?? string.Empty;
            }
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

        private void CaptionText_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_hasSpoilers) return;
            _revealSpoilers = !_revealSpoilers;
            ApplyCaption();
        }

        private void OnActionButtonClick(object sender, RoutedEventArgs e)
        {
            HandleAction();
        }

        private void OnDocumentTapped(object sender, TappedRoutedEventArgs e)
        {
            HandleAction();
        }

        private void HandleAction()
        {
            if (IsDownloading)
            {
                EventHandler cancel = CancelRequested;
                if (cancel != null)
                {
                    SetCanceling();
                    cancel(this, EventArgs.Empty);
                }
                return;
            }

            if (!IsDownloaded)
            {
                EventHandler download = DownloadRequested;
                if (download != null)
                {
                    SetDownloading(true);
                    download(this, EventArgs.Empty);
                }
                return;
            }

            EventHandler open = OpenRequested;
            if (open != null)
            {
                open(this, EventArgs.Empty);
                return;
            }

            LaunchFileAsync();
        }

        private async void LaunchFileAsync()
        {
            if (string.IsNullOrEmpty(FilePath)) return;

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(FilePath);
                if (file != null) await Launcher.LaunchFileAsync(file);
            }
            catch
            {
                SetFailed();
            }
        }

        private string FormatProgress(double progress)
        {
            if (TotalBytes > 0)
                return "Downloading " + ((int)progress).ToString() + "% - " +
                    FormatBytes(DownloadedBytes) + " / " + FormatBytes(TotalBytes);

            return progress > 0 ? "Downloading " + ((int)progress).ToString() + "%" : "Downloading...";
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes >= 1024L * 1024L) return (bytes / (1024.0 * 1024.0)).ToString("0.#") + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.#") + " KB";
            return bytes.ToString() + " B";
        }
    }
}
