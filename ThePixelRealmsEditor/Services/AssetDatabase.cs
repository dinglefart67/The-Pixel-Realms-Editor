using System.IO;
using System.Text.Json;
using ThePixelRealmsEditor.Models;

namespace ThePixelRealmsEditor.Services
{
    public static class AssetDatabase
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static List<AssetModel> Assets { get; private set; } = new();

        public static void Load()
        {
            EditorStorage.EnsureDirectories();

            if (!File.Exists(EditorStorage.AssetsJsonPath))
            {
                Assets = new List<AssetModel>();
                Save();
                return;
            }

            try
            {
                string json = File.ReadAllText(EditorStorage.AssetsJsonPath);

                Assets =
                    JsonSerializer.Deserialize<List<AssetModel>>(json)
                    ?? new List<AssetModel>();
            }
            catch
            {
                Assets = new List<AssetModel>();
            }
        }

        public static void Save()
        {
            EditorStorage.EnsureDirectories();

            string json =
                JsonSerializer.Serialize(Assets, JsonOptions);

            File.WriteAllText(
                EditorStorage.AssetsJsonPath,
                json);
        }

        public static void Add(AssetModel asset)
        {
            Assets.Add(asset);
            Save();
        }

        public static void Update(AssetModel asset)
        {
            AssetModel? existing =
                Assets.FirstOrDefault(x => x.Id == asset.Id);

            if (existing == null)
                return;

            int index = Assets.IndexOf(existing);

            Assets[index] = asset;

            Save();
        }

        public static void Delete(AssetModel asset)
        {
            Assets.RemoveAll(x => x.Id == asset.Id);

            string imagePath =
                EditorStorage.GetAssetImagePath(asset);

            try
            {
                if (File.Exists(imagePath))
                    File.Delete(imagePath);
            }
            catch
            {
                // Ha valami éppen fogja a fájlt,
                // az adatbázisból akkor is eltűnik.
            }

            Save();
        }
    }
}