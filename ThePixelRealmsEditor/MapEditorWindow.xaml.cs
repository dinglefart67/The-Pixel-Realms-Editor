using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ThePixelRealmsEditor.Models;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class MapEditorWindow : Window
    {
        private const string AssetDragFormat =
            "ThePixelRealmsEditor.Asset";

        private Point assetDragStartPoint;

        private AssetModel? dragCandidateAsset;

        private AssetModel? previewAsset;

        private readonly List<MapObject> mapObjects = new();

        private readonly Dictionary<Guid, Border> objectViews = new();

        private MapObject? selectedObject;
        private readonly HashSet<Guid> selectedObjectIds = new();
        private bool updatingCollisionUi;
        private string? draggingCollisionSide;
        private int draggingCollisionThumb;
        private EditorSnapshot? collisionDragSnapshot;
        private static readonly Brush CollisionBrush = new SolidColorBrush(Color.FromRgb(117, 216, 255));


        private MapObject? movingObject;
        private Border? movingObjectView;
        private Point objectDragStartMouse;
        private double objectDragStartX;
        private double objectDragStartY;
        private bool isMovingObject;

        private const double ObjectSnapDistance = 6.0;

        private LayerModel? selectedLayer;

        private const double OtherLayerOpacity = 0.22;

        private string? currentMapPath;
        private bool loadingMap;
        private static readonly JsonSerializerOptions MapJsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        private sealed class MapProject
        {
            public int FormatVersion { get; set; } = 1;
            public string Name { get; set; } = "Untitled";
            public double Width { get; set; } = 5000;
            public double Height { get; set; } = 1080;
            public string BackgroundColor { get; set; } = "#101318";
            public bool HudVisible { get; set; }
            public bool GridVisible { get; set; } = true;
            public bool SnapEnabled { get; set; } = true;
            public int GridSizeIndex { get; set; } = 2;
            public int LayerVisibilityMode { get; set; } = 1;
            public double Zoom { get; set; } = 1;
            public bool Maximized { get; set; }
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public List<LayerModel> Layers { get; set; } = new();
            public List<MapObject> Objects { get; set; } = new();
        }

        private double currentZoom = 1.0;
        private bool isChangingZoom;

        private readonly Stack<EditorSnapshot> undoStack = new();
        private readonly Stack<EditorSnapshot> redoStack = new();

        private bool isRestoringHistory;
        private EditorSnapshot? objectDragStartSnapshot;

        private MapObject? objectClipboard;
        private bool clipboardIsCut;

        private Point objectTreeDragStartPoint;
        private MapObject? objectTreeDragObject;
        private bool isRefreshingObjectTree;

        private sealed class EditorSnapshot
        {
            public List<MapObject> Objects { get; init; } = new();
            public double MapWidth { get; init; }
            public double MapHeight { get; init; }
            public Guid? SelectedObjectId { get; init; }
        }


        public MapEditorWindow()
        {
            InitializeComponent();

            RefreshAssetBrowser();

            LayerDatabase.ResetForNewMap();
            RefreshLayers();

            UpdateWorldCanvasSize();
            UpdateParallaxTransforms();
            UpdateHudPreview();
            RefreshPlacedObjectsTree();
            RefreshCollisionInspector();

            PreviewKeyDown += MapEditorWindow_PreviewKeyDown;
            PreviewKeyDown += (sender, e) =>
            {
                if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    SaveProject(false);
                    e.Handled = true;
                }
            };
        }


        private void MapNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ToolbarMapNameText != null && MapNameTextBox != null)
                ToolbarMapNameText.Text = MapNameTextBox.Text;
        }

        private void NewMapMenu_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Új üres pályát hozol létre. A nem mentett változtatások elvesznek. Folytatod?",
                    "Új pálya", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            ResetProject();
            StatusText.Text = "Új, üres pálya létrehozva.";
        }

        private void ResetProject()
        {
            loadingMap = true;
            try
            {
                currentMapPath = null;
                LayerDatabase.ResetForNewMap();
                mapObjects.Clear();
                selectedObjectIds.Clear();
                objectViews.Clear();
                selectedObject = null;
                selectedLayer = null;
                undoStack.Clear();
                redoStack.Clear();
                ClearRenderedObjects();
                MapNameTextBox.Text = "Untitled";
                MapWidthTextBox.Text = "5000";
                MapHeightTextBox.Text = "1080";
                ApplyMapSize(5000, 1080);
                Color color = (Color)ColorConverter.ConvertFromString("#101318");
                MapBackgroundRectangle.Fill = new SolidColorBrush(color);
                BackgroundColorSwatch.Background = new SolidColorBrush(color);
                ShowHudCheckBox.IsChecked = false;
                GridToggle.IsChecked = true;
                SnapToggle.IsChecked = true;
                GridSizeComboBox.SelectedIndex = 2;
                OtherLayersVisibilityComboBox.SelectedIndex = 1;
                SetZoom(1.0);
                RefreshLayers("Layer 1");
                RefreshPlacedObjectsTree();
                SelectObject(null);
                EmptyViewportMessage.Visibility = Visibility.Visible;
                UpdateParallaxTransforms();
                UpdateHudPreview();
            }
            finally { loadingMap = false; }
        }

        private void ClearRenderedObjects()
        {
            var rendered = WorldCanvas.Children.Cast<UIElement>()
                .Where(x => x is Border border && border.Tag is MapObject ||
                    x is FrameworkElement element && element.Tag is string tag &&
                    tag.StartsWith("GROUND_FILL_", StringComparison.Ordinal)).ToList();
            foreach (var element in rendered)
                WorldCanvas.Children.Remove(element);
        }

        private MapProject CaptureProject()
        {
            Color background = (MapBackgroundRectangle.Fill as SolidColorBrush)?.Color ?? Colors.Black;
            return new MapProject
            {
                Name = MapNameTextBox.Text,
                Width = WorldContainer.Width,
                Height = WorldContainer.Height,
                BackgroundColor = $"#{background.R:X2}{background.G:X2}{background.B:X2}",
                HudVisible = ShowHudCheckBox.IsChecked == true,
                GridVisible = GridToggle.IsChecked == true,
                SnapEnabled = SnapToggle.IsChecked == true,
                GridSizeIndex = GridSizeComboBox.SelectedIndex,
                LayerVisibilityMode = OtherLayersVisibilityComboBox.SelectedIndex,
                Zoom = currentZoom,
                Maximized = WindowState == WindowState.Maximized,
                WindowWidth = RestoreBounds.Width,
                WindowHeight = RestoreBounds.Height,
                Layers = LayerDatabase.Layers.Select(x => new LayerModel { Id = x.Id, Name = x.Name, Value = x.Value }).ToList(),
                Objects = mapObjects.Select(x => x.Clone()).ToList()
            };
        }

        private void SaveMapMenu_Click(object sender, RoutedEventArgs e) => SaveProject(false);
        private void SaveAsMapMenu_Click(object sender, RoutedEventArgs e) => SaveProject(true);

        private void SaveProject(bool saveAs)
        {
            string? path = currentMapPath;
            if (saveAs || string.IsNullOrWhiteSpace(path))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Pályaprojekt mentése",
                    Filter = "The Pixel Realms pálya (*.tprmap)|*.tprmap|JSON (*.json)|*.json",
                    DefaultExt = ".tprmap",
                    AddExtension = true,
                    FileName = string.IsNullOrWhiteSpace(MapNameTextBox.Text) ? "Untitled" :
                        string.Concat(MapNameTextBox.Text.Split(System.IO.Path.GetInvalidFileNameChars()))
                };
                if (dialog.ShowDialog(this) != true) return;
                path = dialog.FileName;
            }
            try
            {
                string json = JsonSerializer.Serialize(CaptureProject(), MapJsonOptions);
                // Write atomically so an interrupted save does not corrupt the existing map.
                string tempPath = path + ".tmp";
                try
                {
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, path, true);
                }
                finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
                currentMapPath = path;
                StatusText.Text = $"Pálya mentve: {System.IO.Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A mentés sikertelen: {ex.Message}", "Mentési hiba",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenMapMenu_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pályaprojekt megnyitása",
                Filter = "The Pixel Realms pálya (*.tprmap)|*.tprmap|JSON (*.json)|*.json|Minden fájl (*.*)|*.*"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                string json = File.ReadAllText(dialog.FileName);
                MapProject project = JsonSerializer.Deserialize<MapProject>(json, MapJsonOptions)
                    ?? throw new InvalidDataException("Üres vagy érvénytelen pályafájl.");
                if (project.FormatVersion != 1 || project.Width <= 0 || project.Height <= 0 ||
                    project.Layers == null || project.Layers.Count == 0 || project.Objects == null)
                    throw new InvalidDataException("Nem támogatott vagy hiányos pályafájl.");
                // Validate everything before replacing the current editor state.
                Color color = (Color)ColorConverter.ConvertFromString(project.BackgroundColor);
                foreach (MapObject obj in project.Objects)
                    if (!project.Layers.Any(l => l.Name == obj.Layer))
                        throw new InvalidDataException($"Hiányzó layer: {obj.Layer}");
                RestoreProject(project, color);
                currentMapPath = dialog.FileName;
                StatusText.Text = $"Pálya betöltve: {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A pálya nem nyitható meg: {ex.Message}", "Megnyitási hiba",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreProject(MapProject project, Color background)
        {
            loadingMap = true;
            try
            {
                mapObjects.Clear();
                selectedObjectIds.Clear();
                objectViews.Clear();
                selectedObject = null;
                selectedLayer = null;
                undoStack.Clear();
                redoStack.Clear();
                ClearRenderedObjects();
                LayerDatabase.ReplaceLayers(project.Layers);
                MapNameTextBox.Text = project.Name;
                MapWidthTextBox.Text = project.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                MapHeightTextBox.Text = project.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                ApplyMapSize(project.Width, project.Height);
                MapBackgroundRectangle.Fill = new SolidColorBrush(background);
                BackgroundColorSwatch.Background = new SolidColorBrush(background);
                ShowHudCheckBox.IsChecked = project.HudVisible;
                GridToggle.IsChecked = project.GridVisible;
                SnapToggle.IsChecked = project.SnapEnabled;
                GridSizeComboBox.SelectedIndex = Math.Clamp(project.GridSizeIndex, 0, GridSizeComboBox.Items.Count - 1);
                OtherLayersVisibilityComboBox.SelectedIndex = Math.Clamp(project.LayerVisibilityMode, 0, 2);
                RefreshLayers(project.Layers[0].Name);
                foreach (MapObject saved in project.Objects)
                {
                    MapObject obj = saved.Clone();
                    mapObjects.Add(obj);
                    RenderMapObject(obj);
                }
                SetZoom(project.Zoom);
                if (project.WindowWidth > 100 && project.WindowHeight > 100)
                {
                    Width = project.WindowWidth;
                    Height = project.WindowHeight;
                }
                WindowState = project.Maximized ? WindowState.Maximized : WindowState.Normal;
                EmptyViewportMessage.Visibility = mapObjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SelectObject(null);
                RefreshPlacedObjectsTree();
                UpdateLayerVisibility();
                UpdateParallaxTransforms();
                UpdateHudPreview();
            }
            finally { loadingMap = false; }
        }

        // =====================================================
        // ASSET BROWSER
        // =====================================================

        private void RefreshAssetBrowser()
        {
            AssetBrowserPanel.Children.Clear();

            string searchText =
                AssetSearchTextBox?.Text?.Trim() ?? "";

            IEnumerable<AssetModel> filteredAssets =
                AssetDatabase.Assets;


            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredAssets =
                    filteredAssets.Where(
                        x =>
                            x.Name.Contains(
                                searchText,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            x.Category.Contains(
                                searchText,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            x.Type.Contains(
                                searchText,
                                StringComparison.OrdinalIgnoreCase));
            }


            foreach (AssetCategory category
                     in CategoryDatabase.Categories)
            {
                Expander categoryExpander =
                    new()
                    {
                        Header = category.Name,
                        Foreground =
                            (Brush)FindResource("TextBrush"),
                        IsExpanded = true,
                        Margin =
                            new Thickness(0, 0, 0, 6)
                    };


                StackPanel typePanel = new();

                bool categoryContainsAnything = false;


                foreach (string type in category.Types)
                {
                    List<AssetModel> assets =
                        filteredAssets
                            .Where(
                                x =>
                                    x.Category == category.Name &&
                                    x.Type == type)
                            .OrderBy(x => x.Name)
                            .ToList();


                    if (!string.IsNullOrWhiteSpace(searchText) &&
                        assets.Count == 0)
                    {
                        continue;
                    }


                    Expander typeExpander =
                        new()
                        {
                            Header = type,
                            Foreground =
                                (Brush)FindResource("TextBrush"),
                            IsExpanded = true,
                            Margin =
                                new Thickness(12, 4, 0, 4)
                        };


                    StackPanel assetPanel =
                        new()
                        {
                            Margin =
                                new Thickness(5, 5, 0, 5)
                        };


                    foreach (AssetModel asset in assets)
                    {
                        assetPanel.Children.Add(
                            CreateAssetItem(asset));
                    }


                    if (assets.Count == 0)
                    {
                        assetPanel.Children.Add(
                            new TextBlock
                            {
                                Text = "Nincs asset",
                                Foreground =
                                    (Brush)FindResource(
                                        "MutedTextBrush"),
                                FontSize = 10,
                                Margin =
                                    new Thickness(8, 4, 0, 4)
                            });
                    }


                    typeExpander.Content =
                        assetPanel;

                    typePanel.Children.Add(
                        typeExpander);

                    categoryContainsAnything = true;
                }


                if (!categoryContainsAnything &&
                    !string.IsNullOrWhiteSpace(searchText))
                {
                    continue;
                }


                categoryExpander.Content =
                    typePanel;

                AssetBrowserPanel.Children.Add(
                    categoryExpander);
            }
        }


        private UIElement CreateAssetItem(
            AssetModel asset)
        {
            Border border =
                new()
                {
                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(24, 28, 34)),

                    BorderBrush =
                        (Brush)FindResource("BorderBrush"),

                    BorderThickness =
                        new Thickness(1),

                    CornerRadius =
                        new CornerRadius(4),

                    Margin =
                        new Thickness(0, 0, 0, 6),

                    Padding =
                        new Thickness(6),

                    Cursor =
                        Cursors.Hand,

                    Tag = asset
                };


            Grid grid = new();

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(52)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                });


            Border imageBorder =
                new()
                {
                    Width = 46,
                    Height = 46,

                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(9, 11, 14)),

                    BorderBrush =
                        (Brush)FindResource("BorderBrush"),

                    BorderThickness =
                        new Thickness(1),

                    CornerRadius =
                        new CornerRadius(3)
                };


            Image image =
                new()
                {
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(3),
                    IsHitTestVisible = false
                };


            string imagePath =
                EditorStorage.GetAssetImagePath(asset);


            if (File.Exists(imagePath))
            {
                try
                {
                    image.Source =
                        LoadBitmap(imagePath, 96);
                }
                catch
                {
                }
            }


            imageBorder.Child = image;

            Grid.SetColumn(imageBorder, 0);

            grid.Children.Add(imageBorder);


            StackPanel textPanel =
                new()
                {
                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Margin =
                        new Thickness(8, 0, 5, 0),

                    IsHitTestVisible = false
                };


            textPanel.Children.Add(
                new TextBlock
                {
                    Text = asset.Name,
                    FontSize = 12,
                    FontWeight =
                        FontWeights.SemiBold,
                    TextTrimming =
                        TextTrimming.CharacterEllipsis
                });


            textPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"{asset.GameWidth:0.##} × {asset.GameHeight:0.##}",

                    FontSize = 10,

                    Foreground =
                        (Brush)FindResource(
                            "MutedTextBrush"),

                    Margin =
                        new Thickness(0, 3, 0, 0)
                });


            Grid.SetColumn(textPanel, 1);

            grid.Children.Add(textPanel);


            Button editButton =
                new()
                {
                    Content = "✎",
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    Visibility =
                        Visibility.Collapsed,
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    Tag = asset
                };


            editButton.Click +=
                AssetEditButton_Click;


            Grid.SetColumn(editButton, 2);

            grid.Children.Add(editButton);


            border.MouseEnter +=
                (_, _) =>
                {
                    editButton.Visibility =
                        Visibility.Visible;

                    border.BorderBrush =
                        (Brush)FindResource(
                            "AccentBrush");
                };


            border.MouseLeave +=
                (_, _) =>
                {
                    editButton.Visibility =
                        Visibility.Collapsed;

                    border.BorderBrush =
                        (Brush)FindResource(
                            "BorderBrush");
                };


            border.PreviewMouseLeftButtonDown +=
                AssetItem_PreviewMouseLeftButtonDown;

            border.PreviewMouseMove +=
                AssetItem_PreviewMouseMove;


            border.Child = grid;

            return border;
        }


        // =====================================================
        // ASSET DRAG START
        // =====================================================

        private void AssetItem_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not Border border ||
                border.Tag is not AssetModel asset)
            {
                return;
            }


            assetDragStartPoint =
                e.GetPosition(null);

            dragCandidateAsset =
                asset;
        }


        private void AssetItem_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (e.LeftButton !=
                    MouseButtonState.Pressed ||
                dragCandidateAsset == null)
            {
                return;
            }


            Point currentPosition =
                e.GetPosition(null);

            Vector difference =
                assetDragStartPoint -
                currentPosition;


            if (Math.Abs(difference.X) <
                    SystemParameters
                        .MinimumHorizontalDragDistance
                &&
                Math.Abs(difference.Y) <
                    SystemParameters
                        .MinimumVerticalDragDistance)
            {
                return;
            }


            AssetModel asset =
                dragCandidateAsset;

            dragCandidateAsset = null;


            DataObject data = new();

            data.SetData(
                AssetDragFormat,
                asset.Id.ToString());


            DragDrop.DoDragDrop(
                (DependencyObject)sender,
                data,
                DragDropEffects.Copy);


            HideDropPreview();
        }


        // =====================================================
        // DROP PREVIEW
        // =====================================================




        private void WorldCanvas_DragMove(object sender, DragEventArgs e)
        {
            UpdateDragPreview(e);
        }


        private void WorldCanvas_DragLeave(
            object sender,
            DragEventArgs e)
        {
            HideDropPreview();
        }


        private void UpdateDragPreview(
            DragEventArgs e)
        {
            if (!TryGetDraggedAsset(
                e,
                out AssetModel? asset))
            {
                e.Effects =
                    DragDropEffects.None;

                HideDropPreview();

                e.Handled = true;

                return;
            }


            e.Effects =
                DragDropEffects.Copy;


            if (previewAsset?.Id != asset.Id)
            {
                string path =
                    EditorStorage.GetAssetImagePath(
                        asset);


                if (File.Exists(path))
                {
                    try
                    {
                        DropPreviewImage.Source =
                            LoadBitmap(path);

                        RenderOptions
                            .SetBitmapScalingMode(
                                DropPreviewImage,
                                BitmapScalingMode
                                    .NearestNeighbor);
                    }
                    catch
                    {
                        DropPreviewImage.Source =
                            null;
                    }
                }


                previewAsset = asset;
            }


            Point position =
                e.GetPosition(WorldCanvas);

            string previewLayer =
                selectedLayer?.Name ?? "Layer 1";

            double logicalPreviewX =
                position.X -
                GetParallaxContentOffset(
                    previewLayer);


            Point snappedPreview =
                GetSnappedPositionForBounds(
                    logicalPreviewX,
                    position.Y,
                    asset.GameWidth,
                    asset.GameHeight,
                    null,
                    previewLayer);

            double x = snappedPreview.X;
            double y = snappedPreview.Y;


            DropPreview.Width =
                asset.GameWidth;

            DropPreview.Height =
                asset.GameHeight;


            DropPreview.Margin =
                new Thickness(
                    x,
                    y,
                    0,
                    0);


            DropPreview.Visibility =
                Visibility.Visible;

            ApplyParallaxTransform(
                DropPreview,
                previewLayer);


            CursorPositionText.Text =
                $"X: {x:0.##}   Y: {y:0.##}";


            e.Handled = true;
        }


        private void HideDropPreview()
        {
            DropPreview.Visibility =
                Visibility.Collapsed;

            previewAsset = null;
        }


        private bool TryGetDraggedAsset(
            DragEventArgs e,
            out AssetModel? asset)
        {
            asset = null;


            if (!e.Data.GetDataPresent(
                AssetDragFormat))
            {
                return false;
            }


            string? idText =
                e.Data.GetData(
                    AssetDragFormat)
                as string;


            if (!Guid.TryParse(
                idText,
                out Guid id))
            {
                return false;
            }


            asset =
                AssetDatabase.Assets
                    .FirstOrDefault(
                        x => x.Id == id);


            return asset != null;
        }


        // =====================================================
        // DROP
        // =====================================================

        private void WorldCanvas_Drop(
            object sender,
            DragEventArgs e)
        {
            if (!TryGetDraggedAsset(
                e,
                out AssetModel? asset) ||
                asset == null)
            {
                return;
            }


            Point position =
                e.GetPosition(WorldCanvas);

            string dropLayer =
                selectedLayer?.Name ?? "Layer 1";

            double logicalDropX =
                position.X -
                GetParallaxContentOffset(
                    dropLayer);


            Point snappedDrop =
                GetSnappedPositionForBounds(
                    logicalDropX,
                    position.Y,
                    asset.GameWidth,
                    asset.GameHeight,
                    null,
                    dropLayer);

            PlaceAsset(
                asset,
                snappedDrop.X,
                snappedDrop.Y);


            HideDropPreview();

            e.Handled = true;
        }


        // =====================================================
        // PLACE OBJECT
        // =====================================================

        private void PlaceAsset(
            AssetModel asset,
            double x,
            double y)
        {
            EditorSnapshot beforeChange =
                CaptureEditorSnapshot();

            x =
                Math.Clamp(
                    x,
                    0,
                    Math.Max(
                        0,
                        WorldCanvas.Width -
                        asset.GameWidth));


            y =
                Math.Clamp(
                    y,
                    0,
                    Math.Max(
                        0,
                        WorldCanvas.Height -
                        asset.GameHeight));


            MapObject mapObject =
                new()
                {
                    AssetId = asset.Id,

                    X = x,
                    Y = y,

                    Width =
                        asset.GameWidth,

                    Height =
                        asset.GameHeight,

                    WidthMode =
                        AssetResizeMode.Stretch,

                    HeightMode =
                        AssetResizeMode.Stretch,

                    Layer = selectedLayer?.Name ?? "Layer 1",

                    ZIndex = 0
                };


            if (string.Equals(asset.Type, "Ground", StringComparison.OrdinalIgnoreCase))
                mapObject.TopCollision.Enabled = true;

            mapObjects.Add(mapObject);

            RenderMapObject(mapObject);

            SelectObject(mapObject);
            RefreshPlacedObjectsTree();


            EmptyViewportMessage.Visibility =
                Visibility.Collapsed;


            StatusText.Text =
                $"{asset.Name} elhelyezve — X: {x:0.##}, Y: {y:0.##}";

            CommitHistory(beforeChange);
        }


        // =====================================================
        // RENDER MAP OBJECT
        // =====================================================

        private void RenderMapObject(
            MapObject mapObject)
        {
            AssetModel? asset =
                GetAsset(mapObject);


            if (asset == null)
                return;


            string path =
                EditorStorage.GetAssetImagePath(
                    asset);


            if (!File.Exists(path))
                return;


            if (objectViews.TryGetValue(
                mapObject.Id,
                out Border? oldView))
            {
                WorldCanvas.Children.Remove(
                    oldView);

                objectViews.Remove(
                    mapObject.Id);
            }


            Border objectBorder =
                new()
                {
                    // A kijelölő keret az asseten KÍVÜL van.
                    // Így a Border 1 pixele nem vesz el helyet magából a képből.
                    Width =
                        mapObject.Width + 2,

                    Height =
                        mapObject.Height + 2,

                    BorderThickness =
                        new Thickness(1),

                    BorderBrush =
                        selectedObjectIds.Contains(mapObject.Id)
                            ? Brushes.White
                            : Brushes.Transparent,

                    Background =
                        Brushes.Transparent,

                    Tag =
                        mapObject,

                    Cursor =
                        Cursors.Hand,

                    ClipToBounds =
                        true
                };


            FrameworkElement renderer =
                CreateObjectRenderer(
                    mapObject,
                    asset,
                    path);


            Grid visualWithCollision = new();
            visualWithCollision.Children.Add(renderer);
            Canvas collisionOverlay = new()
            {
                Width = mapObject.Width,
                Height = mapObject.Height,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(1, 1, 0, 0),
                IsHitTestVisible = false,
                Visibility = CollisionsToggle?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed,
                Tag = "COLLISION_OVERLAY"
            };
            DrawCollisionLines(collisionOverlay, mapObject);
            visualWithCollision.Children.Add(collisionOverlay);
            objectBorder.Child = visualWithCollision;


            objectBorder.PreviewMouseLeftButtonDown +=
                MapObject_PreviewMouseLeftButtonDown;

            objectBorder.PreviewMouseMove +=
                MapObject_PreviewMouseMove;

            objectBorder.PreviewMouseLeftButtonUp +=
                MapObject_PreviewMouseLeftButtonUp;


            // -1 azért kell, mert a fehér kijelölő keret az objektum
            // tényleges határán kívül helyezkedik el.
            Canvas.SetLeft(
                objectBorder,
                mapObject.X - 1);

            Canvas.SetTop(
                objectBorder,
                mapObject.Y - 1);


            Panel.SetZIndex(
                objectBorder,
                100 + mapObject.ZIndex);


            WorldCanvas.Children.Add(
                objectBorder);


            objectViews[mapObject.Id] =
                objectBorder;

            ApplyParallaxTransform(
                objectBorder,
                mapObject.Layer);



            RenderGroundFill(
                mapObject,
                asset,
                path);

            ApplyLayerVisibilityToObject(
                mapObject);
            UpdateLayerDrawOrder();
        }


        // =====================================================
        // STRETCH / REPEAT RENDERING
        // =====================================================

        private FrameworkElement CreateObjectRenderer(
            MapObject mapObject,
            AssetModel asset,
            string path)
        {
            BitmapImage bitmap =
                LoadBitmap(path);


            // Mindkét irány nyújtás.
            if (mapObject.WidthMode ==
                    AssetResizeMode.Stretch &&
                mapObject.HeightMode ==
                    AssetResizeMode.Stretch)
            {
                Image image =
                    new()
                    {
                        Source = bitmap,
                        Width =
                            mapObject.Width,
                        Height =
                            mapObject.Height,
                        Stretch =
                            Stretch.Fill,
                        IsHitTestVisible =
                            false
                    };


                RenderOptions
                    .SetBitmapScalingMode(
                        image,
                        BitmapScalingMode
                            .NearestNeighbor);


                return image;
            }


            Grid tileGrid = new()
            {
                Width =
                    mapObject.Width,

                Height =
                    mapObject.Height,

                IsHitTestVisible =
                    false,

                ClipToBounds =
                    true
            };


            double nativeWidth =
                asset.GameWidth;

            double nativeHeight =
                asset.GameHeight;


            int columns =
                mapObject.WidthMode ==
                AssetResizeMode.Repeat
                    ? Math.Max(
                        1,
                        (int)Math.Round(
                            mapObject.Width /
                            nativeWidth))
                    : 1;


            int rows =
                mapObject.HeightMode ==
                AssetResizeMode.Repeat
                    ? Math.Max(
                        1,
                        (int)Math.Round(
                            mapObject.Height /
                            nativeHeight))
                    : 1;


            double cellWidth =
                mapObject.WidthMode ==
                AssetResizeMode.Repeat
                    ? nativeWidth
                    : mapObject.Width;


            double cellHeight =
                mapObject.HeightMode ==
                AssetResizeMode.Repeat
                    ? nativeHeight
                    : mapObject.Height;


            Canvas tileCanvas =
                new()
                {
                    Width =
                        mapObject.Width,

                    Height =
                        mapObject.Height,

                    IsHitTestVisible =
                        false
                };


            for (int row = 0;
                 row < rows;
                 row++)
            {
                for (int column = 0;
                     column < columns;
                     column++)
                {
                    Image tile =
                        new()
                        {
                            Source = bitmap,

                            Width =
                                cellWidth,

                            Height =
                                cellHeight,

                            Stretch =
                                Stretch.Fill,

                            IsHitTestVisible =
                                false
                        };


                    RenderOptions
                        .SetBitmapScalingMode(
                            tile,
                            BitmapScalingMode
                                .NearestNeighbor);


                    Canvas.SetLeft(
                        tile,
                        column *
                        cellWidth);

                    Canvas.SetTop(
                        tile,
                        row *
                        cellHeight);


                    tileCanvas.Children.Add(
                        tile);
                }
            }


            tileGrid.Children.Add(
                tileCanvas);


            return tileGrid;
        }


        // =====================================================
        // GROUND FILL
        // =====================================================

        private void RenderGroundFill(
            MapObject mapObject,
            AssetModel asset,
            string path)
        {
            string fillTag =
                $"GROUND_FILL_{mapObject.Id}";


            UIElement? existing =
                WorldCanvas.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(
                        x =>
                            x.Tag as string ==
                            fillTag);


            if (existing != null)
            {
                WorldCanvas.Children.Remove(
                    existing);
            }


            if (!string.Equals(
                asset.Type,
                "Ground",
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }


            double startY =
                mapObject.Y +
                mapObject.Height;


            double height =
                WorldCanvas.Height -
                startY;


            if (height <= 0)
                return;


            Color fillColor;


            try
            {
                fillColor =
                    GetDominantBottomRowColor(
                        path);
            }
            catch
            {
                fillColor =
                    Colors.Black;
            }


            Rectangle fill =
                new()
                {
                    Width =
                        mapObject.Width,

                    Height =
                        height,

                    Fill =
                        new SolidColorBrush(
                            fillColor),

                    IsHitTestVisible =
                        false,

                    Tag =
                        fillTag
                };


            Canvas.SetLeft(
                fill,
                mapObject.X);

            Canvas.SetTop(
                fill,
                startY);


            Panel.SetZIndex(
                fill,
                mapObject.ZIndex);


            ApplyParallaxTransform(
                fill,
                mapObject.Layer);

            WorldCanvas.Children.Add(
                fill);

            ApplyLayerVisibilityToElement(
                fill,
                mapObject.Layer);
        }


        // =====================================================
        // SELECT OBJECT
        // =====================================================

        private void MapObject_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not Border border ||
                border.Tag is not MapObject mapObject)
            {
                return;
            }


            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ToggleObjectSelection(mapObject);
                e.Handled = true;
                return;
            }
            if (!selectedObjectIds.Contains(mapObject.Id) || selectedObjectIds.Count <= 1)
                SelectObject(mapObject);
            if (selectedObjectIds.Count > 1)
            {
                e.Handled = true;
                return;
            }

            movingObject = mapObject;
            movingObjectView = border;
            objectDragStartMouse = e.GetPosition(WorldCanvas);
            objectDragStartX = mapObject.X;
            objectDragStartY = mapObject.Y;
            objectDragStartSnapshot = CaptureEditorSnapshot();
            isMovingObject = false;

            border.CaptureMouse();

            e.Handled = true;
        }


        private void MapObject_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed ||
                movingObject == null ||
                movingObjectView == null ||
                sender != movingObjectView)
            {
                return;
            }

            Point mouse = e.GetPosition(WorldCanvas);

            double dx = mouse.X - objectDragStartMouse.X;
            double dy = mouse.Y - objectDragStartMouse.Y;

            if (!isMovingObject &&
                Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            isMovingObject = true;

            double wantedX = objectDragStartX + dx;
            double wantedY = objectDragStartY + dy;

            SetObjectPosition(
                movingObject,
                wantedX,
                wantedY,
                snap: true);

            e.Handled = true;
        }


        private void MapObject_PreviewMouseLeftButtonUp(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is Border border &&
                border.IsMouseCaptured)
            {
                border.ReleaseMouseCapture();
            }

            if (movingObject != null && isMovingObject)
            {
                StatusText.Text =
                    $"Objektum mozgatva — X: {movingObject.X:0.##}, Y: {movingObject.Y:0.##}";

                if (objectDragStartSnapshot != null)
                {
                    CommitHistory(objectDragStartSnapshot);
                }

            }

            objectDragStartSnapshot = null;
            movingObject = null;
            movingObjectView = null;
            isMovingObject = false;

            e.Handled = true;
        }

        private void SetObjectPosition(MapObject mapObject, double x, double y, bool snap)
        {
            Point position = GetObjectPosition(mapObject, x, y, snap);
            mapObject.X = position.X;
            mapObject.Y = position.Y;

            if (objectViews.TryGetValue(mapObject.Id, out Border? view))
            {
                Canvas.SetLeft(view, mapObject.X - 1);
                Canvas.SetTop(view, mapObject.Y - 1);
            }

            AssetModel? asset = GetAsset(mapObject);
            if (asset != null)
            {
                string path = EditorStorage.GetAssetImagePath(asset);
                if (File.Exists(path))
                    RenderGroundFill(mapObject, asset, path);
            }

            if (selectedObject?.Id == mapObject.Id)
            {
                ObjectXTextBox.Text = mapObject.X.ToString("0.##");
                ObjectYTextBox.Text = mapObject.Y.ToString("0.##");
            }

            CursorPositionText.Text = $"X: {mapObject.X:0.##}   Y: {mapObject.Y:0.##}";
        }

        private Point GetObjectPosition(MapObject mapObject, double x, double y, bool snap)
        {
            if (snap)
                return GetSnappedPositionForBounds(
                    x, y, mapObject.Width, mapObject.Height, mapObject.Id, mapObject.Layer);

            return new Point(
                Math.Clamp(x, 0, Math.Max(0, WorldCanvas.Width - mapObject.Width)),
                Math.Clamp(y, 0, Math.Max(0, WorldCanvas.Height - mapObject.Height)));
        }





        private Point GetSnappedPositionForBounds(
            double wantedX,
            double wantedY,
            double width,
            double height,
            Guid? ignoredObjectId,
            string layerName)
        {
            if (SnapToggle?.IsChecked != true)
                return new Point(
                    Math.Clamp(wantedX, 0, Math.Max(0, WorldCanvas.Width - width)),
                    Math.Clamp(wantedY, 0, Math.Max(0, WorldCanvas.Height - height)));

            double layerWidth = WorldCanvas.Width;
            double layerHeight = WorldCanvas.Height;

            double maxX = Math.Max(0, layerWidth - width);
            double maxY = Math.Max(0, layerHeight - height);

            double x = Math.Clamp(wantedX, 0, maxX);
            double y = Math.Clamp(wantedY, 0, maxY);

            double? bestSnapX = null;
            double bestXDistance = ObjectSnapDistance + 1;

            double? bestSnapY = null;
            double bestYDistance = ObjectSnapDistance + 1;

            TrySnapX(0, x);
            TrySnapX(layerWidth - width, x);
            TrySnapY(0, y);
            TrySnapY(layerHeight - height, y);

            double movingLeft = x;
            double movingRight = x + width;
            double movingTop = y;
            double movingBottom = y + height;

            foreach (MapObject other in mapObjects)
            {
                if (ignoredObjectId.HasValue &&
                    other.Id == ignoredObjectId.Value)
                {
                    continue;
                }

                double activeParallaxOffset =
                    GetParallaxContentOffset(layerName);

                double otherParallaxOffset =
                    GetParallaxContentOffset(other.Layer);

                double otherLeft =
                    other.X +
                    otherParallaxOffset -
                    activeParallaxOffset;

                double otherRight =
                    otherLeft +
                    other.Width;

                double otherTop = other.Y;
                double otherBottom =
                    other.Y +
                    other.Height;

                bool verticalOverlap =
                    movingBottom > otherTop &&
                    movingTop < otherBottom;

                bool horizontalOverlap =
                    movingRight > otherLeft &&
                    movingLeft < otherRight;

                if (verticalOverlap)
                {
                    TrySnapX(otherLeft - width, x);
                    TrySnapX(otherRight, x);
                    TrySnapX(otherLeft, x);
                    TrySnapX(otherRight - width, x);
                }

                if (horizontalOverlap)
                {
                    TrySnapY(otherTop - height, y);
                    TrySnapY(otherBottom, y);
                    TrySnapY(otherTop, y);
                    TrySnapY(otherBottom - height, y);
                }
            }

            if (bestSnapX.HasValue)
                x = Math.Clamp(bestSnapX.Value, 0, maxX);

            if (bestSnapY.HasValue)
                y = Math.Clamp(bestSnapY.Value, 0, maxY);

            return new Point(x, y);

            void TrySnapX(double candidateX, double currentX)
            {
                double distance = Math.Abs(candidateX - currentX);

                if (distance <= ObjectSnapDistance &&
                    distance < bestXDistance)
                {
                    bestXDistance = distance;
                    bestSnapX = candidateX;
                }
            }

            void TrySnapY(double candidateY, double currentY)
            {
                double distance = Math.Abs(candidateY - currentY);

                if (distance <= ObjectSnapDistance &&
                    distance < bestYDistance)
                {
                    bestYDistance = distance;
                    bestSnapY = candidateY;
                }
            }
        }


        private void WorldCanvas_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.OriginalSource == WorldCanvas)
            {
                SelectObject(null);
            }
        }


        private void SelectObject(MapObject? mapObject)
        {
            selectedObjectIds.Clear();
            if (mapObject != null) selectedObjectIds.Add(mapObject.Id);
            UpdateObjectSelectionVisuals();
        }

        private void ToggleObjectSelection(MapObject mapObject)
        {
            if (!selectedObjectIds.Add(mapObject.Id)) selectedObjectIds.Remove(mapObject.Id);
            UpdateObjectSelectionVisuals();
        }

        private void UpdateObjectSelectionVisuals()
        {
            selectedObject = selectedObjectIds.Count == 1
                ? mapObjects.FirstOrDefault(x => selectedObjectIds.Contains(x.Id)) : null;
            foreach (var pair in objectViews)
                pair.Value.BorderBrush = selectedObjectIds.Contains(pair.Key) ? Brushes.White : Brushes.Transparent;
            UpdateInspector();
            SyncObjectTreeSelection();
        }

        // =====================================================
        // INSPECTOR
        // =====================================================

        private void UpdateInspector()
        {
            bool enabled =
                selectedObject != null;
            if (selectedObjectIds.Count > 1)
            {
                InspectorSelectionText.Text = $"{selectedObjectIds.Count} objektum kijelölve";
                enabled = false;
            }


            ObjectXTextBox.IsEnabled =
                enabled;

            ObjectYTextBox.IsEnabled =
                enabled;

            ObjectWidthTextBox.IsEnabled =
                enabled;

            ObjectHeightTextBox.IsEnabled =
                enabled;

            WidthModeComboBox.IsEnabled =
                enabled;

            HeightModeComboBox.IsEnabled =
                enabled;

            KeepAspectRatioCheckBox.IsEnabled =
                enabled;

            ApplyObjectButton.IsEnabled =
                enabled;


            if (selectedObject == null)
            {
                if (selectedObjectIds.Count == 0)
                    InspectorSelectionText.Text = "Nincs kijelölt objektum";

                ObjectXTextBox.Text = "0";
                ObjectYTextBox.Text = "0";
                ObjectWidthTextBox.Text = "0";
                ObjectHeightTextBox.Text = "0";
                KeepAspectRatioCheckBox.IsChecked = false;
                RefreshCollisionInspector();
                return;
            }


            AssetModel? asset =
                GetAsset(selectedObject);


            InspectorSelectionText.Text =
                asset?.Name ??
                "Ismeretlen asset";


            ObjectXTextBox.Text =
                selectedObject.X
                    .ToString("0.##");

            ObjectYTextBox.Text =
                selectedObject.Y
                    .ToString("0.##");

            ObjectWidthTextBox.Text =
                selectedObject.Width
                    .ToString("0.##");

            ObjectHeightTextBox.Text =
                selectedObject.Height
                    .ToString("0.##");

            WidthModeComboBox.SelectedIndex =
                selectedObject.WidthMode ==
                AssetResizeMode.Stretch
                    ? 0
                    : 1;


            HeightModeComboBox.SelectedIndex =
                selectedObject.HeightMode ==
                AssetResizeMode.Stretch
                    ? 0
                    : 1;
            RefreshCollisionInspector();

        }


        // =====================================================
        // APPLY OBJECT SETTINGS
        // =====================================================

        private void ApplyObjectButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (selectedObject == null)
                return;


            AssetModel? asset =
                GetAsset(selectedObject);


            if (asset == null)
                return;


            if (!double.TryParse(
                    ObjectXTextBox.Text,
                    out double x) ||
                !double.TryParse(
                    ObjectYTextBox.Text,
                    out double y) ||
                !double.TryParse(
                    ObjectWidthTextBox.Text,
                    out double requestedWidth) ||
                !double.TryParse(
                    ObjectHeightTextBox.Text,
                    out double requestedHeight))
            {
                MessageBox.Show(
                    "Az X, Y, szélesség és magasság mezőkben szám szerepeljen.",
                    "Hibás érték",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (requestedWidth <= 0 ||
                requestedHeight <= 0)
            {
                MessageBox.Show(
                    "A szélesség és magasság legyen nagyobb 0-nál.",
                    "Hibás méret",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (KeepAspectRatioCheckBox.IsChecked == true)
            {
                double aspectRatio =
                    selectedObject.Width /
                    selectedObject.Height;

                bool widthChanged =
                    Math.Abs(
                        requestedWidth -
                        selectedObject.Width) > 0.001;

                bool heightChanged =
                    Math.Abs(
                        requestedHeight -
                        selectedObject.Height) > 0.001;

                if (widthChanged && !heightChanged)
                {
                    requestedHeight =
                        requestedWidth /
                        aspectRatio;
                }
                else if (heightChanged && !widthChanged)
                {
                    requestedWidth =
                        requestedHeight *
                        aspectRatio;
                }
                else if (widthChanged && heightChanged)
                {
                    // Ha mindkét mezőt átírta, a szélesség legyen a vezérlő érték.
                    requestedHeight =
                        requestedWidth /
                        aspectRatio;
                }
            }


            AssetResizeMode widthMode =
                WidthModeComboBox.SelectedIndex == 1
                    ? AssetResizeMode.Repeat
                    : AssetResizeMode.Stretch;


            AssetResizeMode heightMode =
                HeightModeComboBox.SelectedIndex == 1
                    ? AssetResizeMode.Repeat
                    : AssetResizeMode.Stretch;


            double finalWidth =
                widthMode ==
                AssetResizeMode.Repeat
                    ? RoundUpToAssetSize(
                        requestedWidth,
                        asset.GameWidth)
                    : requestedWidth;


            double finalHeight =
                heightMode ==
                AssetResizeMode.Repeat
                    ? RoundUpToAssetSize(
                        requestedHeight,
                        asset.GameHeight)
                    : requestedHeight;


            double maximumObjectWidth =
                WorldCanvas.Width;

            if (finalWidth >
                maximumObjectWidth)
            {
                if (KeepAspectRatioCheckBox.IsChecked == true)
                {
                    double scale =
                        maximumObjectWidth /
                        finalWidth;

                    finalHeight *=
                        scale;
                }

                finalWidth =
                    maximumObjectWidth;
            }


            x =
                Math.Clamp(
                    x,
                    0,
                    Math.Max(
                        0,
                        WorldCanvas.Width -
                        finalWidth));


            y =
                Math.Clamp(
                    y,
                    0,
                    Math.Max(
                        0,
                        WorldCanvas.Height -
                        finalHeight));


            EditorSnapshot beforeChange =
                CaptureEditorSnapshot();

            selectedObject.Width =
                finalWidth;

            selectedObject.Height =
                finalHeight;

            selectedObject.WidthMode =
                widthMode;

            selectedObject.HeightMode =
                heightMode;

            SetObjectPosition(
                selectedObject,
                x,
                y,
                snap: false);

            RenderMapObject(
                selectedObject);

            SelectObject(
                selectedObject);


            StatusText.Text =
                $"Objektum alkalmazva — {finalWidth:0.##} × {finalHeight:0.##}";

            CommitHistory(beforeChange);
        }


        private double RoundUpToAssetSize(
            double requested,
            double nativeSize)
        {
            if (nativeSize <= 0)
                return requested;


            double count =
                Math.Ceiling(
                    requested /
                    nativeSize);


            return Math.Max(
                nativeSize,
                count *
                nativeSize);
        }


        // =====================================================
        // GAME HUD / BORDER PREVIEW
        // =====================================================

        private void GridToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (MapGridOverlay != null)
                MapGridOverlay.Visibility = GridToggle.IsChecked == true
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowHudCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (GameHudPreview == null)
                return;

            GameHudPreview.Visibility =
                ShowHudCheckBox.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            UpdateHudPreview();
        }


        private void UpdateHudPreview()
        {
            if (GameHudPreview == null ||
                WorldCanvas == null ||
                MapScrollViewer == null)
            {
                return;
            }

            double mapHeight =
                WorldCanvas.Height;

            if (double.IsNaN(mapHeight) ||
                mapHeight <= 0)
            {
                return;
            }

            // A HUD magassága továbbra is a teljes pályamagasság,
            // a szélessége viszont az editor ténylegesen látható
            // canvas/viewport szélességéhez igazodik. Így nem lóghat
            // rá a jobb oldali Inspector panelre.
            double logicalViewportWidth =
                currentZoom <= 0
                    ? MapScrollViewer.ViewportWidth
                    : MapScrollViewer.ViewportWidth /
                      currentZoom;

            double hudWidth =
                Math.Min(
                    WorldCanvas.Width,
                    logicalViewportWidth);

            GameHudPreview.Width =
                hudWidth;

            GameHudPreview.Height =
                mapHeight;

            // Kamera-space overlay: a vízszintes viewportot követi.
            double logicalViewportX =
                currentZoom <= 0
                    ? MapScrollViewer.HorizontalOffset
                    : MapScrollViewer.HorizontalOffset /
                      currentZoom;

            double maxX =
                Math.Max(
                    0,
                    WorldCanvas.Width -
                    hudWidth);

            logicalViewportX =
                Math.Clamp(
                    logicalViewportX,
                    0,
                    maxX);

            GameHudPreview.Margin =
                new Thickness(
                    logicalViewportX,
                    0,
                    0,
                    0);
        }


        // =====================================================
        // VIEWPORT ZOOM / PARALLAX PREVIEW
        // =====================================================

        private void MapScrollViewer_ScrollChanged(
            object sender,
            ScrollChangedEventArgs e)
        {
            UpdateParallaxTransforms();
            UpdateHudPreview();
        }


        private void UpdateParallaxTransforms()
        {
            if (WorldCanvas == null ||
                MapScrollViewer == null)
            {
                return;
            }

            foreach (MapObject mapObject
                     in mapObjects)
            {
                if (objectViews.TryGetValue(
                    mapObject.Id,
                    out Border? border))
                {
                    ApplyParallaxTransform(
                        border,
                        mapObject.Layer);
                }

                string fillTag =
                    $"GROUND_FILL_{mapObject.Id}";

                FrameworkElement? fill =
                    WorldCanvas.Children
                        .OfType<FrameworkElement>()
                        .FirstOrDefault(
                            x =>
                                x.Tag as string ==
                                fillTag);

                if (fill != null)
                {
                    ApplyParallaxTransform(
                        fill,
                        mapObject.Layer);
                }
            }

            if (DropPreview.Visibility ==
                Visibility.Visible)
            {
                ApplyParallaxTransform(
                    DropPreview,
                    selectedLayer?.Name
                    ?? "Layer 1");
            }
        }


        private void ApplyParallaxTransform(
            UIElement element,
            string layerName)
        {
            element.RenderTransform =
                new TranslateTransform(
                    GetParallaxContentOffset(
                        layerName),
                    0);
        }


        private double GetParallaxContentOffset(
            string? layerName)
        {
            if (MapScrollViewer == null)
                return 0;

            double value =
                GetLayer(layerName).Value;

            // A ScrollViewer minden layert 1.0 sebességgel húzna.
            // Ezzel visszaadjuk a különbséget:
            // képernyőn a layer tényleges elmozdulása = scroll * value.
            double unscaledHorizontalOffset =
                currentZoom <= 0
                    ? MapScrollViewer.HorizontalOffset
                    : MapScrollViewer.HorizontalOffset /
                      currentZoom;

            return unscaledHorizontalOffset *
                   (1.0 - value);
        }


        private void MapScrollViewer_PreviewMouseWheel(
            object sender,
            MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers &
                 ModifierKeys.Control) == 0)
            {
                return;
            }

            double[] zoomLevels =
            {
                0.5,
                0.75,
                0.9,
                1.0,
                1.25,
                1.5,
                2.0
            };

            int currentIndex = 0;
            double bestDistance =
                double.MaxValue;

            for (int i = 0;
                 i < zoomLevels.Length;
                 i++)
            {
                double distance =
                    Math.Abs(
                        zoomLevels[i] -
                        currentZoom);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    currentIndex = i;
                }
            }

            int nextIndex =
                e.Delta > 0
                    ? Math.Min(
                        zoomLevels.Length - 1,
                        currentIndex + 1)
                    : Math.Max(
                        0,
                        currentIndex - 1);

            SetZoom(
                zoomLevels[nextIndex]);

            e.Handled = true;
        }


        private void ZoomComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isChangingZoom ||
                ZoomComboBox?.SelectedItem
                is not ComboBoxItem item)
            {
                return;
            }

            string? tag =
                item.Tag?.ToString();

            if (double.TryParse(
                    tag,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double zoom))
            {
                SetZoom(zoom);
            }
        }


        private void SetZoom(
            double zoom)
        {
            zoom =
                Math.Clamp(
                    zoom,
                    0.5,
                    2.0);

            if (Math.Abs(
                    currentZoom -
                    zoom) < 0.0001)
            {
                return;
            }

            double oldZoom =
                currentZoom;

            double logicalCenterX =
                (MapScrollViewer.HorizontalOffset +
                 MapScrollViewer.ViewportWidth / 2.0) /
                oldZoom;

            double logicalCenterY =
                (MapScrollViewer.VerticalOffset +
                 MapScrollViewer.ViewportHeight / 2.0) /
                oldZoom;

            currentZoom = zoom;

            WorldZoomTransform.ScaleX =
                currentZoom;

            WorldZoomTransform.ScaleY =
                currentZoom;

            SelectZoomComboItem(
                currentZoom);

            Dispatcher.BeginInvoke(
                new Action(
                    () =>
                    {
                        double newHorizontal =
                            logicalCenterX *
                            currentZoom -
                            MapScrollViewer.ViewportWidth /
                            2.0;

                        double newVertical =
                            logicalCenterY *
                            currentZoom -
                            MapScrollViewer.ViewportHeight /
                            2.0;

                        MapScrollViewer.ScrollToHorizontalOffset(
                            Math.Max(
                                0,
                                newHorizontal));

                        MapScrollViewer.ScrollToVerticalOffset(
                            Math.Max(
                                0,
                                newVertical));

                        UpdateParallaxTransforms();
                        UpdateHudPreview();
                    }));
        }


        private void SelectZoomComboItem(
            double zoom)
        {
            if (ZoomComboBox == null)
                return;

            isChangingZoom = true;

            try
            {
                for (int i = 0;
                     i < ZoomComboBox.Items.Count;
                     i++)
                {
                    if (ZoomComboBox.Items[i]
                        is not ComboBoxItem item)
                    {
                        continue;
                    }

                    if (!double.TryParse(
                            item.Tag?.ToString(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double itemZoom))
                    {
                        continue;
                    }

                    if (Math.Abs(
                            itemZoom -
                            zoom) < 0.0001)
                    {
                        ZoomComboBox.SelectedIndex =
                            i;

                        break;
                    }
                }
            }
            finally
            {
                isChangingZoom = false;
            }
        }


        // =====================================================
        // LAYERS / PARALLAX
        // =====================================================

        private void RefreshLayers(
            string? selectLayerName = null)
        {
            if (LayersListBox == null)
            {
                return;
            }

            string? wantedName =
                selectLayerName
                ?? selectedLayer?.Name
                ?? "Layer 1";

            LayersListBox.Items.Clear();

            foreach (LayerModel layer in
                     LayerDatabase.Layers
                         .OrderByDescending(x => x.Value)
                         .ThenBy(x => x.Name))
            {
                ListBoxItem item =
                    new()
                    {
                        Tag = layer,
                        Padding = new Thickness(8, 6, 8, 6),
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch
                    };

                Grid row = new();

                row.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(
                            1,
                            GridUnitType.Star)
                    });

                row.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = GridLength.Auto
                    });

                TextBlock nameText =
                    new()
                    {
                        Text = layer.Name,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    };

                Grid.SetColumn(nameText, 0);
                row.Children.Add(nameText);

                StackPanel right =
                    new()
                    {
                        Orientation =
                            Orientation.Horizontal,
                        HorizontalAlignment =
                            HorizontalAlignment.Right
                    };

                Border valueBadge =
                    new()
                    {
                        BorderBrush =
                            (Brush)FindResource(
                                "BorderBrush"),
                        BorderThickness =
                            new Thickness(1),
                        CornerRadius =
                            new CornerRadius(3),
                        Padding =
                            new Thickness(7, 2, 7, 2),
                        Margin =
                            new Thickness(12, 0, 8, 0),
                        VerticalAlignment =
                            VerticalAlignment.Center
                    };

                valueBadge.Child =
                    new TextBlock
                    {
                        Text =
                            layer.Value.ToString("0.###"),
                        Foreground =
                            (Brush)FindResource(
                                "MutedTextBrush"),
                        VerticalAlignment =
                            VerticalAlignment.Center
                    };

                right.Children.Add(
                    valueBadge);

                Button editButton =
                    new()
                    {
                        Content = "✎",
                        Width = 26,
                        Height = 24,
                        Padding = new Thickness(0),
                        Tag = layer
                    };

                editButton.Click +=
                    LayerEditButton_Click;

                right.Children.Add(editButton);

                Grid.SetColumn(right, 1);
                row.Children.Add(right);

                item.Content = row;

                LayersListBox.Items.Add(item);

                if (string.Equals(
                    layer.Name,
                    wantedName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    LayersListBox.SelectedItem =
                        item;
                }

            }

            if (LayersListBox.SelectedItem == null &&
                LayersListBox.Items.Count > 0)
            {
                LayersListBox.SelectedIndex = 0;
            }

            UpdateLayerVisibility();
        }


        private void BackgroundColorButton_Click(object sender, RoutedEventArgs e)
        {
            Color? current = (MapBackgroundRectangle.Fill as SolidColorBrush)?.Color;
            BackgroundColorWindow dialog = new(current) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            Color color = dialog.SelectedColor;
            MapBackgroundRectangle.Fill = new SolidColorBrush(color);
            BackgroundColorSwatch.Background = new SolidColorBrush(color);
            StatusText.Text = $"Háttérszín: RGB({color.R}, {color.G}, {color.B})";
        }

        private void AddLayerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LayerEditorWindow window =
                new()
                {
                    Owner = this
                };

            if (window.ShowDialog() == true &&
                window.SavedLayer != null)
            {
                RefreshLayers(
                    window.SavedLayer.Name);
                RefreshPlacedObjectsTree();
            }
        }


        private void LayerEditButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not LayerModel layer)
            {
                return;
            }

            string oldName = layer.Name;

            LayerEditorWindow window =
                new(layer)
                {
                    Owner = this
                };

            if (window.ShowDialog() == true)
            {
                if (window.LayerDeleted)
                {
                    // A törölt layer objektumai ne vesszenek el:
                    // átkerülnek a megmaradt, legnagyobb értékű layerre.
                    LayerModel fallbackLayer =
                        LayerDatabase.Layers
                            .OrderByDescending(x => x.Value)
                            .ThenBy(x => x.Name)
                            .First();

                    EditorSnapshot beforeChange =
                        CaptureEditorSnapshot();

                    foreach (MapObject mapObject
                             in mapObjects.Where(
                                 x =>
                                     string.Equals(
                                         x.Layer,
                                         oldName,
                                         StringComparison.OrdinalIgnoreCase)))
                    {
                        mapObject.Layer =
                            fallbackLayer.Name;
                    }

                    selectedLayer =
                        fallbackLayer;

                    RefreshLayers(
                        fallbackLayer.Name);

                    RerenderAllMapObjects();
                    RefreshPlacedObjectsTree();

                    CommitHistory(
                        beforeChange);

                    StatusText.Text =
                        $"Layer törölve: {oldName}. " +
                        $"Az objektumai ide kerültek: {fallbackLayer.Name}";
                }
                else if (window.SavedLayer != null)
                {
                    string newName =
                        window.SavedLayer.Name;

                    if (!string.Equals(
                        oldName,
                        newName,
                        StringComparison.Ordinal))
                    {
                        foreach (MapObject mapObject
                                 in mapObjects.Where(
                                     x =>
                                         string.Equals(
                                             x.Layer,
                                             oldName,
                                             StringComparison.OrdinalIgnoreCase)))
                        {
                            mapObject.Layer =
                                newName;
                        }
                    }

                    RefreshLayers(newName);
                    RerenderAllMapObjects();
                    RefreshPlacedObjectsTree();
                }
            }

            e.Handled = true;
        }


        private void LayersListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (LayersListBox.SelectedItem
                is ListBoxItem item &&
                item.Tag is LayerModel layer)
            {
                selectedLayer = layer;

                StatusText.Text =
                    $"Aktív layer: {layer.Name} ({layer.Value:0.###})";

                UpdateLayerVisibility();
                UpdateParallaxTransforms();
            }
        }


        private void OtherLayersVisibilityComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateLayerVisibility();
        }


        private void UpdateLayerVisibility()
        {
            if (WorldCanvas == null)
                return;

            foreach (MapObject mapObject
                     in mapObjects)
            {
                ApplyLayerVisibilityToObject(
                    mapObject);
            }

            UpdateLayerDrawOrder();
        }

        // The layer Value controls depth, independently of the active layer
        // and the three layer-visibility modes. Lower Value = further back.
        // Within one layer, preserve the objects' own ZIndex order.
        // Ground fill belongs immediately behind its owning sprite.
        private void UpdateLayerDrawOrder()
        {
            if (WorldCanvas == null)
                return;

            int order = 100;
            foreach (MapObject mapObject in mapObjects
                .OrderBy(x => GetLayer(x.Layer).Value)
                .ThenBy(x => x.ZIndex)
                .ThenBy(x => x.Id))
            {
                string fillTag = $"GROUND_FILL_{mapObject.Id}";
                FrameworkElement? fill = WorldCanvas.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(x => x.Tag as string == fillTag);

                if (fill != null)
                    Panel.SetZIndex(fill, order++);

                if (objectViews.TryGetValue(mapObject.Id, out Border? border))
                    Panel.SetZIndex(border, order++);
            }
        }


        private void ApplyLayerVisibilityToObject(
            MapObject mapObject)
        {
            if (objectViews.TryGetValue(
                mapObject.Id,
                out Border? border))
            {
                ApplyLayerVisibilityToElement(
                    border,
                    mapObject.Layer);
            }

            string fillTag =
                $"GROUND_FILL_{mapObject.Id}";

            FrameworkElement? fill =
                WorldCanvas.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(
                        x =>
                            x.Tag as string ==
                            fillTag);

            if (fill != null)
            {
                ApplyLayerVisibilityToElement(
                    fill,
                    mapObject.Layer);
            }
        }


        private void ApplyLayerVisibilityToElement(UIElement element, string layerName)
        {
            bool active = selectedLayer == null ||
                string.Equals(selectedLayer.Name, layerName, StringComparison.OrdinalIgnoreCase);

            // 0: minden layer teljesen látható
            // 1: más layerek halványak (korábbi alapértelmezett)
            // 2: más layerek rejtettek
            int mode = OtherLayersVisibilityComboBox?.SelectedIndex ?? 1;
            element.Visibility = !active && mode == 2
                ? Visibility.Collapsed
                : Visibility.Visible;
            element.Opacity = active || mode == 0 ? 1.0 : OtherLayerOpacity;
        }


        private LayerModel GetLayer(
            string? layerName)
        {
            LayerModel? layer =
                LayerDatabase.Layers
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                layerName,
                                StringComparison.OrdinalIgnoreCase));

            return layer
                ?? LayerDatabase.Layers
                    .First();
        }








        private void RerenderAllMapObjects()
        {
            foreach (MapObject mapObject in mapObjects.ToList())
            {
                Point position = GetObjectPosition(
                    mapObject, mapObject.X, mapObject.Y, snap: false);

                mapObject.X = position.X;
                mapObject.Y = position.Y;
                RenderMapObject(mapObject);
            }

            UpdateLayerVisibility();
            UpdateParallaxTransforms();
            UpdateInspector();
        }


        // =====================================================
        // MAP SIZE
        // =====================================================

        private void ApplyMapSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetPositiveNumber(
                    MapWidthTextBox, "A pálya szélessége pozitív szám legyen.", out double width) ||
                !TryGetPositiveNumber(
                    MapHeightTextBox, "A pálya magassága pozitív szám legyen.", out double height))
                return;

            EditorSnapshot beforeChange = CaptureEditorSnapshot();
            ApplyMapSize(width, height);

            foreach (MapObject mapObject in mapObjects)
            {
                AssetModel? asset = GetAsset(mapObject);
                if (asset == null)
                    continue;

                string path = EditorStorage.GetAssetImagePath(asset);
                RenderGroundFill(mapObject, asset, path);
            }

            StatusText.Text = $"Pályaméret alkalmazva: {width:0.##} × {height:0.##}";
            CommitHistory(beforeChange);
        }

        private bool TryGetPositiveNumber(TextBox textBox, string error, out double value)
        {
            if (double.TryParse(textBox.Text, out value) && value > 0)
                return true;

            MessageBox.Show(
                error,
                "Hibás pályaméret",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }


        private void ApplyMapSize(
            double width,
            double height)
        {
            WorldContainer.Width =
                width;

            WorldContainer.Height =
                height;

            WorldCanvas.Width =
                width;

            WorldCanvas.Height =
                height;

            MapBoundaryBorder.Width =
                width;

            MapBoundaryBorder.Height =
                height;

            UpdateHudPreview();

            if (LayerDatabase.Layers.Count > 0 &&
                mapObjects.Count > 0 &&
                !isRestoringHistory)
            {
                RerenderAllMapObjects();
            }
        }


        private void UpdateWorldCanvasSize()
        {
            double width = double.TryParse(MapWidthTextBox.Text, out double w) && w > 0 ? w : 5000;
            double height = double.TryParse(MapHeightTextBox.Text, out double h) && h > 0 ? h : 1080;
            ApplyMapSize(width, height);
        }


        // =====================================================
        // PLACED OBJECT LIST
        // =====================================================

        private void RefreshPlacedObjectsTree()
        {
            if (PlacedObjectsTreeView == null)
                return;

            isRefreshingObjectTree = true;

            try
            {
                PlacedObjectsTreeView.Items.Clear();

                IEnumerable<LayerModel> orderedLayers =
                    LayerDatabase.Layers
                        .OrderByDescending(x => x.Value)
                        .ThenBy(x => x.Name);

                foreach (LayerModel layer in orderedLayers)
                {
                    TreeViewItem layerItem =
                        new()
                        {
                            Tag = layer,
                            IsExpanded = true,
                            AllowDrop = true
                        };

                    StackPanel layerHeader =
                        new()
                        {
                            Orientation =
                                Orientation.Horizontal
                        };

                    layerHeader.Children.Add(
                        new TextBlock
                        {
                            Text = layer.Name,
                            FontWeight =
                                FontWeights.SemiBold
                        });

                    Border valueBadge =
                        new()
                        {
                            BorderBrush =
                                (Brush)FindResource(
                                    "BorderBrush"),
                            BorderThickness =
                                new Thickness(1),
                            CornerRadius =
                                new CornerRadius(3),
                            Padding =
                                new Thickness(6, 1, 6, 1),
                            Margin =
                                new Thickness(8, 0, 0, 0)
                        };

                    valueBadge.Child =
                        new TextBlock
                        {
                            Text =
                                layer.Value.ToString(
                                    "0.###"),
                            Foreground =
                                (Brush)FindResource(
                                    "MutedTextBrush"),
                            FontSize = 10
                        };

                    layerHeader.Children.Add(
                        valueBadge);

                    layerItem.Header =
                        layerHeader;

                    IEnumerable<MapObject> objects =
                        mapObjects
                            .Where(
                                x =>
                                    string.Equals(
                                        x.Layer,
                                        layer.Name,
                                        StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(
                                x => x.ZIndex)
                            .ThenBy(
                                x => GetAsset(x)?.Name);

                    foreach (MapObject mapObject
                             in objects)
                    {
                        AssetModel? asset =
                            GetAsset(mapObject);

                        TreeViewItem objectItem =
                            new()
                            {
                                Tag = mapObject
                            };

                        StackPanel objectHeader =
                            new()
                            {
                                Orientation =
                                    Orientation.Horizontal
                            };

                        objectHeader.Children.Add(
                            new TextBlock
                            {
                                Text =
                                    asset?.Name ??
                                    "Ismeretlen asset"
                            });

                        TextBlock xText =
                            new()
                            {
                                Margin =
                                    new Thickness(
                                        10,
                                        0,
                                        0,
                                        0)
                            };

                        xText.SetBinding(
                            TextBlock.TextProperty,
                            new Binding(nameof(MapObject.X))
                            {
                                Source = mapObject,
                                StringFormat = "X:{0:0.#}"
                            });

                        TextBlock yText =
                            new()
                            {
                                Margin =
                                    new Thickness(
                                        8,
                                        0,
                                        0,
                                        0)
                            };

                        yText.SetBinding(
                            TextBlock.TextProperty,
                            new Binding(nameof(MapObject.Y))
                            {
                                Source = mapObject,
                                StringFormat = "Y:{0:0.#}"
                            });

                        objectHeader.Children.Add(
                            xText);

                        objectHeader.Children.Add(
                            yText);

                        // Saját kijelölési háttér: több elem is kiemelhető egyszerre.
                        objectItem.Header = new Border
                        {
                            Background = Brushes.Transparent,
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(4, 2, 4, 2),
                            Child = objectHeader
                        };

                        layerItem.Items.Add(
                            objectItem);
                    }

                    PlacedObjectsTreeView.Items.Add(
                        layerItem);
                }

                SyncObjectTreeSelection();
            }
            finally
            {
                isRefreshingObjectTree = false;
            }
        }


        private void SyncObjectTreeSelection()
        {
            if (PlacedObjectsTreeView == null ||
                isRefreshingObjectTree)
            {
                return;
            }

            isRefreshingObjectTree = true;

            try
            {
                foreach (object root
                         in PlacedObjectsTreeView.Items)
                {
                    if (root is not TreeViewItem layerItem)
                        continue;

                    foreach (object child
                             in layerItem.Items)
                    {
                        if (child is not TreeViewItem objectItem ||
                            objectItem.Tag is not MapObject mapObject)
                        {
                            continue;
                        }

                        bool shouldBeSelected = selectedObjectIds.Contains(mapObject.Id);

                        // A TreeView natívan csak egyetlen elemet tud kijelölni.
                        // A többes kijelölést a fejléc háttérszíne jelzi.
                        if (objectItem.Header is Border highlight)
                        {
                            highlight.Background = shouldBeSelected
                                ? new SolidColorBrush(Color.FromRgb(38, 50, 68))
                                : Brushes.Transparent;
                        }

                        // A natív kijelölés csak az egyetlen kijelölt elemnél aktív.
                        // Többes kijelölésnél nem hagyjuk, hogy a TreeView
                        // automatikusan törölje a többi elem vizuális állapotát.
                        objectItem.IsSelected = shouldBeSelected && selectedObjectIds.Count == 1;

                    }
                }
            }
            finally
            {
                isRefreshingObjectTree = false;
            }
        }


        private void PlacedObjectsTreeView_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            objectTreeDragStartPoint =
                e.GetPosition(
                    PlacedObjectsTreeView);

            TreeViewItem? item =
                FindTreeViewItem(
                    e.OriginalSource
                    as DependencyObject);

            if (item?.Tag is MapObject mapObject)
            {
                objectTreeDragObject = mapObject;

                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    ToggleObjectSelection(mapObject);
                    e.Handled = true;
                }
                else if (selectedObjectIds.Count > 1 &&
                         selectedObjectIds.Contains(mapObject.Id))
                {
                    // Többes kijelölésnél a húzás kezdete ne törölje a csoportot.
                    e.Handled = true;
                }
            }
            else
            {
                objectTreeDragObject =
                    null;
            }
        }


        private void PlacedObjectsTreeView_SelectedItemChanged(
            object sender,
            RoutedPropertyChangedEventArgs<object> e)
        {
            if (isRefreshingObjectTree)
                return;

            if (e.NewValue is TreeViewItem item &&
                item.Tag is MapObject mapObject)
            {
                // Ugyanaz az egyetlen kijelölési mechanika,
                // mint amikor közvetlenül a canvason kattintunk.
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    ToggleObjectSelection(mapObject);
                else
                    SelectObject(mapObject);
            }
        }


        private void PlacedObjectsTreeView_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
        {
            if (e.LeftButton !=
                MouseButtonState.Pressed ||
                objectTreeDragObject == null)
            {
                return;
            }

            Point current =
                e.GetPosition(
                    PlacedObjectsTreeView);

            Vector delta =
                current -
                objectTreeDragStartPoint;

            if (Math.Abs(delta.X) <
                    SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) <
                    SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            DragDrop.DoDragDrop(
                PlacedObjectsTreeView,
                objectTreeDragObject,
                DragDropEffects.Move);

            objectTreeDragObject =
                null;
        }


        private void PlacedObjectsTreeView_DragOver(
            object sender,
            DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(
                    typeof(MapObject)))
            {
                e.Effects =
                    DragDropEffects.None;

                e.Handled = true;
                return;
            }

            TreeViewItem? targetItem =
                FindTreeViewItem(
                    e.OriginalSource
                    as DependencyObject);

            LayerModel? targetLayer =
                GetLayerFromTreeItem(
                    targetItem);

            e.Effects =
                targetLayer != null
                    ? DragDropEffects.Move
                    : DragDropEffects.None;

            e.Handled = true;
        }


        private void PlacedObjectsTreeView_Drop(
            object sender,
            DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(MapObject)))
                return;

            MapObject? draggedObject = e.Data.GetData(typeof(MapObject)) as MapObject;
            TreeViewItem? targetItem = FindTreeViewItem(e.OriginalSource as DependencyObject);
            LayerModel? targetLayer = GetLayerFromTreeItem(targetItem);

            if (draggedObject == null || targetLayer == null)
                return;

            // Ha egy kijelölt csoport tagját húzzuk, az egész csoportot visszük.
            // Kijelölésen kívüli objektum húzásakor csak azt az egyet.
            List<MapObject> movingObjects =
                selectedObjectIds.Count > 1 && selectedObjectIds.Contains(draggedObject.Id)
                    ? mapObjects.Where(o => selectedObjectIds.Contains(o.Id)).ToList()
                    : new List<MapObject> { draggedObject };

            movingObjects = movingObjects
                .Where(o => !string.Equals(o.Layer, targetLayer.Name,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (movingObjects.Count == 0)
            {
                e.Handled = true;
                return;
            }

            EditorSnapshot beforeChange = CaptureEditorSnapshot();

            foreach (MapObject obj in movingObjects)
            {
                obj.Layer = targetLayer.Name;
                SetObjectPosition(obj, obj.X, obj.Y, snap: false);
                RenderMapObject(obj);
            }

            // A kijelölést megtartjuk, hogy a csoport tovább szerkeszthető legyen.
            UpdateObjectSelectionVisuals();
            RefreshPlacedObjectsTree();
            UpdateParallaxTransforms();

            StatusText.Text = $"{movingObjects.Count} objektum áthelyezve: {targetLayer.Name}";
            CommitHistory(beforeChange);
            e.Handled = true;
        }


        private TreeViewItem? FindTreeViewItem(
            DependencyObject? source)
        {
            DependencyObject? current =
                source;

            while (current != null)
            {
                if (current is TreeViewItem item)
                    return item;

                current =
                    VisualTreeHelper.GetParent(
                        current);
            }

            return null;
        }


        private LayerModel? GetLayerFromTreeItem(
            TreeViewItem? item)
        {
            if (item == null)
                return null;

            if (item.Tag is LayerModel layer)
                return layer;

            if (item.Tag is MapObject)
            {
                ItemsControl? parent =
                    ItemsControl.ItemsControlFromItemContainer(
                        item);

                if (parent is TreeViewItem layerItem &&
                    layerItem.Tag is LayerModel parentLayer)
                {
                    return parentLayer;
                }
            }

            return null;
        }


        // =====================================================
        // DELETE / COPY / CUT / PASTE
        // =====================================================




        private void CopySelectedObject()
        {
            if (selectedObject == null)
                return;

            objectClipboard = selectedObject.Clone();
            clipboardIsCut = false;
            StatusText.Text = "Objektum másolva — Ctrl+V a beillesztéshez.";
        }


        private void CutSelectedObject()
        {
            if (selectedObject == null)
                return;

            objectClipboard = selectedObject.Clone();
            clipboardIsCut = true;
            DeleteSelectedObject("Objektum kivágva — Ctrl+V a beillesztéshez.");
        }


        private void PasteObject()
        {
            if (objectClipboard == null)
                return;

            EditorSnapshot beforeChange = CaptureEditorSnapshot();
            string targetLayer = selectedLayer?.Name ?? objectClipboard.Layer;
            double offset = clipboardIsCut ? 0 : 12;

            MapObject pasted = objectClipboard.Clone(newId: true);
            pasted.Layer = targetLayer;

            Point position = GetObjectPosition(
                pasted, pasted.X + offset, pasted.Y + offset, snap: false);

            pasted.X = position.X;
            pasted.Y = position.Y;

            mapObjects.Add(pasted);
            RenderMapObject(pasted);
            SelectObject(pasted);
            EmptyViewportMessage.Visibility = Visibility.Collapsed;
            RefreshPlacedObjectsTree();

            StatusText.Text = clipboardIsCut
                ? $"Objektum beillesztve — {targetLayer}"
                : $"Másolat beillesztve — {targetLayer}";

            objectClipboard = pasted.Clone();
            clipboardIsCut = false;
            CommitHistory(beforeChange);
        }


        private void DeleteSelectedObject(
            string? statusText = null)
        {
            if (selectedObject == null)
                return;

            EditorSnapshot beforeChange =
                CaptureEditorSnapshot();

            Guid id =
                selectedObject.Id;

            if (objectViews.TryGetValue(
                    id,
                    out Border? border))
            {
                WorldCanvas.Children.Remove(
                    border);

                objectViews.Remove(
                    id);
            }

            string fillTag =
                $"GROUND_FILL_{id}";

            FrameworkElement? fill =
                WorldCanvas.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(
                        x =>
                            x.Tag as string ==
                            fillTag);

            if (fill != null)
            {
                WorldCanvas.Children.Remove(
                    fill);
            }

            mapObjects.RemoveAll(
                x => x.Id == id);

            selectedObject = null;
            selectedObjectIds.Clear();

            UpdateInspector();
            RefreshPlacedObjectsTree();

            EmptyViewportMessage.Visibility =
                mapObjects.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            StatusText.Text =
                statusText ??
                "Objektum törölve.";

            CommitHistory(
                beforeChange);
        }


        // =====================================================
        // UNDO / REDO
        // =====================================================

        private void MapEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            Action? action = (ctrl, e.Key) switch
            {
                (true, Key.Z) => Undo,
                (true, Key.Y) => Redo,
                (true, Key.C) => CopySelectedObject,
                (true, Key.X) => CutSelectedObject,
                (true, Key.V) => PasteObject,
                (false, Key.Delete) => () => DeleteSelectedObject(),
                _ => null
            };

            if (action == null)
                return;

            action();
            e.Handled = true;
        }


        private EditorSnapshot CaptureEditorSnapshot()
        {
            return new EditorSnapshot
            {
                MapWidth = WorldCanvas.Width,
                MapHeight = WorldCanvas.Height,
                SelectedObjectId = selectedObject?.Id,
                Objects = mapObjects.Select(x => x.Clone()).ToList()
            };
        }


        private void CommitHistory(
            EditorSnapshot beforeChange)
        {
            if (isRestoringHistory)
                return;

            EditorSnapshot afterChange =
                CaptureEditorSnapshot();

            if (SnapshotsEqual(
                beforeChange,
                afterChange))
            {
                return;
            }

            undoStack.Push(beforeChange);
            redoStack.Clear();
        }


        private void Undo()
        {
            NavigateHistory(undoStack, redoStack,
                "Nincs visszavonható művelet.", "Visszavonás — Ctrl+Z");
        }


        private void Redo()
        {
            NavigateHistory(redoStack, undoStack,
                "Nincs újra végrehajtható művelet.", "Újra — Ctrl+Y");
        }

        private void NavigateHistory(
            Stack<EditorSnapshot> source,
            Stack<EditorSnapshot> destination,
            string emptyMessage,
            string successMessage)
        {
            if (source.Count == 0)
            {
                StatusText.Text = emptyMessage;
                return;
            }

            destination.Push(CaptureEditorSnapshot());
            RestoreEditorSnapshot(source.Pop());
            StatusText.Text = successMessage;
        }


        private void RestoreEditorSnapshot(EditorSnapshot snapshot)
        {
            isRestoringHistory = true;
            try
            {
                movingObject = null;
                movingObjectView = null;
                isMovingObject = false;
                objectDragStartSnapshot = null;

                ApplyMapSize(snapshot.MapWidth, snapshot.MapHeight);
                MapWidthTextBox.Text = snapshot.MapWidth.ToString("0.##");
                MapHeightTextBox.Text = snapshot.MapHeight.ToString("0.##");

                mapObjects.Clear();
                selectedObjectIds.Clear();
                objectViews.Clear();

                List<UIElement> generatedElements = WorldCanvas.Children
                    .Cast<UIElement>()
                    .Where(x =>
                        x is Border border && border.Tag is MapObject ||
                        x is FrameworkElement element &&
                        element.Tag is string tag &&
                        tag.StartsWith("GROUND_FILL_", StringComparison.Ordinal))
                    .ToList();

                foreach (UIElement element in generatedElements)
                    WorldCanvas.Children.Remove(element);

                foreach (MapObject saved in snapshot.Objects)
                {
                    MapObject mapObject = saved.Clone();

                    if (!LayerDatabase.Layers.Any(x =>
                            string.Equals(x.Name, mapObject.Layer, StringComparison.OrdinalIgnoreCase)))
                        mapObject.Layer = "Layer 1";

                    mapObjects.Add(mapObject);
                    RenderMapObject(mapObject);
                }

                EmptyViewportMessage.Visibility =
                    mapObjects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                MapObject? objectToSelect = snapshot.SelectedObjectId.HasValue
                    ? mapObjects.FirstOrDefault(x => x.Id == snapshot.SelectedObjectId.Value)
                    : null;

                SelectObject(objectToSelect);
                RefreshPlacedObjectsTree();
            }
            finally
            {
                isRestoringHistory = false;
            }
        }


        private bool SnapshotsEqual(EditorSnapshot left, EditorSnapshot right)
        {
            return left.MapWidth == right.MapWidth &&
                   left.MapHeight == right.MapHeight &&
                   left.Objects.Count == right.Objects.Count &&
                   left.Objects.Zip(right.Objects, (a, b) => a.StateEquals(b)).All(x => x);
        }


        // =====================================================
        // HELPERS
        // =====================================================

        private AssetModel? GetAsset(
            MapObject mapObject)
        {
            return AssetDatabase.Assets
                .FirstOrDefault(
                    x =>
                        x.Id ==
                        mapObject.AssetId);
        }


        private string? GetComboBoxText(
            ComboBox comboBox)
        {
            if (comboBox.SelectedItem
                is ComboBoxItem item)
            {
                return item.Content?.ToString();
            }


            return comboBox
                .SelectedItem?
                .ToString();
        }


        private void SetComboBoxSelectionByText(
            ComboBox comboBox,
            string value)
        {
            for (int i = 0;
                 i < comboBox.Items.Count;
                 i++)
            {
                if (comboBox.Items[i]
                    is ComboBoxItem item &&
                    string.Equals(
                        item.Content?.ToString(),
                        value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex =
                        i;

                    return;
                }
            }


            comboBox.SelectedIndex = 0;
        }


        // =====================================================
        // BOTTOM ROW COLOR
        // =====================================================

        private Color GetDominantBottomRowColor(
            string imagePath)
        {
            BitmapImage bitmap =
                LoadBitmap(imagePath);


            FormatConvertedBitmap converted =
                new(
                    bitmap,
                    PixelFormats.Bgra32,
                    null,
                    0);


            int width =
                converted.PixelWidth;

            int height =
                converted.PixelHeight;


            if (width <= 0 ||
                height <= 0)
            {
                return Colors.Black;
            }


            int stride =
                width * 4;


            byte[] pixels =
                new byte[stride];


            converted.CopyPixels(
                new Int32Rect(
                    0,
                    height - 1,
                    width,
                    1),

                pixels,
                stride,
                0);


            Dictionary<uint, int> counts =
                new();


            for (int x = 0;
                 x < width;
                 x++)
            {
                int index =
                    x * 4;


                byte blue =
                    pixels[index];

                byte green =
                    pixels[index + 1];

                byte red =
                    pixels[index + 2];

                byte alpha =
                    pixels[index + 3];


                if (alpha == 0)
                    continue;


                uint key =
                    ((uint)alpha << 24) |
                    ((uint)red << 16) |
                    ((uint)green << 8) |
                    blue;


                if (!counts.TryAdd(
                    key,
                    1))
                {
                    counts[key]++;
                }
            }


            if (counts.Count == 0)
                return Colors.Transparent;


            uint dominant =
                counts
                    .OrderByDescending(
                        x => x.Value)
                    .First()
                    .Key;


            return Color.FromArgb(
                (byte)((dominant >> 24) & 0xFF),
                (byte)((dominant >> 16) & 0xFF),
                (byte)((dominant >> 8) & 0xFF),
                (byte)(dominant & 0xFF));
        }


        // =====================================================
        // BITMAP
        // =====================================================

        private BitmapImage LoadBitmap(
            string path,
            int? decodePixelWidth = null)
        {
            BitmapImage bitmap = new();

            bitmap.BeginInit();

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.CreateOptions =
                BitmapCreateOptions
                    .PreservePixelFormat;


            if (decodePixelWidth.HasValue)
            {
                bitmap.DecodePixelWidth =
                    decodePixelWidth.Value;
            }


            bitmap.UriSource =
                new Uri(
                    path,
                    UriKind.Absolute);


            bitmap.EndInit();

            bitmap.Freeze();

            return bitmap;
        }


        // =====================================================
        // ASSET MANAGEMENT
        // =====================================================

        private void AddAssetButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AssetEditorWindow window =
                new()
                {
                    Owner = this
                };


            if (window.ShowDialog() == true)
            {
                RefreshAssetBrowser();
            }
        }


        private void AssetEditButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                button.Tag is not AssetModel asset)
            {
                return;
            }


            AssetEditorWindow window =
                new(asset)
                {
                    Owner = this
                };


            if (window.ShowDialog() == true)
            {
                RefreshAssetBrowser();
            }


            e.Handled = true;
        }


        private void CategoriesButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CategoryManagerWindow window =
                new()
                {
                    Owner = this
                };


            window.ShowDialog();

            RefreshAssetBrowser();
        }


        private void AssetSearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (AssetBrowserPanel != null)
            {
                RefreshAssetBrowser();
            }
        }


        // =====================================================
        // MENU
        // =====================================================

        // A menü és az eszköztár a meglévő billentyűparancsok műveleteit hívja.
        private void UndoMenu_Click(object sender, RoutedEventArgs e) => Undo();
        private void RedoMenu_Click(object sender, RoutedEventArgs e) => Redo();
        private void CollisionsToggle_Changed(object sender, RoutedEventArgs e)
        {
            foreach (Border view in objectViews.Values)
                if (view.Child is Grid grid)
                    foreach (Canvas overlay in grid.Children.OfType<Canvas>().Where(c => Equals(c.Tag, "COLLISION_OVERLAY")))
                        overlay.Visibility = CollisionsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private static CollisionEdge Edge(MapObject obj, string side) => side switch
        {
            "Top" => obj.TopCollision,
            "Right" => obj.RightCollision,
            "Bottom" => obj.BottomCollision,
            _ => obj.LeftCollision
        };
        private IEnumerable<MapObject> SelectedObjects() => mapObjects.Where(o => selectedObjectIds.Contains(o.Id));

        private void DrawCollisionLines(Canvas canvas, MapObject obj)
        {
            canvas.Children.Clear();
            foreach (string side in new[] { "Top", "Right", "Bottom", "Left" })
            {
                CollisionEdge edge = Edge(obj, side);
                if (!edge.Enabled) continue;
                double w = Math.Max(0, obj.Width), h = Math.Max(0, obj.Height);
                var line = new Line { Stroke = CollisionBrush, StrokeThickness = 1.5, IsHitTestVisible = false };
                switch (side)
                {
                    case "Top": line.X1 = w * edge.Start; line.Y1 = 0; line.X2 = w * edge.End; line.Y2 = 0; break;
                    case "Bottom": line.X1 = w * edge.Start; line.Y1 = h; line.X2 = w * edge.End; line.Y2 = h; break;
                    case "Left": line.X1 = 0; line.Y1 = h * edge.Start; line.X2 = 0; line.Y2 = h * edge.End; break;
                    case "Right": line.X1 = w; line.Y1 = h * edge.Start; line.X2 = w; line.Y2 = h * edge.End; break;
                }
                canvas.Children.Add(line);
            }
        }

        private void RefreshCollisionOverlays()
        {
            foreach (MapObject obj in mapObjects)
                if (objectViews.TryGetValue(obj.Id, out Border? view) && view.Child is Grid grid)
                    foreach (Canvas overlay in grid.Children.OfType<Canvas>().Where(c => Equals(c.Tag, "COLLISION_OVERLAY")))
                        DrawCollisionLines(overlay, obj);
        }

        private CheckBox CollisionCheck(string side) => side switch
        {
            "Top" => CollisionTopCheck,
            "Right" => CollisionRightCheck,
            "Bottom" => CollisionBottomCheck,
            _ => CollisionLeftCheck
        };
        private Canvas CollisionSlider(string side) => side switch
        {
            "Top" => CollisionTopSlider,
            "Right" => CollisionRightSlider,
            "Bottom" => CollisionBottomSlider,
            _ => CollisionLeftSlider
        };
        private TextBlock CollisionLabel(string side) => side switch
        {
            "Top" => CollisionTopLabel,
            "Right" => CollisionRightLabel,
            "Bottom" => CollisionBottomLabel,
            _ => CollisionLeftLabel
        };

        private void RefreshCollisionInspector()
        {
            if (CollisionEditorPanel == null) return;
            updatingCollisionUi = true;
            try
            {
                var selected = SelectedObjects().ToList();
                CollisionEditorPanel.IsEnabled = selected.Count > 0;
                CollisionSlidersPanel.IsEnabled = selected.Count > 0;
                foreach (string side in new[] { "Top", "Right", "Bottom", "Left" })
                {
                    bool enabled = selected.Count > 0 && selected.All(o => Edge(o, side).Enabled);
                    CollisionCheck(side).IsChecked = enabled;
                    CollisionSlider(side).IsEnabled = enabled;
                    CollisionSlider(side).Opacity = enabled ? 1 : .3;
                    DrawRangeSlider(side);
                }
                DrawCollisionDiagram();
            }
            finally { updatingCollisionUi = false; }
        }

        private void CollisionCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (updatingCollisionUi || loadingMap || sender is not CheckBox check) return;
            string? side = new[] { "Top", "Right", "Bottom", "Left" }
                .FirstOrDefault(s => ReferenceEquals(CollisionCheck(s), check));
            if (side == null || !SelectedObjects().Any()) return;
            EditorSnapshot before = CaptureEditorSnapshot();
            foreach (MapObject obj in SelectedObjects()) Edge(obj, side).Enabled = check.IsChecked == true;
            CommitHistory(before);
            RefreshCollisionOverlays();
            RefreshCollisionInspector();
        }

        private void DrawCollisionDiagram()
        {
            if (CollisionDiagram == null) return;
            CollisionDiagram.Children.Clear();
            double w = CollisionDiagram.ActualWidth, h = CollisionDiagram.ActualHeight;
            if (w <= 0 || h <= 0) { w = 180; h = 108; }
            var objects = SelectedObjects().ToList();
            if (objects.Count == 0) return;
            foreach (string side in new[] { "Top", "Right", "Bottom", "Left" })
            {
                if (!objects.All(o => Edge(o, side).Enabled)) continue;
                CollisionEdge e = Edge(objects[0], side);
                var line = new Line { Stroke = CollisionBrush, StrokeThickness = 3 };
                if (side == "Top" || side == "Bottom")
                { line.X1 = e.Start * w; line.X2 = e.End * w; line.Y1 = line.Y2 = side == "Top" ? 0 : h; }
                else { line.Y1 = e.Start * h; line.Y2 = e.End * h; line.X1 = line.X2 = side == "Left" ? 0 : w; }
                CollisionDiagram.Children.Add(line);
            }
        }

        private void DrawRangeSlider(string side)
        {
            Canvas slider = CollisionSlider(side);
            slider.Children.Clear();
            double width = Math.Max(20, slider.ActualWidth), y = 13;
            var objects = SelectedObjects().ToList();
            CollisionEdge edge = objects.Count > 0 ? Edge(objects[0], side) : new CollisionEdge();
            double x1 = 8 + edge.Start * (width - 16), x2 = 8 + edge.End * (width - 16);
            slider.Children.Add(new Line { X1 = 8, Y1 = y, X2 = width - 8, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(65, 76, 90)), StrokeThickness = 4 });
            slider.Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = CollisionBrush, StrokeThickness = 4 });
            foreach (double x in new[] { x1, x2 })
            {
                var thumb = new Ellipse { Width = 14, Height = 14, Fill = Brushes.LightCyan, Stroke = Brushes.DarkSlateGray, StrokeThickness = 1, IsHitTestVisible = false };
                Canvas.SetLeft(thumb, x - 7); Canvas.SetTop(thumb, y - 7); slider.Children.Add(thumb);
            }
            CollisionLabel(side).Text = $"{(side == "Top" ? "Felső" : side == "Right" ? "Jobb" : side == "Bottom" ? "Alsó" : "Bal")}: {edge.Start * 100:0}–{edge.End * 100:0}%";
        }

        private void CollisionSlider_SizeChanged(object sender, SizeChangedEventArgs e)
        { if (sender is Canvas canvas && canvas.Tag is string side && CollisionTopLabel != null) DrawRangeSlider(side); }
        private void CollisionSlider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Canvas canvas || canvas.Tag is not string side || !canvas.IsEnabled) return;
            var selected = SelectedObjects().ToList();
            if (selected.Count == 0) return;
            CollisionEdge edge = Edge(selected[0], side);
            double fraction = Math.Clamp((e.GetPosition(canvas).X - 8) / Math.Max(1, canvas.ActualWidth - 16), 0, 1);
            draggingCollisionSide = side;
            draggingCollisionThumb = Math.Abs(fraction - edge.Start) <= Math.Abs(fraction - edge.End) ? 0 : 1;
            collisionDragSnapshot = CaptureEditorSnapshot();
            canvas.CaptureMouse();
            UpdateDraggedCollision(canvas, e);
            e.Handled = true;
        }
        private void CollisionSlider_MouseMove(object sender, MouseEventArgs e)
        { if (sender is Canvas canvas && canvas.IsMouseCaptured) UpdateDraggedCollision(canvas, e); }
        private void UpdateDraggedCollision(Canvas canvas, MouseEventArgs e)
        {
            if (draggingCollisionSide == null) return;
            double fraction = Math.Clamp((e.GetPosition(canvas).X - 8) / Math.Max(1, canvas.ActualWidth - 16), 0, 1);
            var targets = SelectedObjects().ToList();
            if (targets.Count == 0) return;
            CollisionEdge reference = Edge(targets[0], draggingCollisionSide);
            double start = draggingCollisionThumb == 0 ? Math.Min(fraction, reference.End) : reference.Start;
            double end = draggingCollisionThumb == 1 ? Math.Max(fraction, reference.Start) : reference.End;
            foreach (MapObject obj in targets)
            {
                CollisionEdge edge = Edge(obj, draggingCollisionSide);
                // Reset both endpoints in a safe order, then apply the shared range.
                edge.Start = 0;
                edge.End = 1;
                edge.Start = start;
                edge.End = end;
            }
            DrawRangeSlider(draggingCollisionSide);
            DrawCollisionDiagram();
            RefreshCollisionOverlays();
        }
        private void CollisionSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Canvas canvas && canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
            if (collisionDragSnapshot != null) CommitHistory(collisionDragSnapshot);
            collisionDragSnapshot = null; draggingCollisionSide = null;
        }

        private void CopyMenu_Click(object sender, RoutedEventArgs e) => CopySelectedObject();
        private void CutMenu_Click(object sender, RoutedEventArgs e) => CutSelectedObject();
        private void PasteMenu_Click(object sender, RoutedEventArgs e) => PasteObject();
        private void DeleteMenu_Click(object sender, RoutedEventArgs e) => DeleteSelectedObject();
        private void ExitEditor_Click(object sender, RoutedEventArgs e) => Close();

        private void BackToMenu_Click(
            object sender,
            RoutedEventArgs e)
        {
            MainWindow mainWindow =
                new();

            mainWindow.Show();

            Close();
        }
    }
}