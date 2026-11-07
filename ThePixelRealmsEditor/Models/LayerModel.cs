namespace ThePixelRealmsEditor.Models
{
    public class LayerModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = "Layer 1";

        /// <summary>
        /// Parallax / pályahossz szorzó.
        /// 1.0 = teljes pályaszélesség, 0.2 = a pályaszélesség 20%-a.
        /// </summary>
        public double Value { get; set; } = 1.0;
    }
}
