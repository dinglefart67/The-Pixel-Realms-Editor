using System.IO;

namespace ThePixelRealmsEditor.Services
{
    public static class EditorStorage
    {
        public static string RootDirectory { get; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "The Pixel Realms Editor");

        public static string AssetsDirectory =>
            Path.Combine(RootDirectory, "Assets");

        public static string AssetsJsonPath =>
            Path.Combine(RootDirectory, "assets.json");

        public static string CategoriesJsonPath =>
            Path.Combine(RootDirectory, "categories.json");

        public static string LayersJsonPath =>
            Path.Combine(RootDirectory, "layers.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(AssetsDirectory);
        }

        public static string GetAssetImagePath(Models.AssetModel asset)
        {
            return Path.Combine(AssetsDirectory, asset.ImageFileName);
        }
    }
}
