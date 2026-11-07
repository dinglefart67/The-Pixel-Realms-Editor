using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ThePixelRealmsEditor.Models;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class AssetEditorWindow : Window
    {
        private readonly AssetModel? editingAsset;

        private string? selectedImagePath;

        private int sourceWidth;
        private int sourceHeight;


        public AssetEditorWindow()
        {
            InitializeComponent();

            LoadCategories();

            if (CategoryComboBox.Items.Count > 0)
                CategoryComboBox.SelectedIndex = 0;
        }


        public AssetEditorWindow(AssetModel asset)
        {
            InitializeComponent();

            editingAsset = asset;

            WindowTitleText.Text = "ASSET SZERKESZTÉSE";
            DeleteButton.Visibility = Visibility.Visible;

            LoadCategories();

            LoadExistingAsset();
        }


        private void LoadCategories()
        {
            CategoryComboBox.Items.Clear();

            foreach (AssetCategory category in CategoryDatabase.Categories)
            {
                CategoryComboBox.Items.Add(category.Name);
            }
        }


        private void LoadExistingAsset()
        {
            if (editingAsset == null)
                return;

            NameTextBox.Text = editingAsset.Name;

            CategoryComboBox.SelectedItem =
                editingAsset.Category;

            RefreshTypes();

            TypeComboBox.SelectedItem =
                editingAsset.Type;

            sourceWidth = editingAsset.SourceWidth;
            sourceHeight = editingAsset.SourceHeight;

            string path =
                EditorStorage.GetAssetImagePath(editingAsset);

            if (File.Exists(path))
            {
                LoadPreview(path);
            }

            UpdateSizeLabels();
        }


        private void CategoryComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshTypes();
        }


        private void RefreshTypes()
        {
            if (TypeComboBox == null)
                return;

            TypeComboBox.Items.Clear();

            string? selectedCategory =
                CategoryComboBox.SelectedItem as string;

            if (selectedCategory == null)
                return;

            AssetCategory? category =
                CategoryDatabase.Categories.FirstOrDefault(
                    x => x.Name == selectedCategory);

            if (category == null)
                return;

            foreach (string type in category.Types)
            {
                TypeComboBox.Items.Add(type);
            }

            if (TypeComboBox.Items.Count > 0)
                TypeComboBox.SelectedIndex = 0;
        }


        private void SelectImageButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Title = "PNG asset kiválasztása",
                Filter = "PNG képek (*.png)|*.png",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            selectedImagePath = dialog.FileName;

            try
            {
                BitmapDecoder decoder =
                    BitmapDecoder.Create(
                        new Uri(selectedImagePath),
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);

                BitmapFrame frame = decoder.Frames[0];

                sourceWidth = frame.PixelWidth;
                sourceHeight = frame.PixelHeight;

                LoadPreview(selectedImagePath);

                UpdateSizeLabels();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"A kép nem tölthető be.\n\n{ex.Message}",
                    "Hiba",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                selectedImagePath = null;
            }
        }


        private void LoadPreview(string path)
        {
            try
            {
                BitmapImage bitmap = new();

                bitmap.BeginInit();

                bitmap.CacheOption =
                    BitmapCacheOption.OnLoad;

                bitmap.UriSource =
                    new Uri(path, UriKind.Absolute);

                bitmap.EndInit();
                bitmap.Freeze();

                PreviewImage.Source = bitmap;

                NoImageText.Visibility =
                    Visibility.Collapsed;
            }
            catch
            {
                PreviewImage.Source = null;

                NoImageText.Visibility =
                    Visibility.Visible;
            }
        }


        private void UpdateSizeLabels()
        {
            SourceSizeText.Text =
                $"Forrás: {sourceWidth} × {sourceHeight} px";

            GameSizeText.Text =
                $"Játékban: {sourceWidth / 16.0:0.##} × {sourceHeight / 16.0:0.##}";
        }


        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string name =
                NameTextBox.Text.Trim();

            string? category =
                CategoryComboBox.SelectedItem as string;

            string? type =
                TypeComboBox.SelectedItem as string;


            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    "Adj nevet az assetnek.",
                    "Hiányzó adat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (string.IsNullOrWhiteSpace(category))
            {
                MessageBox.Show(
                    "Válassz kategóriát.",
                    "Hiányzó adat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            if (string.IsNullOrWhiteSpace(type))
            {
                MessageBox.Show(
                    "Válassz type-ot.",
                    "Hiányzó adat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            // ÚJ ASSET

            if (editingAsset == null)
            {
                if (selectedImagePath == null)
                {
                    MessageBox.Show(
                        "Válassz PNG képet.",
                        "Hiányzó kép",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                Guid id = Guid.NewGuid();

                string extension =
                    Path.GetExtension(selectedImagePath);

                string fileName =
                    $"{id}{extension}";

                string destination =
                    Path.Combine(
                        EditorStorage.AssetsDirectory,
                        fileName);

                File.Copy(
                    selectedImagePath,
                    destination,
                    true);

                AssetModel asset = new()
                {
                    Id = id,
                    Name = name,
                    Category = category,
                    Type = type,
                    ImageFileName = fileName,
                    SourceWidth = sourceWidth,
                    SourceHeight = sourceHeight
                };

                AssetDatabase.Add(asset);
            }

            // MEGLÉVŐ ASSET

            else
            {
                editingAsset.Name = name;
                editingAsset.Category = category;
                editingAsset.Type = type;

                if (selectedImagePath != null)
                {
                    string oldPath =
                        EditorStorage.GetAssetImagePath(
                            editingAsset);

                    string extension =
                        Path.GetExtension(selectedImagePath);

                    string newFileName =
                        $"{editingAsset.Id}{extension}";

                    string newPath =
                        Path.Combine(
                            EditorStorage.AssetsDirectory,
                            newFileName);

                    // Ha az új és régi útvonal nem ugyanaz.
                    if (!string.Equals(
                        selectedImagePath,
                        newPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(
                            selectedImagePath,
                            newPath,
                            true);
                    }

                    // Ha változott a kiterjesztés/fájlnév,
                    // a régit töröljük.
                    if (!string.Equals(
                        oldPath,
                        newPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (File.Exists(oldPath))
                                File.Delete(oldPath);
                        }
                        catch
                        {
                        }
                    }

                    editingAsset.ImageFileName =
                        newFileName;

                    editingAsset.SourceWidth =
                        sourceWidth;

                    editingAsset.SourceHeight =
                        sourceHeight;
                }

                AssetDatabase.Update(editingAsset);
            }


            DialogResult = true;
            Close();
        }


        private void DeleteButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (editingAsset == null)
                return;

            MessageBoxResult result =
                MessageBox.Show(
                    $"Biztosan törlöd ezt az assetet?\n\n{editingAsset.Name}",
                    "Asset törlése",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            AssetDatabase.Delete(editingAsset);

            DialogResult = true;

            Close();
        }


        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;

            Close();
        }
    }
}