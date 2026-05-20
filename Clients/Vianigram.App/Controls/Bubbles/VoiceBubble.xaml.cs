// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace Vianigram.App.Controls.Bubbles
{
    public sealed class VoiceSeekRequestedEventArgs : EventArgs
    {
        public double Progress { get; private set; }
        public int PositionSeconds { get; private set; }

        public VoiceSeekRequestedEventArgs(double progress, int positionSeconds)
        {
            Progress = progress;
            PositionSeconds = positionSeconds;
        }
    }

    public sealed partial class VoiceBubble : UserControl
    {
        private const int WaveformBarCount = 40;
        private const int TelegramWaveformSampleBits = 5;

        private readonly DispatcherTimer _playbackTimer;
        private IRandomAccessStream _audioStream;
        private string _loadedAudioSource;

        public static readonly DependencyProperty AudioSourceProperty =
            DependencyProperty.Register("AudioSource", typeof(string), typeof(VoiceBubble),
                new PropertyMetadata("", OnAudioSourceChanged));

        public static readonly DependencyProperty WaveformDataProperty =
            DependencyProperty.Register("WaveformData", typeof(byte[]), typeof(VoiceBubble),
                new PropertyMetadata(null, OnWaveformChanged));

        public static readonly DependencyProperty DurationSecondsProperty =
            DependencyProperty.Register("DurationSeconds", typeof(int), typeof(VoiceBubble),
                new PropertyMetadata(0, OnDurationChanged));

        public static readonly DependencyProperty ElapsedSecondsProperty =
            DependencyProperty.Register("ElapsedSeconds", typeof(int), typeof(VoiceBubble),
                new PropertyMetadata(0, OnElapsedChanged));

        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register("IsPlaying", typeof(bool), typeof(VoiceBubble),
                new PropertyMetadata(false, OnIsPlayingChanged));

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(VoiceBubble),
                new PropertyMetadata(0.0, OnProgressChanged));

        public static readonly DependencyProperty IsOutgoingProperty =
            DependencyProperty.Register("IsOutgoing", typeof(bool), typeof(VoiceBubble),
                new PropertyMetadata(false, OnIsOutgoingChanged));

        public static readonly DependencyProperty IsDownloadingProperty =
            DependencyProperty.Register("IsDownloading", typeof(bool), typeof(VoiceBubble),
                new PropertyMetadata(false, OnDownloadStateChanged));

        public static readonly DependencyProperty HasFailedProperty =
            DependencyProperty.Register("HasFailed", typeof(bool), typeof(VoiceBubble),
                new PropertyMetadata(false, OnDownloadStateChanged));

        public static readonly DependencyProperty DownloadProgressProperty =
            DependencyProperty.Register("DownloadProgress", typeof(double), typeof(VoiceBubble),
                new PropertyMetadata(0.0, OnDownloadStateChanged));

        public string AudioSource
        {
            get { return (string)GetValue(AudioSourceProperty); }
            set { SetValue(AudioSourceProperty, value); }
        }

        public byte[] WaveformData
        {
            get { return (byte[])GetValue(WaveformDataProperty); }
            set { SetValue(WaveformDataProperty, value); }
        }

        public int DurationSeconds
        {
            get { return (int)GetValue(DurationSecondsProperty); }
            set { SetValue(DurationSecondsProperty, value); }
        }

        public int ElapsedSeconds
        {
            get { return (int)GetValue(ElapsedSecondsProperty); }
            set { SetValue(ElapsedSecondsProperty, value); }
        }

        public bool IsPlaying
        {
            get { return (bool)GetValue(IsPlayingProperty); }
            set { SetValue(IsPlayingProperty, value); }
        }

        public double Progress
        {
            get { return (double)GetValue(ProgressProperty); }
            set { SetValue(ProgressProperty, value); }
        }

        public bool IsOutgoing
        {
            get { return (bool)GetValue(IsOutgoingProperty); }
            set { SetValue(IsOutgoingProperty, value); }
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

        public double DownloadProgress
        {
            get { return (double)GetValue(DownloadProgressProperty); }
            set { SetValue(DownloadProgressProperty, value); }
        }

        public event EventHandler PlayRequested;
        public event EventHandler PauseRequested;
        public event EventHandler DownloadRequested;
        public event EventHandler CancelRequested;
        public event EventHandler<VoiceSeekRequestedEventArgs> SeekRequested;

        public VoiceBubble()
        {
            InitializeComponent();
            _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _playbackTimer.Tick += OnPlaybackTimerTick;
            Unloaded += OnUnloaded;
            BuildWaveform();
            ApplyDuration();
            ApplyPlaying();
            ApplyProgress();
            ApplyAlignment();
        }

        private static void OnAudioSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null)
            {
                b.ResetLoadedAudio();
                b.ApplyPlaying();
            }
        }

        private static void OnWaveformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null)
            {
                b.BuildWaveform();
                b.ApplyProgress();
            }
        }

        private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null)
            {
                b.ApplyDuration();
                b.ApplyProgress();
            }
        }

        private static void OnElapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null)
            {
                int duration = b.DurationSeconds;
                if (duration > 0) b.Progress = Clamp(b.ElapsedSeconds / (double)duration, 0, 1);
                b.ApplyDuration();
            }
        }

        private static void OnIsPlayingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null) b.ApplyPlaying();
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null) b.ApplyProgress();
        }

        private static void OnIsOutgoingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null) b.ApplyAlignment();
        }

        private static void OnDownloadStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VoiceBubble b = d as VoiceBubble;
            if (b != null) b.ApplyPlaying();
        }

        private void BuildWaveform()
        {
            if (WaveformPanel == null) return;

            WaveformPanel.Children.Clear();
            int[] samples = BuildWaveformSamples(WaveformData);
            for (int i = 0; i < samples.Length; i++)
            {
                double height = 4 + (samples[i] / 31.0) * 24;
                Rectangle rect = new Rectangle
                {
                    Width = 3,
                    Height = height,
                    Margin = new Thickness(1, 0, 1, 0),
                    RadiusX = 1.5,
                    RadiusY = 1.5,
                    Fill = IdleBrush(),
                    VerticalAlignment = VerticalAlignment.Center
                };
                WaveformPanel.Children.Add(rect);
            }
        }

        private static int[] BuildWaveformSamples(byte[] data)
        {
            if (data == null || data.Length == 0)
                return NeutralWaveform(WaveformBarCount);

            int[] raw = LooksLikeUnpackedWaveform(data)
                ? CopyUnpackedWaveform(data)
                : UnpackTelegramWaveform(data);

            if (raw.Length == 0)
                return NeutralWaveform(WaveformBarCount);

            return ResampleWaveform(raw, WaveformBarCount);
        }

        private static bool LooksLikeUnpackedWaveform(byte[] data)
        {
            if (data == null || data.Length < WaveformBarCount / 2) return false;

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] > 31) return false;
            }

            return true;
        }

        private static int[] CopyUnpackedWaveform(byte[] data)
        {
            int[] values = new int[data.Length];
            for (int i = 0; i < data.Length; i++)
                values[i] = ClampToInt(data[i], 0, 31);
            return values;
        }

        private static int[] UnpackTelegramWaveform(byte[] data)
        {
            var values = new List<int>();
            int bitOffset = 0;
            int totalBits = data.Length * 8;

            while (bitOffset + TelegramWaveformSampleBits <= totalBits && values.Count < 160)
            {
                int byteIndex = bitOffset >> 3;
                int shift = bitOffset & 7;
                int bitsFromFirstByte = 8 - shift;
                if (bitsFromFirstByte > TelegramWaveformSampleBits)
                    bitsFromFirstByte = TelegramWaveformSampleBits;

                int firstMask = (1 << bitsFromFirstByte) - 1;
                int value = (data[byteIndex] >> shift) & firstMask;
                int nextByteRest = TelegramWaveformSampleBits - bitsFromFirstByte;

                if (nextByteRest > 0 && byteIndex + 1 < data.Length)
                {
                    value <<= nextByteRest;
                    value |= data[byteIndex + 1] & ((1 << nextByteRest) - 1);
                }

                values.Add(ClampToInt(value, 0, 31));
                bitOffset += TelegramWaveformSampleBits;
            }

            return values.ToArray();
        }

        private static int[] ResampleWaveform(int[] values, int targetCount)
        {
            if (values == null || values.Length == 0)
                return NeutralWaveform(targetCount);

            int[] result = new int[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                double start = (i * values.Length) / (double)targetCount;
                double end = ((i + 1) * values.Length) / (double)targetCount;
                int startIndex = (int)Math.Floor(start);
                int endIndex = (int)Math.Ceiling(end);
                int peak = 0;

                if (endIndex <= startIndex) endIndex = startIndex + 1;
                for (int j = startIndex; j < endIndex && j < values.Length; j++)
                    peak = Math.Max(peak, ClampToInt(values[j], 0, 31));

                result[i] = peak;
            }

            return result;
        }

        private static int[] NeutralWaveform(int count)
        {
            int[] values = new int[count];
            for (int i = 0; i < values.Length; i++) values[i] = 8;
            return values;
        }

        private void ApplyDuration()
        {
            if (DurationText == null) return;
            int seconds = ElapsedSeconds > 0 ? ElapsedSeconds : DurationSeconds;
            DurationText.Text = FormatDuration(seconds);
        }

        private void ApplyPlaying()
        {
            if (PlayPauseIcon == null) return;

            bool hasAudioSource = HasAudioSource();
            bool downloading = IsDownloading || (!hasAudioSource && DownloadProgress > 0 && DownloadProgress < 100);
            double progress = Clamp(DownloadProgress, 0, 100);

            if (DownloadRing != null)
            {
                DownloadRing.IsActive = downloading && progress <= 0;
                DownloadRing.Visibility = DownloadRing.IsActive ? Visibility.Visible : Visibility.Collapsed;
            }

            if (DownloadProgressBar != null)
            {
                DownloadProgressBar.Value = progress;
                DownloadProgressBar.Visibility = downloading && progress > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (HasFailed)
            {
                PlayPauseIcon.Text = "\uE72C";
            }
            else if (downloading)
            {
                PlayPauseIcon.Text = "\uE711";
            }
            else if (!hasAudioSource)
            {
                PlayPauseIcon.Text = "\uE896";
            }
            else
            {
                PlayPauseIcon.Text = IsPlaying ? "\uE103" : "\uE102";
            }

            if (PlayPauseButton != null)
                PlayPauseButton.Opacity = hasAudioSource || HasFailed || downloading ? 1.0 : 0.82;
            if (WaveformHitTarget != null)
                WaveformHitTarget.Opacity = hasAudioSource ? 1.0 : 0.72;
        }

        private void ApplyProgress()
        {
            if (WaveformPanel == null) return;

            int total = WaveformPanel.Children.Count;
            if (total == 0) return;

            double p = Clamp(Progress, 0, 1);
            int filled = (int)Math.Round(total * p);
            Brush accent = AccentBrush();
            Brush idle = IdleBrush();
            for (int i = 0; i < total; i++)
            {
                Rectangle rect = WaveformPanel.Children[i] as Rectangle;
                if (rect != null) rect.Fill = i < filled ? accent : idle;
            }
        }

        private void ApplyAlignment()
        {
            if (RootGrid != null)
                RootGrid.HorizontalAlignment = IsOutgoing ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        private async void OnPlayPauseClicked(object sender, RoutedEventArgs e)
        {
            if (IsDownloading)
            {
                EventHandler cancel = CancelRequested;
                if (cancel != null) cancel(this, EventArgs.Empty);
                return;
            }

            if (!HasAudioSource())
            {
                EventHandler download = DownloadRequested;
                if (download != null)
                {
                    IsDownloading = true;
                    download(this, EventArgs.Empty);
                }
                return;
            }

            if (IsPlaying)
            {
                PausePlayback();
                return;
            }

            await StartPlaybackAsync();
        }

        private void OnWaveformTapped(object sender, TappedRoutedEventArgs e)
        {
            if (WaveformHitTarget == null) return;

            if (!HasAudioSource())
            {
                EventHandler download = DownloadRequested;
                if (download != null)
                {
                    IsDownloading = true;
                    download(this, EventArgs.Empty);
                }
                return;
            }

            Point point = e.GetPosition(WaveformHitTarget);
            double width = WaveformHitTarget.ActualWidth;
            if (width <= 0) return;

            double progress = Clamp(point.X / width, 0, 1);
            Progress = progress;
            int duration = GetPlaybackDurationSeconds();
            ElapsedSeconds = duration > 0 ? (int)Math.Round(duration * progress) : 0;
            if (AudioPlayer != null && duration > 0)
            {
                try { AudioPlayer.Position = TimeSpan.FromSeconds(ElapsedSeconds); }
                catch { }
            }

            EventHandler<VoiceSeekRequestedEventArgs> handler = SeekRequested;
            if (handler != null)
                handler(this, new VoiceSeekRequestedEventArgs(progress, ElapsedSeconds));
        }

        private async Task StartPlaybackAsync()
        {
            try
            {
                HasFailed = false;
                await EnsureAudioLoadedAsync();
                if (AudioPlayer == null) return;

                AudioPlayer.Play();
                IsPlaying = true;
                _playbackTimer.Start();

                EventHandler handler = PlayRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            catch
            {
                SetPlaybackFailed();
            }
        }

        private void PausePlayback()
        {
            if (AudioPlayer != null) AudioPlayer.Pause();
            IsPlaying = false;
            _playbackTimer.Stop();

            EventHandler handler = PauseRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private async Task EnsureAudioLoadedAsync()
        {
            string source = AudioSource ?? string.Empty;
            if (AudioPlayer == null || string.IsNullOrWhiteSpace(source)) return;
            if (string.Equals(_loadedAudioSource, source, StringComparison.OrdinalIgnoreCase)) return;

            ResetLoadedAudio();

            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri) && !uri.IsFile)
            {
                AudioPlayer.Source = uri;
            }
            else
            {
                string path = uri != null && uri.IsFile ? uri.LocalPath : source;
                StorageFile file = await StorageFile.GetFileFromPathAsync(path);
                _audioStream = await file.OpenAsync(FileAccessMode.Read);
                string contentType = string.IsNullOrEmpty(file.ContentType) ? "audio/ogg" : file.ContentType;
                AudioPlayer.SetSource(_audioStream, contentType);
            }

            _loadedAudioSource = source;
        }

        private void OnPlaybackTimerTick(object sender, object e)
        {
            UpdatePlaybackPosition();
        }

        private void OnAudioOpened(object sender, RoutedEventArgs e)
        {
            if (DurationSeconds <= 0 && AudioPlayer != null && AudioPlayer.NaturalDuration.HasTimeSpan)
                DurationSeconds = (int)Math.Round(AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds);
            UpdatePlaybackPosition();
        }

        private void OnAudioEnded(object sender, RoutedEventArgs e)
        {
            _playbackTimer.Stop();
            IsPlaying = false;
            Progress = 1.0;
            ElapsedSeconds = GetPlaybackDurationSeconds();
        }

        private void OnAudioFailed(object sender, ExceptionRoutedEventArgs e)
        {
            SetPlaybackFailed();
        }

        private void OnAudioCurrentStateChanged(object sender, RoutedEventArgs e)
        {
            if (AudioPlayer == null) return;

            if (AudioPlayer.CurrentState == MediaElementState.Playing)
            {
                if (!IsPlaying) IsPlaying = true;
                _playbackTimer.Start();
            }
            else if (AudioPlayer.CurrentState == MediaElementState.Paused ||
                     AudioPlayer.CurrentState == MediaElementState.Stopped)
            {
                if (IsPlaying) IsPlaying = false;
                _playbackTimer.Stop();
            }
        }

        private void UpdatePlaybackPosition()
        {
            if (AudioPlayer == null) return;

            int seconds = (int)Math.Round(AudioPlayer.Position.TotalSeconds);
            if (seconds < 0) seconds = 0;
            ElapsedSeconds = seconds;

            int duration = GetPlaybackDurationSeconds();

            if (duration > 0)
                Progress = Clamp(seconds / (double)duration, 0, 1);
        }

        private int GetPlaybackDurationSeconds()
        {
            int duration = DurationSeconds;
            if (duration <= 0 && AudioPlayer != null && AudioPlayer.NaturalDuration.HasTimeSpan)
                duration = (int)Math.Round(AudioPlayer.NaturalDuration.TimeSpan.TotalSeconds);
            return duration < 0 ? 0 : duration;
        }

        private void SetPlaybackFailed()
        {
            _playbackTimer.Stop();
            IsPlaying = false;
            HasFailed = true;
            ApplyPlaying();
        }

        private void ResetLoadedAudio()
        {
            _playbackTimer.Stop();
            if (AudioPlayer != null)
            {
                try { AudioPlayer.Stop(); }
                catch { }
                AudioPlayer.Source = null;
            }

            if (_audioStream != null)
            {
                _audioStream.Dispose();
                _audioStream = null;
            }

            _loadedAudioSource = null;
            IsPlaying = false;
            ElapsedSeconds = 0;
            Progress = 0;
            HasFailed = false;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ResetLoadedAudio();
        }

        private static string FormatDuration(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return minutes.ToString() + ":" + (seconds < 10 ? "0" + seconds.ToString() : seconds.ToString());
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int ClampToInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private bool HasAudioSource()
        {
            return !string.IsNullOrWhiteSpace(AudioSource);
        }

        private static Brush AccentBrush()
        {
            return new SolidColorBrush(Color.FromArgb(255, 83, 189, 235));
        }

        private static Brush IdleBrush()
        {
            return new SolidColorBrush(Color.FromArgb(120, 153, 153, 153));
        }
    }
}
