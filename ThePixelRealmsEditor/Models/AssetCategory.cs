namespace ThePixelRealmsEditor.Models
{
    public class AssetCategory
    {
        public string Name { get; set; } = string.Empty;

        public List<string> Types { get; set; } = new();
    }
}