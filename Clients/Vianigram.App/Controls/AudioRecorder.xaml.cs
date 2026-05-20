// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Windows.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed partial class AudioRecorder : UserControl
    {
        public static readonly DependencyProperty IsRecordingProperty =
            DependencyProperty.Register("IsRecording", typeof(bool), typeof(AudioRecorder),
                new PropertyMetadata(false, OnIsRecordingChanged));

        public static readonly DependencyProperty RecordedSecondsProperty =
            DependencyProperty.Register("RecordedSeconds", typeof(int), typeof(AudioRecorder),
                new PropertyMetadata(0, OnSecondsChanged));

        public static readonly DependencyProperty OnStopProperty =
            DependencyProperty.Register("OnStop", typeof(ICommand), typeof(AudioRecorder),
                new PropertyMetadata(null));

        public static readonly DependencyProperty OnCancelProperty =
            DependencyProperty.Register("OnCancel", typeof(ICommand), typeof(AudioRecorder),
                new PropertyMetadata(null));

        public bool IsRecording
        {
            get { return (bool)GetValue(IsRecordingProperty); }
            set { SetValue(IsRecordingProperty, value); }
        }

        public int RecordedSeconds
        {
            get { return (int)GetValue(RecordedSecondsProperty); }
            set { SetValue(RecordedSecondsProperty, value); }
        }

        public ICommand OnStop
        {
            get { return (ICommand)GetValue(OnStopProperty); }
            set { SetValue(OnStopProperty, value); }
        }

        public ICommand OnCancel
        {
            get { return (ICommand)GetValue(OnCancelProperty); }
            set { SetValue(OnCancelProperty, value); }
        }

        public AudioRecorder()
        {
            this.InitializeComponent();
            ApplyRecording();
            ApplySeconds();
        }

        private static void OnIsRecordingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var r = d as AudioRecorder;
            if (r != null) r.ApplyRecording();
        }

        private static void OnSecondsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var r = d as AudioRecorder;
            if (r != null) r.ApplySeconds();
        }

        private void ApplyRecording()
        {
            if (RecordingPanel == null) return;
            RecordingPanel.Visibility = IsRecording ? Visibility.Visible : Visibility.Collapsed;
            if (PulseStoryboard != null)
            {
                if (IsRecording) PulseStoryboard.Begin();
                else PulseStoryboard.Stop();
            }
        }

        private void ApplySeconds()
        {
            if (TimerText == null) return;
            int s = RecordedSeconds;
            if (s < 0) s = 0;
            int m = s / 60;
            int sec = s % 60;
            TimerText.Text = m.ToString() + ":" + (sec < 10 ? "0" + sec.ToString() : sec.ToString());
        }
    }
}
