
using System.Windows;

namespace ThePixelRealmsEditor
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void MapEditorButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MapEditorWindow mapEditor = new MapEditorWindow();

            mapEditor.Show();

            Close();
        }

        private void EntityEditorButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            EntityEditorWindow entityEditor =
                new EntityEditorWindow();

            entityEditor.Show();

            Close();
        }
    }
}
