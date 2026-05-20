// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppBarFooter.xaml.cs
//
// Bottom command bar with a glyph+label primary action and a trailing ellipsis
// affordance. Mirrors docs/design_system "AppBar".
//
// Pages bind ICommands directly. The label defaults to the localized "next"
// entry from Resources.resw and can be overridden per-page.

using System;
using System.Windows.Input;
using Vianigram.App.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls.Layout
{
    public sealed partial class AppBarFooter : UserControl
    {
        public static readonly DependencyProperty NextCommandProperty =
            DependencyProperty.Register(
                "NextCommand",
                typeof(ICommand),
                typeof(AppBarFooter),
                new PropertyMetadata(null, OnNextCommandChanged));

        public static readonly DependencyProperty NextLabelProperty =
            DependencyProperty.Register(
                "NextLabel",
                typeof(string),
                typeof(AppBarFooter),
                new PropertyMetadata(null, OnNextLabelChanged));

        public static readonly DependencyProperty ShowNextProperty =
            DependencyProperty.Register(
                "ShowNext",
                typeof(bool),
                typeof(AppBarFooter),
                new PropertyMetadata(true, OnShowNextChanged));

        public ICommand NextCommand
        {
            get { return (ICommand)GetValue(NextCommandProperty); }
            set { SetValue(NextCommandProperty, value); }
        }

        public string NextLabel
        {
            get { return (string)GetValue(NextLabelProperty); }
            set { SetValue(NextLabelProperty, value); }
        }

        public bool ShowNext
        {
            get { return (bool)GetValue(ShowNextProperty); }
            set { SetValue(ShowNextProperty, value); }
        }

        public AppBarFooter()
        {
            InitializeComponent();
            NextLabelText.Text = Strings.Get("CommonNext");
        }

        private static void OnNextCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var h = d as AppBarFooter;
            if (h != null && h.NextButton != null) h.NextButton.Command = (ICommand)e.NewValue;
        }

        private static void OnNextLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var h = d as AppBarFooter;
            string v = e.NewValue as string;
            if (h != null && h.NextLabelText != null && !string.IsNullOrEmpty(v)) h.NextLabelText.Text = v;
        }

        private static void OnShowNextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var h = d as AppBarFooter;
            if (h != null && h.NextButton != null)
                h.NextButton.Visibility = ((bool)e.NewValue) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
