using System.Globalization;
using System.Windows;
using ThePixelRealmsEditor.Models;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class LayerEditorWindow : Window
    {
        private readonly LayerModel? editingLayer;

        public LayerModel? SavedLayer { get; private set; }

        public bool LayerDeleted { get; private set; }

        public LayerEditorWindow()
        {
            InitializeComponent();

            LayerNameTextBox.Text =
                GetNextDefaultName();

            LayerValueTextBox.Text = "1.0";
        }

        public LayerEditorWindow(
            LayerModel layer)
        {
            InitializeComponent();

            editingLayer = layer;

            LayerNameTextBox.Text =
                layer.Name;

            LayerValueTextBox.Text =
                layer.Value.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);

            DeleteLayerButton.Visibility =
                Visibility.Visible;
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string name =
                LayerNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    "Adj nevet a layernek.",
                    "Hibás név",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            string valueText =
                LayerValueTextBox.Text
                    .Trim()
                    .Replace(',', '.');

            if (!double.TryParse(
                    valueText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value) ||
                value <= 0)
            {
                MessageBox.Show(
                    "A layer értéke 0-nál nagyobb szám legyen. Például: 1.0 vagy 0.2.",
                    "Hibás érték",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (LayerDatabase.NameExists(
                    name,
                    editingLayer?.Id))
            {
                MessageBox.Show(
                    "Már létezik ilyen nevű layer.",
                    "Duplikált layer",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (editingLayer == null)
            {
                SavedLayer =
                    new LayerModel
                    {
                        Name = name,
                        Value = value
                    };

                LayerDatabase.Add(
                    SavedLayer);
            }
            else
            {
                SavedLayer =
                    new LayerModel
                    {
                        Id = editingLayer.Id,
                        Name = name,
                        Value = value
                    };

                LayerDatabase.Update(
                    SavedLayer);
            }

            DialogResult = true;
        }

        private void DeleteLayerButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (editingLayer == null)
                return;

            if (LayerDatabase.Layers.Count <= 1)
            {
                MessageBox.Show(
                    "Az utolsó layert nem lehet törölni.",
                    "Layer törlése",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            MessageBoxResult result =
                MessageBox.Show(
                    $"Biztosan törlöd ezt a layert?\n\n{editingLayer.Name}",
                    "Layer törlése",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            LayerDatabase.Delete(
                editingLayer);

            SavedLayer =
                editingLayer;

            LayerDeleted = true;
            DialogResult = true;
        }


        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private string GetNextDefaultName()
        {
            int index = 1;

            while (LayerDatabase.NameExists(
                       $"Layer {index}"))
            {
                index++;
            }

            return $"Layer {index}";
        }
    }
}
