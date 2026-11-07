namespace ThePixelRealmsEditor.Models
{
    public class AssetModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Az EditorStorage Assets mappáján belüli fájlnév.
        /// Például:
        /// 9e6d...png
        /// </summary>
        public string ImageFileName { get; set; } = string.Empty;

        public int SourceWidth { get; set; }

        public int SourceHeight { get; set; }

        /// <summary>
        /// A játékban használt alapméret.
        /// Piskel export / 16.
        /// </summary>
        public double GameWidth => SourceWidth / 16.0;

        public double GameHeight => SourceHeight / 16.0;
    }
}