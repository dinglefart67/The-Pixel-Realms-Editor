
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ThePixelRealmsEditor.Models
{
    public enum EntityKind
    {
        Enemy,
        Npc
    }

    public sealed class EntityDefinition
    {
        public int FormatVersion { get; set; } = 1;
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public EntityKind Kind { get; set; } = EntityKind.Enemy;

        public EntityStats Stats { get; set; } = new();
        public List<EntityAction> Actions { get; set; } = new();

        public EntityDefinition DeepClone()
        {
            string json = JsonSerializer.Serialize(this);
            return JsonSerializer.Deserialize<EntityDefinition>(json)
                ?? throw new InvalidOperationException(
                    "Nem sikerült másolni az entitást.");
        }
    }

    public sealed class EntityStats
    {
        // Common properties
        public double Width { get; set; } = 48;
        public double Height { get; set; } = 60;
        public double WalkSpeed { get; set; } = 70;
        public double PatrolWaitTime { get; set; } = 4;

        // Enemy properties
        public double MaxHp { get; set; } = 100;
        public double AggroSpeed { get; set; } = 140;
        public double AggroRange { get; set; } = 200;
        public double ContactDamage { get; set; } = 20;
        public double DamageCooldown { get; set; } = 2;
        public double AttackRange { get; set; } = 50;
        public double StopDistance { get; set; } = 50;
        public double AggroShareRadius { get; set; } = 200;
        public double HpBarWidth { get; set; } = 50;
        public double MeleeCooldown { get; set; } = 1;
        public double FlySpeed { get; set; } = 50;

        public bool AggroOnSight { get; set; } = true;
        public bool IgnoresGravity { get; set; }
        public bool CountsForKill { get; set; } = true;

        // NPC properties
        public string DisplayName { get; set; } = string.Empty;
        public string DialogueFile { get; set; } = string.Empty;
        public string InteractionHintText { get; set; }
            = "Press (E) to talk";
    }

    public sealed class EntityAction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Type { get; set; } = "Idle";

        public double FrameDuration { get; set; } = 0.08;

        public List<EntityFrame> Frames { get; set; } = new();

        public EntityAction DeepClone()
        {
            return new EntityAction
            {
                Id = Id,
                Type = Type,
                FrameDuration = FrameDuration,
                Frames = Frames.ConvertAll(f => new EntityFrame
                {
                    ImageFileName = f.ImageFileName,
                    OriginalFileName = f.OriginalFileName,
                    PixelWidth = f.PixelWidth,
                    PixelHeight = f.PixelHeight
                })
            };
        }
    }

    public sealed class EntityFrame
    {
        public string ImageFileName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }
}
