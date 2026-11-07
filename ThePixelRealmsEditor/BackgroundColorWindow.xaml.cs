using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ThePixelRealmsEditor
{
    public partial class BackgroundColorWindow : Window
    {
        private double hue;
        private double saturation;
        private double value;
        private bool draggingField;
        private bool draggingHue;
        private bool updatingControls;

        public Color SelectedColor { get; private set; }

        public BackgroundColorWindow(Color? initialColor = null)
        {
            InitializeComponent();
            SelectedColor = initialColor ?? Colors.White;
            FromRgb(SelectedColor);
            UpdateControls();
        }

        private static Color HueColor(double h)
        {
            double c = 1.0;
            double x = 1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0);

            (double r, double g, double b) = h switch
            {
                < 60 => (c, x, 0.0),
                < 120 => (x, c, 0.0),
                < 180 => (0.0, c, x),
                < 240 => (0.0, x, c),
                < 300 => (x, 0.0, c),
                _ => (c, 0.0, x)
            };

            return Color.FromRgb(
                (byte)Math.Round(r * 255),
                (byte)Math.Round(g * 255),
                (byte)Math.Round(b * 255));
        }

        private static Color HsvColor(double h, double s, double v)
        {
            Color baseColor = HueColor(h);
            byte Channel(byte channel) => (byte)Math.Round(255 * v * (1 - s + s * channel / 255.0));
            return Color.FromRgb(Channel(baseColor.R), Channel(baseColor.G), Channel(baseColor.B));
        }

        private void FromRgb(Color color)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            value = max;
            saturation = max == 0 ? 0 : delta / max;
            if (delta == 0) hue = 0;
            else if (max == r) hue = 60 * (((g - b) / delta + 6) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
        }

        private void UpdateControls()
        {
            SelectedColor = HsvColor(hue, saturation, value);
            updatingControls = true;
            RedText.Text = SelectedColor.R.ToString();
            GreenText.Text = SelectedColor.G.ToString();
            BlueText.Text = SelectedColor.B.ToString();
            HexText.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
            PreviewSwatch.Background = new SolidColorBrush(SelectedColor);
            HueBase.Fill = new SolidColorBrush(HueColor(hue));
            FieldMarker.Margin = new Thickness(
                Math.Max(0, saturation * ColorField.ActualWidth - 7),
                Math.Max(0, (1 - value) * ColorField.ActualHeight - 7), 0, 0);
            HueMarker.Margin = new Thickness(0,
                Math.Max(0, hue / 360 * HueBar.ActualHeight - 3), 0, 0);
            updatingControls = false;
        }

        private void SetField(Point point)
        {
            if (ColorField.ActualWidth <= 0 || ColorField.ActualHeight <= 0) return;
            saturation = Math.Clamp(point.X / ColorField.ActualWidth, 0, 1);
            value = 1 - Math.Clamp(point.Y / ColorField.ActualHeight, 0, 1);
            UpdateControls();
        }

        private void SetHue(Point point)
        {
            if (HueBar.ActualHeight <= 0) return;
            hue = Math.Clamp(point.Y / HueBar.ActualHeight, 0, 1) * 359.999;
            UpdateControls();
        }

        private void ColorField_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            draggingField = true;
            ColorField.CaptureMouse();
            SetField(e.GetPosition(ColorField));
        }

        private void ColorField_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggingField) SetField(e.GetPosition(ColorField));
        }

        private void ColorField_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            draggingField = false;
            ColorField.ReleaseMouseCapture();
        }

        private void HueBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            draggingHue = true;
            HueBar.CaptureMouse();
            SetHue(e.GetPosition(HueBar));
        }

        private void HueBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggingHue) SetHue(e.GetPosition(HueBar));
        }

        private void HueBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            draggingHue = false;
            HueBar.ReleaseMouseCapture();
        }

        private void ColorField_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateControls();
        private void HueBar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateControls();

        private void HexText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (updatingControls || HexText == null) return;
            string hex = HexText.Text.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out int rgb)) return;

            Color color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            FromRgb(color);
            UpdateControls();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            string hex = HexText.Text.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                MessageBox.Show("Adj meg egy érvényes HEX színkódot, például #BFAEAA.",
                    "Hibás színkód", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
