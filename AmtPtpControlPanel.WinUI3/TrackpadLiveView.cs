using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace AmtPtpControlPanel.WinUI3
{
    /// <summary>
    /// Draws the trackpad and every contact on it, live: one soft spot per contact, in the
    /// size, shape and angle that the trackpad measures. Palms are drawn in a warm color,
    /// and the pad lights up while the button is clicked.
    /// </summary>
    public sealed class TrackpadLiveView : Grid
    {
        private const double MaxPadWidth = 520;
        private const double ShadowSpread = 16;
        private const double DefaultContactMm = 9;
        private const double MinContactMm = 9;
        private const double GlowMm = 8;

        // A fixed blue for fingers and orange for palms, so the two are always easy to
        // tell apart, whatever the accent color is.
        private static readonly Color FingerColorOnWhite = Color.FromArgb(255, 0x0A, 0x84, 0xFF);
        private static readonly Color FingerColorOnBlack = Color.FromArgb(255, 0x40, 0x9C, 0xFF);
        private static readonly Color PalmColor = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);

        private readonly TouchpadReader _reader = new TouchpadReader();
        private readonly Grid _stage = new Grid();
        private readonly Border[] _shadowLayers = new Border[8];
        private readonly Border _pad = new Border();
        private readonly Border _highlight = new Border();
        private readonly Canvas _touchLayer = new Canvas();
        private readonly Border _clickGlow = new Border();
        private readonly TextBlock _hint = new TextBlock();
        private readonly TextBlock _details = new TextBlock();
        private readonly Dictionary<int, Spot> _spots = new Dictionary<int, Spot>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CompositionRoundedRectangleGeometry _clipGeometry;

        private double _surfaceWidthMm = 160.0;
        private double _surfaceHeightMm = 114.9;
        private double _padWidth;
        private double _padHeight;
        private double _lastTick;
        private double _lastFrame = double.NegativeInfinity;
        private double _lastDetails;
        private double _click;
        private bool _buttonDown;
        private bool _rendering;
        private bool _readError;
        private bool _darkPad;
        private bool _showDetails;
        private Color _fingerColor;

        public TrackpadLiveView()
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // A few stacked, slightly larger rounded rectangles make a soft shadow.
            _stage.HorizontalAlignment = HorizontalAlignment.Center;
            _stage.Padding = new Thickness(ShadowSpread);
            for (int i = 0; i < _shadowLayers.Length; i++)
            {
                _shadowLayers[i] = new Border
                {
                    IsHitTestVisible = false,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _stage.Children.Add(_shadowLayers[i]);
            }

            Grid surface = new Grid();
            surface.Children.Add(_highlight);
            surface.Children.Add(_touchLayer);
            surface.Children.Add(_clickGlow);
            surface.Children.Add(_hint);

            _pad.HorizontalAlignment = HorizontalAlignment.Center;
            _pad.VerticalAlignment = VerticalAlignment.Center;
            _pad.BorderThickness = new Thickness(1);
            _pad.Child = surface;
            _stage.Children.Add(_pad);

            _highlight.IsHitTestVisible = false;
            _highlight.BorderThickness = new Thickness(1);

            _clickGlow.IsHitTestVisible = false;
            _clickGlow.BorderThickness = new Thickness(2);
            _clickGlow.Opacity = 0;

            _hint.HorizontalAlignment = HorizontalAlignment.Center;
            _hint.VerticalAlignment = VerticalAlignment.Center;
            _hint.FontSize = 15;
            _hint.IsHitTestVisible = false;

            _details.Visibility = Visibility.Collapsed;
            _details.HorizontalAlignment = HorizontalAlignment.Center;
            _details.FontFamily = new FontFamily("Consolas");
            _details.FontSize = 12;
            _details.MinHeight = 5 * 16;
            _details.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

            SetRow(_stage, 0);
            SetRow(_details, 1);
            Children.Add(_stage);
            Children.Add(_details);

            // Keep the spots inside the rounded corners of the pad.
            Visual touchVisual = ElementCompositionPreview.GetElementVisual(_touchLayer);
            Compositor compositor = touchVisual.Compositor;
            _clipGeometry = compositor.CreateRoundedRectangleGeometry();
            touchVisual.Clip = compositor.CreateGeometricClip(_clipGeometry);

            _reader.FrameReceived += OnFrame;
            SizeChanged += (s, e) => UpdatePadSize();
            ActualThemeChanged += (s, e) => ApplyPadStyle();

            ApplyPadStyle();
            ApplyLocalization();
        }

        /// <summary>Show fingers that hover just above the surface, faintly.</summary>
        public bool ShowHover { get; set; } = true;

        /// <summary>Draw the pad in black instead of white.</summary>
        public bool DarkPad
        {
            get => _darkPad;
            set
            {
                _darkPad = value;
                ApplyPadStyle();
            }
        }

        /// <summary>List pressure and contact size of every contact under the pad.</summary>
        public bool ShowDetails
        {
            get => _showDetails;
            set
            {
                _showDetails = value;
                _details.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private double Now => _clock.Elapsed.TotalSeconds;

        private static string T(string key, string fallback) =>
            LocalizationManager.GetString(key, fallback);

        public void Start(IntPtr hwnd)
        {
            _readError = !_reader.Start(hwnd);
            ApplyLocalization();

            if (!_rendering)
            {
                _lastTick = Now;
                CompositionTarget.Rendering += OnRendering;
                _rendering = true;
            }
        }

        public void Stop()
        {
            _reader.Stop();

            if (_rendering)
            {
                CompositionTarget.Rendering -= OnRendering;
                _rendering = false;
            }

            _touchLayer.Children.Clear();
            _spots.Clear();
            _buttonDown = false;
            _click = 0;
            _clickGlow.Opacity = 0;
            _hint.Opacity = 1;
            _details.Text = "";
        }

        public void Shutdown()
        {
            Stop();
            _reader.Dispose();
        }

        public void ApplyLocalization()
        {
            _hint.Text = _readError
                ? T("LiveView.Error", "Could not read the trackpad.")
                : T("LiveView.Hint", "Touch the trackpad");
        }

        private void OnFrame(TouchFrame frame)
        {
            _lastFrame = Now;
            _buttonDown = frame.ButtonDown;

            if (frame.SurfaceWidthMm > 0 && frame.SurfaceHeightMm > 0 &&
                (Math.Abs(frame.SurfaceWidthMm - _surfaceWidthMm) > 0.1 || Math.Abs(frame.SurfaceHeightMm - _surfaceHeightMm) > 0.1))
            {
                _surfaceWidthMm = frame.SurfaceWidthMm;
                _surfaceHeightMm = frame.SurfaceHeightMm;
                _padWidth = 0;
                UpdatePadSize();
            }

            foreach (Spot spot in _spots.Values)
                spot.Seen = false;

            foreach (TouchContact contact in frame.Contacts)
            {
                if (!_spots.TryGetValue(contact.Id, out Spot? spot))
                {
                    spot = new Spot();
                    _spots[contact.Id] = spot;
                    _touchLayer.Children.Add(spot.Shape);
                }

                spot.Target = contact;
                spot.Seen = true;
            }

            // The trackpad gives every new touch a new ID, so number the contacts on the
            // pad from 1 instead, and free a number as soon as its contact lifts.
            foreach (Spot spot in _spots.Values)
            {
                if (!IsShown(spot))
                    spot.Number = 0;
            }

            foreach (TouchContact contact in frame.Contacts)
            {
                Spot spot = _spots[contact.Id];
                if (spot.Number == 0 && IsShown(spot))
                    spot.Number = NextFreeNumber();
            }
        }

        private bool IsShown(Spot spot) => spot.Seen && (spot.Target.Tip || ShowHover);

        private int NextFreeNumber()
        {
            int number = 1;
            while (_spots.Values.Any(s => s.Number == number))
                number++;
            return number;
        }

        private void OnRendering(object? sender, object e)
        {
            double now = Now;
            double dt = Math.Clamp(now - _lastTick, 0, 0.1);
            _lastTick = now;

            if (_padWidth <= 0)
                return;

            // The trackpad stops sending reports once nothing is on or near it.
            bool stale = now - _lastFrame > 0.25;
            double pxPerMm = _padWidth / _surfaceWidthMm;
            double follow = 1 - Math.Exp(-dt * 30);
            double reshape = 1 - Math.Exp(-dt * 14);
            bool anyVisible = false;
            List<int>? finished = null;

            foreach (KeyValuePair<int, Spot> pair in _spots)
            {
                Spot s = pair.Value;
                TouchContact c = s.Target;
                bool present = s.Seen && !stale;
                double targetOpacity = !present ? 0 : c.Tip ? 1 : ShowHover ? 0.3 : 0;

                // The measured contact plus a soft glow around it, a little larger the
                // harder it presses.
                double press = c.Pressure > 0 ? Math.Min(c.Pressure / 200.0, 1.0) : 0;
                double grow = 1.15 + 0.25 * press;
                double targetMajor = Math.Max(c.MajorMm > 0 ? c.MajorMm : DefaultContactMm, MinContactMm) * grow + GlowMm;
                double targetMinor = Math.Max(c.MinorMm > 0 ? c.MinorMm : DefaultContactMm, MinContactMm) * grow + GlowMm;

                if (!s.Initialized)
                {
                    s.X = c.X;
                    s.Y = c.Y;
                    s.Major = targetMajor * 0.6;
                    s.Minor = targetMinor * 0.6;
                    s.Angle = c.AngleDeg;
                    s.Initialized = true;
                }

                if (present)
                {
                    s.X += (c.X - s.X) * follow;
                    s.Y += (c.Y - s.Y) * follow;
                    s.Major += (targetMajor - s.Major) * reshape;
                    s.Minor += (targetMinor - s.Minor) * reshape;

                    // The axes repeat every 180 degrees, so take the short way round.
                    double turn = c.AngleDeg - s.Angle;
                    turn -= 180 * Math.Round(turn / 180);
                    s.Angle += turn * reshape;
                }
                else
                {
                    // A lifted finger spreads out a little while it fades away.
                    s.Major += s.Major * 0.5 * dt;
                    s.Minor += s.Minor * 0.5 * dt;
                }

                double fade = 1 - Math.Exp(-dt * (targetOpacity > s.Opacity ? 18 : 7));
                s.Opacity += (targetOpacity - s.Opacity) * fade;

                if (!present && s.Opacity < 0.01)
                {
                    (finished ??= new List<int>()).Add(pair.Key);
                    continue;
                }

                bool palm = c.IsPalm;
                if (s.Palm != palm)
                {
                    s.SetColor(palm ? PalmColor : _fingerColor);
                    s.Palm = palm;
                }

                double width = s.Minor * pxPerMm;
                double height = s.Major * pxPerMm;
                s.Shape.Width = width;
                s.Shape.Height = height;
                s.Shape.Opacity = s.Opacity;
                s.Rotation.Angle = s.Angle;
                Canvas.SetLeft(s.Shape, s.X * _padWidth - width / 2);
                Canvas.SetTop(s.Shape, s.Y * _padHeight - height / 2);

                if (s.Opacity > 0.05)
                    anyVisible = true;
            }

            if (finished != null)
            {
                foreach (int id in finished)
                {
                    _touchLayer.Children.Remove(_spots[id].Shape);
                    _spots.Remove(id);
                }
            }

            double clickTarget = _buttonDown && !stale ? 1 : 0;
            _click += (clickTarget - _click) * (1 - Math.Exp(-dt * (clickTarget > _click ? 30 : 6)));
            _clickGlow.Opacity = _click;

            double hintTarget = anyVisible ? 0 : 1;
            _hint.Opacity += (hintTarget - _hint.Opacity) * (1 - Math.Exp(-dt * 8));

            if (_showDetails && now - _lastDetails > 0.1)
            {
                _lastDetails = now;
                UpdateDetails(stale);
            }
        }

        private void UpdateDetails(bool stale)
        {
            StringBuilder text = new StringBuilder();

            foreach (Spot spot in _spots.Values.Where(s => s.Number > 0).OrderBy(s => s.Number))
            {
                TouchContact c = spot.Target;
                if (stale || !IsShown(spot))
                    continue;

                if (text.Length > 0)
                    text.AppendLine();

                text.Append(c.IsPalm ? T("LiveView.Palm", "Palm") : T("LiveView.Finger", "Finger"));
                text.Append(' ').Append(spot.Number.ToString(CultureInfo.CurrentCulture));

                if (c.Pressure >= 0)
                    text.Append("  ").Append(T("LiveView.Pressure", "pressure")).Append(' ').Append(c.Pressure.ToString(CultureInfo.CurrentCulture).PadLeft(3));

                if (c.MajorMm > 0)
                    text.AppendFormat(CultureInfo.CurrentCulture, "  {0,4:0.0} × {1,4:0.0} mm", c.MajorMm, c.MinorMm);

                if (!c.Tip)
                    text.Append("  ").Append(T("LiveView.Hovering", "hovering"));
            }

            _details.Text = text.ToString();
        }

        private void UpdatePadSize()
        {
            double available = ActualWidth - 2 * ShadowSpread;
            if (available <= 0)
                return;

            double width = Math.Min(available, MaxPadWidth);
            double height = width * _surfaceHeightMm / _surfaceWidthMm;
            if (Math.Abs(width - _padWidth) < 0.5 && Math.Abs(height - _padHeight) < 0.5)
                return;

            _padWidth = width;
            _padHeight = height;

            // The Magic Trackpad's corners are about 7.5% of its depth.
            double radius = height * 0.075;

            _pad.Width = width;
            _pad.Height = height;
            _pad.CornerRadius = new CornerRadius(radius);
            _highlight.CornerRadius = new CornerRadius(radius);
            _clickGlow.CornerRadius = new CornerRadius(radius);
            _touchLayer.Width = width;
            _touchLayer.Height = height;

            for (int i = 0; i < _shadowLayers.Length; i++)
            {
                double spread = (i + 1) * ShadowSpread / _shadowLayers.Length;
                Border layer = _shadowLayers[i];
                layer.Width = width + 2 * spread;
                layer.Height = height + 2 * spread;
                layer.CornerRadius = new CornerRadius(radius + spread);
                layer.RenderTransform = new TranslateTransform { Y = spread * 0.4 };
            }

            _clipGeometry.Size = new Vector2((float)width, (float)height);
            _clipGeometry.CornerRadius = new Vector2((float)radius, (float)radius);
        }

        private void ApplyPadStyle()
        {
            bool dark = _darkPad;

            _pad.Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0.35, 1),
                GradientStops =
                {
                    new GradientStop { Offset = 0, Color = dark ? Color.FromArgb(255, 0x38, 0x38, 0x3B) : Color.FromArgb(255, 0xFE, 0xFE, 0xFE) },
                    new GradientStop { Offset = 1, Color = dark ? Color.FromArgb(255, 0x1E, 0x1E, 0x21) : Color.FromArgb(255, 0xEA, 0xEA, 0xED) },
                },
            };
            _pad.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(255, 0x0A, 0x0A, 0x0C) : Color.FromArgb(255, 0xCC, 0xCC, 0xD1));

            // A thin light edge along the top, like the bevel of the real glass.
            _highlight.BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Offset = 0, Color = Color.FromArgb(dark ? (byte)0x40 : (byte)0xFF, 255, 255, 255) },
                    new GradientStop { Offset = 0.5, Color = Color.FromArgb(0, 255, 255, 255) },
                },
            };

            _hint.Foreground = new SolidColorBrush(dark ? Color.FromArgb(0x80, 255, 255, 255) : Color.FromArgb(0x66, 0, 0, 0));

            byte shadowAlpha = ActualTheme == ElementTheme.Dark ? (byte)14 : (byte)5;
            foreach (Border layer in _shadowLayers)
                layer.Background = new SolidColorBrush(Color.FromArgb(shadowAlpha, 0, 0, 0));

            _fingerColor = dark ? FingerColorOnBlack : FingerColorOnWhite;
            _clickGlow.BorderBrush = new SolidColorBrush(WithAlpha(_fingerColor, 0.55));
            _clickGlow.Background = new SolidColorBrush(WithAlpha(_fingerColor, dark ? 0.12 : 0.07));

            foreach (Spot spot in _spots.Values)
                spot.Palm = null; // recolor on the next frame
        }

        private static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), color.R, color.G, color.B);

        private sealed class Spot
        {
            public readonly Ellipse Shape = new Ellipse { IsHitTestVisible = false };
            public readonly RotateTransform Rotation = new RotateTransform();

            public TouchContact Target;
            public bool Seen;
            public bool Initialized;
            public bool? Palm;
            public int Number;
            public double X;
            public double Y;
            public double Major;
            public double Minor;
            public double Angle;
            public double Opacity;

            public Spot()
            {
                Shape.Opacity = 0;
                Shape.RenderTransform = Rotation;
                Shape.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            public void SetColor(Color color)
            {
                // RadialGradientBrush does not redraw when the color of one of its stops
                // changes, so build a new brush instead. Bright in the middle and fading
                // out to nothing at the edge.
                Shape.Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop { Offset = 0, Color = WithAlpha(color, 0.85) },
                        new GradientStop { Offset = 0.38, Color = WithAlpha(color, 0.55) },
                        new GradientStop { Offset = 0.72, Color = WithAlpha(color, 0.16) },
                        new GradientStop { Offset = 1, Color = WithAlpha(color, 0) },
                    },
                };
            }
        }
    }
}
