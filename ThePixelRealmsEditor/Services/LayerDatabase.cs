using System.IO;
using System.Text.Json;
using ThePixelRealmsEditor.Models;

namespace ThePixelRealmsEditor.Services
{
    public static class LayerDatabase
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static List<LayerModel> Layers { get; private set; } = new();

        // Layers belong to the currently open map, not to a global editor database.
        public static void Load() => ResetForNewMap();

        public static void ResetForNewMap()
        {
            Layers = new List<LayerModel>
            {
                new LayerModel { Name = "Layer 1", Value = 1.0 }
            };
        }

        public static void ReplaceLayers(IEnumerable<LayerModel> layers)
        {
            Layers = layers.Select(x => new LayerModel
            {
                Id = x.Id,
                Name = x.Name,
                Value = x.Value
            }).ToList();
            Sort();
        }

        // Kept for existing editor call sites. Persistence is handled by the map project.
        public static void Save() => Sort();

        public static void Add(LayerModel layer)
        {
            Layers.Add(layer);
            Save();
        }

        public static void Delete(LayerModel layer)
        {
            Layers.RemoveAll(x => x.Id == layer.Id);
            Save();
        }

        public static void Update(LayerModel layer)
        {
            LayerModel? existing =
                Layers.FirstOrDefault(x => x.Id == layer.Id);

            if (existing == null)
                return;

            int index = Layers.IndexOf(existing);
            Layers[index] = layer;
            Save();
        }

        public static bool NameExists(
            string name,
            Guid? exceptId = null)
        {
            return Layers.Any(
                x =>
                    (!exceptId.HasValue || x.Id != exceptId.Value) &&
                    x.Name.Equals(
                        name.Trim(),
                        StringComparison.OrdinalIgnoreCase));
        }

        private static void CreateDefaults()
        {
            Layers = new List<LayerModel>
            {
                new LayerModel
                {
                    Name = "Layer 1",
                    Value = 1.0
                }
            };
        }

        private static void Sort()
        {
            Layers =
                Layers
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Name)
                    .ToList();
        }
    }
}
