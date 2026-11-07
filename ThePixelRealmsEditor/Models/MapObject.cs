using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace ThePixelRealmsEditor.Models
{
    public enum AssetResizeMode { Stretch, Repeat }
    public class CollisionEdge : INotifyPropertyChanged
    {
        private bool enabled;
        private double start;
        private double end = 1;
        public bool Enabled { get => enabled; set => Set(ref enabled, value); }
        public double Start { get => start; set => Set(ref start, Math.Clamp(value, 0, End)); }
        public double End { get => end; set => Set(ref end, Math.Clamp(value, Start, 1)); }
        public CollisionEdge Clone() => new() { Enabled = Enabled, Start = Start, End = End };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }
    public class MapObject : INotifyPropertyChanged
    {
        private Guid id = Guid.NewGuid(), assetId;
        private double x, y, width, height;
        private AssetResizeMode widthMode = AssetResizeMode.Stretch, heightMode = AssetResizeMode.Stretch;
        private string layer = "Layer 1";
        private int zIndex;
        public Guid Id { get => id; set => Set(ref id, value); }
        public Guid AssetId { get => assetId; set => Set(ref assetId, value); }
        public double X { get => x; set => Set(ref x, value); }
        public double Y { get => y; set => Set(ref y, value); }
        public double Width { get => width; set => Set(ref width, value); }
        public double Height { get => height; set => Set(ref height, value); }
        public AssetResizeMode WidthMode { get => widthMode; set => Set(ref widthMode, value); }
        public AssetResizeMode HeightMode { get => heightMode; set => Set(ref heightMode, value); }
        public string Layer { get => layer; set => Set(ref layer, value); }
        public int ZIndex { get => zIndex; set => Set(ref zIndex, value); }
        public CollisionEdge TopCollision { get; set; } = new();
        public CollisionEdge RightCollision { get; set; } = new();
        public CollisionEdge BottomCollision { get; set; } = new();
        public CollisionEdge LeftCollision { get; set; } = new();
        public MapObject Clone(bool newId = false) => new()
        {
            Id = newId ? Guid.NewGuid() : Id,
            AssetId = AssetId,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            WidthMode = WidthMode,
            HeightMode = HeightMode,
            Layer = Layer,
            ZIndex = ZIndex,
            TopCollision = TopCollision.Clone(),
            RightCollision = RightCollision.Clone(),
            BottomCollision = BottomCollision.Clone(),
            LeftCollision = LeftCollision.Clone()
        };
        public bool StateEquals(MapObject other) => Id == other.Id && AssetId == other.AssetId && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height && WidthMode == other.WidthMode && HeightMode == other.HeightMode && Layer == other.Layer && ZIndex == other.ZIndex &&
            EdgeEquals(TopCollision, other.TopCollision) && EdgeEquals(RightCollision, other.RightCollision) && EdgeEquals(BottomCollision, other.BottomCollision) && EdgeEquals(LeftCollision, other.LeftCollision);
        private static bool EdgeEquals(CollisionEdge a, CollisionEdge b) => a.Enabled == b.Enabled && a.Start == b.Start && a.End == b.End;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
    }
}
