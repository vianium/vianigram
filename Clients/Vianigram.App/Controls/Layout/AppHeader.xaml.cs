// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppHeader.xaml.cs
//
// Two-line page header used by the auth pages: a small uppercase app-name
// strap on top, and a 40px Light page title beneath. Mirrors the
// docs/design_system "TitleBar" component.

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls.Layout
{
    public sealed partial class AppHeader : UserControl
    {
        public static readonly DependencyProperty AppNameProperty =
            DependencyProperty.Register(
                "AppName",
                typeof(string),
                typeof(AppHeader),
                new PropertyMetadata("VIANIGRAM", OnAppNameChanged));

        public static readonly DependencyProperty PageTitleProperty =
            DependencyProperty.Register(
                "PageTitle",
                typeof(string),
                typeof(AppHeader),
                new PropertyMetadata(string.Empty, OnPageTitleChanged));

        public string AppName
        {
            get { return (string)GetValue(AppNameProperty); }
            set { SetValue(AppNameProperty, value); }
        }

        public string PageTitle
        {
            get { return (string)GetValue(PageTitleProperty); }
            set { SetValue(PageTitleProperty, value); }
        }

        public AppHeader()
        {
            InitializeComponent();
            AppNameText.Text = AppName;
            PageTitleText.Text = PageTitle;
        }

        private static void OnAppNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var h = d as AppHeader;
            if (h != null && h.AppNameText != null) h.AppNameText.Text = (string)e.NewValue;
        }

        private static void OnPageTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var h = d as AppHeader;
            if (h != null && h.PageTitleText != null) h.PageTitleText.Text = (string)e.NewValue;
        }
    }
}
