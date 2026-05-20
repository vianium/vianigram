// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.App.Helpers;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Shapes;

namespace Vianigram.App.Controls
{
    public sealed class QrCodeCanvas : Canvas
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                "Text",
                typeof(string),
                typeof(QrCodeCanvas),
                new PropertyMetadata(null, OnVisualPropertyChanged));

        public static readonly DependencyProperty QuietZoneModulesProperty =
            DependencyProperty.Register(
                "QuietZoneModules",
                typeof(int),
                typeof(QrCodeCanvas),
                new PropertyMetadata(4, OnVisualPropertyChanged));

        public static readonly DependencyProperty ModuleBrushProperty =
            DependencyProperty.Register(
                "ModuleBrush",
                typeof(Brush),
                typeof(QrCodeCanvas),
                new PropertyMetadata(new SolidColorBrush(Colors.Black), OnVisualPropertyChanged));

        public static readonly DependencyProperty LightBrushProperty =
            DependencyProperty.Register(
                "LightBrush",
                typeof(Brush),
                typeof(QrCodeCanvas),
                new PropertyMetadata(new SolidColorBrush(Colors.White), OnVisualPropertyChanged));

        private bool _isRendering;

        public QrCodeCanvas()
        {
            Background = LightBrush;
            SizeChanged += OnSizeChanged;
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public int QuietZoneModules
        {
            get { return (int)GetValue(QuietZoneModulesProperty); }
            set { SetValue(QuietZoneModulesProperty, value); }
        }

        public Brush ModuleBrush
        {
            get { return (Brush)GetValue(ModuleBrushProperty); }
            set { SetValue(ModuleBrushProperty, value); }
        }

        public Brush LightBrush
        {
            get { return (Brush)GetValue(LightBrushProperty); }
            set { SetValue(LightBrushProperty, value); }
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = d as QrCodeCanvas;
            if (view != null)
            {
                view.Render();
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            Render();
        }

        private void Render()
        {
            if (_isRendering)
            {
                return;
            }

            _isRendering = true;
            try
            {
                Children.Clear();
                Background = LightBrush;

                if (string.IsNullOrEmpty(Text))
                {
                    return;
                }

                double renderWidth = ActualWidth > 0 ? ActualWidth : Width;
                double renderHeight = ActualHeight > 0 ? ActualHeight : Height;
                if (!IsUsableSize(renderWidth) || !IsUsableSize(renderHeight))
                {
                    return;
                }

                bool[,] matrix = QrCodeGenerator.EncodeText(Text);
                int modules = matrix.GetLength(0);
                int quiet = QuietZoneModules < 0 ? 4 : QuietZoneModules;
                int totalModules = modules + quiet * 2;
                double renderSize = Math.Min(renderWidth, renderHeight);
                int modulePixels = (int)Math.Floor(renderSize / totalModules);
                if (modulePixels < 1)
                {
                    return;
                }

                double qrPixelSize = modulePixels * totalModules;
                double originX = Math.Floor((renderWidth - qrPixelSize) / 2.0);
                double originY = Math.Floor((renderHeight - qrPixelSize) / 2.0);
                Brush darkBrush = ModuleBrush;

                for (int y = 0; y < modules; y++)
                {
                    for (int x = 0; x < modules; x++)
                    {
                        if (!matrix[y, x])
                        {
                            continue;
                        }

                        var rect = new Rectangle
                        {
                            Width = modulePixels,
                            Height = modulePixels,
                            Fill = darkBrush
                        };

                        SetLeft(rect, originX + (x + quiet) * modulePixels);
                        SetTop(rect, originY + (y + quiet) * modulePixels);
                        Children.Add(rect);
                    }
                }
            }
            catch
            {
                Children.Clear();
            }
            finally
            {
                _isRendering = false;
            }
        }

        private static bool IsUsableSize(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
