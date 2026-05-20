// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Windows.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed partial class ReplyBar : UserControl
    {
        public static readonly DependencyProperty OriginalSenderProperty =
            DependencyProperty.Register("OriginalSender", typeof(string), typeof(ReplyBar),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty OriginalTextProperty =
            DependencyProperty.Register("OriginalText", typeof(string), typeof(ReplyBar),
                new PropertyMetadata("", OnTextChanged));

        public static readonly DependencyProperty OnDismissCommandProperty =
            DependencyProperty.Register("OnDismissCommand", typeof(ICommand), typeof(ReplyBar),
                new PropertyMetadata(null, OnCommandChanged));

        public string OriginalSender
        {
            get { return (string)GetValue(OriginalSenderProperty); }
            set { SetValue(OriginalSenderProperty, value); }
        }

        public string OriginalText
        {
            get { return (string)GetValue(OriginalTextProperty); }
            set { SetValue(OriginalTextProperty, value); }
        }

        public ICommand OnDismissCommand
        {
            get { return (ICommand)GetValue(OnDismissCommandProperty); }
            set { SetValue(OnDismissCommandProperty, value); }
        }

        public ReplyBar()
        {
            this.InitializeComponent();
            ApplyText();
            ApplyCommand();
            DismissButton.Click += OnDismissButtonClick;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as ReplyBar;
            if (b != null) b.ApplyText();
        }

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var b = d as ReplyBar;
            if (b != null) b.ApplyCommand();
        }

        private void ApplyText()
        {
            if (OriginalSenderText != null) OriginalSenderText.Text = OriginalSender ?? string.Empty;
            if (OriginalTextText != null) OriginalTextText.Text = OriginalText ?? string.Empty;
        }

        private void ApplyCommand()
        {
            if (DismissButton != null) DismissButton.Command = OnDismissCommand;
        }

        private void OnDismissButtonClick(object sender, RoutedEventArgs e)
        {
            ICommand cmd = OnDismissCommand;
            if (cmd != null && cmd.CanExecute(null))
            {
                cmd.Execute(null);
            }
        }
    }
}
