
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThePixelRealmsEditor.Models;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class ActionFramesWindow : Window
    {
        public EntityAction EditedAction { get; }

        private readonly List<string> importedFiles = new();

        public ActionFramesWindow(EntityAction action)
        {
            InitializeComponent();

            EditedAction = action.DeepClone();

            Title = $"Action – {EditedAction.Type}";
            ActionTitleText.Text =
                EditedAction.Type.ToUpperInvariant();

            FrameDurationBox.Text =
                EditedAction.FrameDuration.ToString(
                    "0.###", CultureInfo.CurrentCulture);

            RefreshFrames();
        }

        private void RefreshFrames()
        {
            FramesPanel.Children.Clear();

            for (int i = 0; i < EditedAction.Frames.Count; i++)
            {
                int index = i;
                EntityFrame frame = EditedAction.Frames[i];

                var panel = new StackPanel
                {
                    Width = 155,
                    Margin = new Thickness(0, 0, 12, 0)
                };

                panel.Children.Add(new TextBlock
                {
                    Text = (index + 1).ToString(),
                    FontWeight = FontWeights.Bold,
                    FontSize = 15,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 9)
                });

                var preview = new Border
                {
                    Width = 150,
                    Height = 155,
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(17, 19, 24)),
                    BorderBrush =
                        (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4)
                };

                var bitmap = EntityStorage.LoadFrame(
                    frame.ImageFileName);

                if (bitmap != null)
                {
                    var image = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(8)
                    };

                    RenderOptions.SetBitmapScalingMode(
                        image,
                        BitmapScalingMode.NearestNeighbor);

                    preview.Child = image;
                }
                else
                {
                    preview.Child = new TextBlock
                    {
                        Text = "Hiányzó kép",
                        Foreground = Brushes.OrangeRed,
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    };
                }

                panel.Children.Add(preview);

                panel.Children.Add(new TextBlock
                {
                    Text = frame.OriginalFileName,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground =
                        (Brush)FindResource("MutedTextBrush"),
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    Width = 150,
                    Margin = new Thickness(0, 8, 0, 5)
                });

                var buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment =
                        HorizontalAlignment.Center
                };

                buttons.Children.Add(
                    SmallButton("←",
                        () => MoveFrame(index, -1),
                        index > 0));

                buttons.Children.Add(
                    SmallButton("→",
                        () => MoveFrame(index, 1),
                        index < EditedAction.Frames.Count - 1));

                buttons.Children.Add(
                    SmallButton("×",
                        () => RemoveFrame(index),
                        true));

                panel.Children.Add(buttons);
                FramesPanel.Children.Add(panel);
            }

            var addPanel = new StackPanel
            {
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center
            };

            var addButton = new Button
            {
                Content = "+",
                FontSize = 49,
                FontWeight = FontWeights.Light,
                Foreground = (Brush)FindResource("AccentBrush"),
                Width = 145,
                Height = 155,
                ToolTip = "PNG képkocka hozzáadása"
            };

            addButton.Click += (_, _) => AddFrame();

            addPanel.Children.Add(addButton);
            addPanel.Children.Add(new TextBlock
            {
                Text = "Képkocka hozzáadása",
                Foreground =
                    (Brush)FindResource("MutedTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });

            FramesPanel.Children.Add(addPanel);

            FrameCountText.Text =
                $"{EditedAction.Frames.Count} képkocka";
        }

        private Button SmallButton(
            string label,
            Action callback,
            bool enabled)
        {
            var button = new Button
            {
                Content = label,
                Width = 39,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(2),
                IsEnabled = enabled
            };

            button.Click += (_, _) => callback();
            return button;
        }

        private void MoveFrame(int index, int direction)
        {
            int target = index + direction;

            if (target < 0 ||
                target >= EditedAction.Frames.Count)
                return;

            (EditedAction.Frames[index],
             EditedAction.Frames[target]) =
            (EditedAction.Frames[target],
             EditedAction.Frames[index]);

            RefreshFrames();
        }

        private void RemoveFrame(int index)
        {
            if (index < 0 ||
                index >= EditedAction.Frames.Count)
                return;

            EditedAction.Frames.RemoveAt(index);
            RefreshFrames();
        }

        private void AddFrame()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Animációs képkocka kiválasztása",
                Filter = "PNG képek (*.png)|*.png",
                Multiselect = false,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != true)
                return;

            List<string> paths = FindSequentialFrames(
                dialog.FileName);

            if (paths.Count > 1)
            {
                MessageBoxResult choice = MessageBox.Show(
                    $"A kiválasztott kép mellett további " +
                    $"{paths.Count - 1} számozott képkockát találtam.\n\n" +
                    "Importáljam a teljes sorozatot számsorrendben?",
                    "Képsorozat felismerve",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (choice != MessageBoxResult.Yes)
                {
                    paths = new List<string>
                    {
                        dialog.FileName
                    };
                }
            }

            var newlyImported = new List<EntityFrame>();

            try
            {
                foreach (string path in paths)
                {
                    EntityFrame frame =
                        EntityStorage.ImportFrame(path);

                    importedFiles.Add(frame.ImageFileName);
                    newlyImported.Add(frame);
                }

                EditedAction.Frames.AddRange(newlyImported);
                RefreshFrames();
            }
            catch (Exception ex)
            {
                foreach (EntityFrame frame in newlyImported)
                {
                    EntityStorage.DeleteImportedFrame(
                        frame.ImageFileName);

                    importedFiles.Remove(
                        frame.ImageFileName);
                }

                MessageBox.Show(
                    $"A képek importálása sikertelen:\n{ex.Message}",
                    "Importálási hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static List<string> FindSequentialFrames(
            string firstPath)
        {
            var result = new List<string> { firstPath };

            string directory =
                Path.GetDirectoryName(firstPath) ?? "";

            string fileName =
                Path.GetFileNameWithoutExtension(firstPath);

            Match match = Regex.Match(
                fileName,
                @"^(.*?)(\d+)$");

            if (!match.Success ||
                !int.TryParse(match.Groups[2].Value,
                    out int firstNumber) ||
                !Directory.Exists(directory))
            {
                return result;
            }

            string prefix = match.Groups[1].Value;

            var numberedFiles = new Dictionary<int, string>();

            foreach (string path in Directory.EnumerateFiles(
                directory))
            {
                if (!string.Equals(
                    Path.GetExtension(path),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                string name = Path.GetFileNameWithoutExtension(
                    path);

                Match candidate = Regex.Match(
                    name,
                    "^" + Regex.Escape(prefix) + @"(\d+)$",
                    RegexOptions.IgnoreCase);

                if (!candidate.Success ||
                    !int.TryParse(candidate.Groups[1].Value,
                        out int number) ||
                    number <= firstNumber)
                    continue;

                if (!numberedFiles.ContainsKey(number))
                    numberedFiles[number] = path;
            }

            int next = firstNumber + 1;

            while (numberedFiles.TryGetValue(
                next, out string? path))
            {
                result.Add(path);
                next++;
            }

            return result;
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (EditedAction.Frames.Count == 0)
            {
                MessageBox.Show(
                    "Adj hozzá legalább egy képkockát!",
                    "Hiányzó animáció",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            bool valid = double.TryParse(
                FrameDurationBox.Text,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out double duration);

            if (!valid)
            {
                valid = double.TryParse(
                    FrameDurationBox.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out duration);
            }

            if (!valid ||
                !double.IsFinite(duration) ||
                duration <= 0)
            {
                MessageBox.Show(
                    "Adj meg pozitív képkockaidőt!",
                    "Érvénytelen érték",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            EditedAction.FrameDuration = duration;
            DialogResult = true;
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            // A Mégse után ne maradjanak új, árva PNG-k.
            // Mentéskor is töröljük azokat, amelyeket
            // az ablakban hozzáadtak, majd eltávolítottak.

            HashSet<string> retained =
                DialogResult == true
                ? EditedAction.Frames
                    .Select(f => f.ImageFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (string file in importedFiles)
            {
                if (!retained.Contains(file))
                    EntityStorage.DeleteImportedFrame(file);
            }

            base.OnClosed(e);
        }
    }
}
