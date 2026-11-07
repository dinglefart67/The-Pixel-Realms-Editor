using System.Windows;
using System.Windows.Controls;
using ThePixelRealmsEditor.Models;
using ThePixelRealmsEditor.Services;

namespace ThePixelRealmsEditor
{
    public partial class CategoryManagerWindow : Window
    {
        public CategoryManagerWindow()
        {
            InitializeComponent();

            RefreshCategories();
        }


        private void RefreshCategories()
        {
            string? previousSelection =
                CategoryListBox.SelectedItem as string;

            CategoryListBox.Items.Clear();

            foreach (AssetCategory category
                     in CategoryDatabase.Categories)
            {
                CategoryListBox.Items.Add(category.Name);
            }

            if (previousSelection != null &&
                CategoryListBox.Items.Contains(previousSelection))
            {
                CategoryListBox.SelectedItem =
                    previousSelection;
            }
            else if (CategoryListBox.Items.Count > 0)
            {
                CategoryListBox.SelectedIndex = 0;
            }

            RefreshTypes();
        }


        private AssetCategory? GetSelectedCategory()
        {
            string? name =
                CategoryListBox.SelectedItem as string;

            if (name == null)
                return null;

            return CategoryDatabase.Categories
                .FirstOrDefault(x => x.Name == name);
        }


        private void RefreshTypes()
        {
            TypeListBox.Items.Clear();

            AssetCategory? category =
                GetSelectedCategory();

            if (category == null)
                return;

            foreach (string type in category.Types)
            {
                TypeListBox.Items.Add(type);
            }
        }


        private void CategoryListBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshTypes();
        }


        private void AddCategoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string name =
                NewCategoryTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
                return;

            CategoryDatabase.AddCategory(name);

            NewCategoryTextBox.Clear();

            RefreshCategories();

            CategoryListBox.SelectedItem = name;
        }


        private void DeleteCategoryButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AssetCategory? category =
                GetSelectedCategory();

            if (category == null)
                return;


            bool categoryIsUsed =
                AssetDatabase.Assets.Any(
                    x => x.Category == category.Name);

            if (categoryIsUsed)
            {
                MessageBox.Show(
                    "Ez a kategória nem törölhető, mert egy vagy több asset használja.",
                    "Kategória használatban",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            MessageBoxResult result =
                MessageBox.Show(
                    $"Biztosan törlöd ezt a kategóriát?\n\n{category.Name}",
                    "Kategória törlése",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            CategoryDatabase.DeleteCategory(category);

            RefreshCategories();
        }


        private void AddTypeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AssetCategory? category =
                GetSelectedCategory();

            if (category == null)
                return;

            string type =
                NewTypeTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(type))
                return;

            CategoryDatabase.AddType(
                category,
                type);

            NewTypeTextBox.Clear();

            RefreshTypes();

            TypeListBox.SelectedItem = type;
        }


        private void DeleteTypeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            AssetCategory? category =
                GetSelectedCategory();

            string? type =
                TypeListBox.SelectedItem as string;

            if (category == null || type == null)
                return;


            bool typeIsUsed =
                AssetDatabase.Assets.Any(
                    x =>
                        x.Category == category.Name &&
                        x.Type == type);

            if (typeIsUsed)
            {
                MessageBox.Show(
                    "Ez a type nem törölhető, mert egy vagy több asset használja.",
                    "Type használatban",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }


            CategoryDatabase.DeleteType(
                category,
                type);

            RefreshTypes();
        }


        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = true;

            Close();
        }
    }
}