
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using ThePixelRealmsEditor.Models;

namespace ThePixelRealmsEditor.Services
{
    public static class EntityStorage
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string GetFramePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                Path.GetFileName(fileName) != fileName)
            {
                throw new InvalidDataException(
                    "Érvénytelen képkocka-fájlnév.");
            }

            return Path.Combine(
                EditorStorage.AssetsDirectory,
                fileName);
        }

        public static BitmapImage? LoadFrame(string fileName)
        {
            try
            {
                string path = GetFramePath(fileName);

                if (!File.Exists(path))
                    return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource =
                    new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static EntityFrame ImportFrame(string sourcePath)
        {
            if (!File.Exists(sourcePath) ||
                !string.Equals(
                    Path.GetExtension(sourcePath),
                    ".png",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Csak létező PNG-fájl importálható.");
            }

            EditorStorage.EnsureDirectories();

            string storedName =
                $"entity_{Guid.NewGuid():N}.png";

            string destination =
                GetFramePath(storedName);

            try
            {
                File.Copy(sourcePath, destination);

                BitmapImage image = LoadFrame(storedName)
                    ?? throw new InvalidDataException(
                        "A PNG nem olvasható.");

                return new EntityFrame
                {
                    ImageFileName = storedName,
                    OriginalFileName =
                        Path.GetFileName(sourcePath),
                    PixelWidth = image.PixelWidth,
                    PixelHeight = image.PixelHeight
                };
            }
            catch
            {
                if (File.Exists(destination))
                    File.Delete(destination);

                throw;
            }
        }

        public static void DeleteImportedFrame(string fileName)
        {
            // Csak az általunk létrehozott képkockák.
            if (!fileName.StartsWith(
                "entity_", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                string path = GetFramePath(fileName);

                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // A zárolt állomány később takarítható.
            }
        }

        public static void Save(
            EntityDefinition entity,
            string path)
        {
            if (entity.Actions == null ||
                !entity.Actions.Any(a =>
                    a.Type.Equals(
                        "Idle",
                        StringComparison.OrdinalIgnoreCase) &&
                    a.Frames.Count > 0))
            {
                throw new InvalidDataException(
                    "Az Idle animáció kötelező, " +
                    "és legalább egy képkockát kell tartalmaznia.");
            }

            string? directory = Path.GetDirectoryName(
                Path.GetFullPath(path));

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(
                entity, Options);

            string tempPath = path + ".tmp";

            try
            {
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public static EntityDefinition Load(string path)
        {
            string json = File.ReadAllText(path);

            EntityDefinition entity =
                JsonSerializer.Deserialize<EntityDefinition>(
                    json, Options)
                ?? throw new InvalidDataException(
                    "Üres entitásfájl.");

            if (entity.FormatVersion != 1 ||
                string.IsNullOrWhiteSpace(entity.Name) ||
                entity.Stats == null ||
                entity.Actions == null ||
                entity.Actions.Count == 0)
            {
                throw new InvalidDataException(
                    "Érvénytelen vagy nem támogatott entitásfájl.");
            }

            if (entity.Actions.Any(a =>
                    a == null ||
                    string.IsNullOrWhiteSpace(a.Type) ||
                    a.Frames == null ||
                    a.FrameDuration <= 0 ||
                    a.Frames.Any(f =>
                        f == null ||
                        string.IsNullOrWhiteSpace(
                            f.ImageFileName) ||
                        Path.GetFileName(f.ImageFileName)
                            != f.ImageFileName)))
            {
                throw new InvalidDataException(
                    "Hibás animációadatok.");
            }

            if (!entity.Actions.Any(a =>
                    a.Type.Equals(
                        "Idle",
                        StringComparison.OrdinalIgnoreCase) &&
                    a.Frames.Count > 0))
            {
                throw new InvalidDataException(
                    "Hiányzik az Idle animáció.");
            }

            if (entity.Actions.GroupBy(
                    a => a.Type,
                    StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
            {
                throw new InvalidDataException(
                    "Duplikált Action-típusok.");
            }

            return entity;
        }
    }

    public static class ActionTypeDatabase
    {
        private static readonly string[] Defaults =
        {
            "Idle",
            "Walking",
            "Melee"
        };

        public static string JsonPath =>
            Path.Combine(
                EditorStorage.RootDirectory,
                "actions.json");

        public static List<string> Types { get; private set; }
            = new();

        public static void Load()
        {
            EditorStorage.EnsureDirectories();

            List<string> loaded = new();

            if (File.Exists(JsonPath))
            {
                try
                {
                    loaded =
                        JsonSerializer.Deserialize<List<string>>(
                            File.ReadAllText(JsonPath))
                        ?? new();
                }
                catch
                {
                    loaded = new();
                }
            }

            Types = Defaults
                .Concat(loaded)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!File.Exists(JsonPath))
                Save();
        }

        public static bool Add(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return false;

            type = type.Trim();

            if (Types.Contains(
                type, StringComparer.OrdinalIgnoreCase))
                return false;

            Types.Add(type);
            Save();
            return true;
        }

        private static void Save()
        {
            EditorStorage.EnsureDirectories();

            string json = JsonSerializer.Serialize(
                Types,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(JsonPath, json);
        }
    }
}
