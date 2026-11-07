using System.IO;
using System.Text.Json;
using ThePixelRealmsEditor.Models;

namespace ThePixelRealmsEditor.Services
{
    public static class CategoryDatabase
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static List<AssetCategory> Categories { get; private set; } = new();

        public static void Load()
        {
            EditorStorage.EnsureDirectories();

            if (!File.Exists(EditorStorage.CategoriesJsonPath))
            {
                CreateDefaults();
                Save();
                return;
            }

            try
            {
                string json =
                    File.ReadAllText(EditorStorage.CategoriesJsonPath);

                Categories =
                    JsonSerializer.Deserialize<List<AssetCategory>>(json)
                    ?? new List<AssetCategory>();
            }
            catch
            {
                Categories = new List<AssetCategory>();
            }
        }

        public static void Save()
        {
            EditorStorage.EnsureDirectories();

            string json =
                JsonSerializer.Serialize(Categories, JsonOptions);

            File.WriteAllText(
                EditorStorage.CategoriesJsonPath,
                json);
        }

        private static void CreateDefaults()
        {
            Categories = new List<AssetCategory>
            {
                new AssetCategory
                {
                    Name = "Környezet",
                    Types = new List<string>
                    {
                        "Ground",
                        "Épületek",
                        "Propok",
                        "Dekoráció"
                    }
                },

                new AssetCategory
                {
                    Name = "Gameplay",
                    Types = new List<string>
                    {
                        "Spawn Point",
                        "Trigger",
                        "Collision"
                    }
                }
            };
        }

        public static void AddCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (Categories.Any(
                    x => x.Name.Equals(
                        name,
                        StringComparison.OrdinalIgnoreCase)))
                return;

            Categories.Add(new AssetCategory
            {
                Name = name.Trim()
            });

            Save();
        }

        public static void DeleteCategory(AssetCategory category)
        {
            Categories.Remove(category);
            Save();
        }

        public static void AddType(
            AssetCategory category,
            string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return;

            if (category.Types.Any(
                    x => x.Equals(
                        type,
                        StringComparison.OrdinalIgnoreCase)))
                return;

            category.Types.Add(type.Trim());

            Save();
        }

        public static void DeleteType(
            AssetCategory category,
            string type)
        {
            category.Types.Remove(type);
            Save();
        }
    }
}