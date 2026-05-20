// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Controls
{
    public sealed partial class DateSeparator : UserControl
    {
        public static readonly DependencyProperty DateProperty =
            DependencyProperty.Register("Date", typeof(DateTime), typeof(DateSeparator),
                new PropertyMetadata(DateTime.MinValue, OnDateChanged));

        public DateTime Date
        {
            get { return (DateTime)GetValue(DateProperty); }
            set { SetValue(DateProperty, value); }
        }

        public DateSeparator()
        {
            this.InitializeComponent();
            ApplyDate();
        }

        private static void OnDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var s = d as DateSeparator;
            if (s != null) s.ApplyDate();
        }

        private void ApplyDate()
        {
            if (DateText == null) return;
            DateTime d = Date;
            if (d == DateTime.MinValue)
            {
                DateText.Text = string.Empty;
                return;
            }

            DateTime today = DateTime.Today;
            DateTime dDate = d.Date;
            if (dDate == today)
            {
                DateText.Text = "Today";
            }
            else if (dDate == today.AddDays(-1))
            {
                DateText.Text = "Yesterday";
            }
            else if (today - dDate < TimeSpan.FromDays(7))
            {
                DateText.Text = d.ToString("ddd");
            }
            else
            {
                DateText.Text = d.ToString("ddd, d MMM");
            }
        }
    }
}
