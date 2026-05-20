// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProgressDots.xaml.cs
//
// Indeterminate progress affordance modelled on the classic Windows Phone
// "five dots crossing the screen" pattern. Mirrors the
// .vg-progress-dots animation in docs/design_system/tokens.css.
//
// Usage:
//   <layout:ProgressDots IsActive="True" />
//
// The storyboard runs only while IsActive is true and the control is
// loaded; otherwise it's stopped to keep the GPU idle.

using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace Vianigram.App.Controls.Layout
{
    public sealed partial class ProgressDots : UserControl
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(
                "IsActive",
                typeof(bool),
                typeof(ProgressDots),
                new PropertyMetadata(false, OnIsActiveChanged));

        public bool IsActive
        {
            get { return (bool)GetValue(IsActiveProperty); }
            set { SetValue(IsActiveProperty, value); }
        }

        private Storyboard _animation;
        private bool _isLoaded;

        public ProgressDots()
        {
            InitializeComponent();
            _animation = Resources["DotsAnimation"] as Storyboard;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            UpdateAnimationState();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
            StopAnimation();
        }

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var dots = d as ProgressDots;
            if (dots != null) dots.UpdateAnimationState();
        }

        private void UpdateAnimationState()
        {
            if (_isLoaded && IsActive) StartAnimation();
            else StopAnimation();
        }

        private void StartAnimation()
        {
            if (_animation == null) return;
            try { _animation.Begin(); }
            catch { }
        }

        private void StopAnimation()
        {
            if (_animation == null) return;
            try { _animation.Stop(); }
            catch { }
        }
    }
}
