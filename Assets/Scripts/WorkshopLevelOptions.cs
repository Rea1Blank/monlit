using UnityEngine;

namespace Monlit
{
    public enum WorkshopDifficulty { Junior, Middle, Senior }

    public readonly struct WorkshopLevelOptions
    {
        public const int MinBlocks = 8;
        public const int MaxBlocks = 128;
        public const int MinServices = 2;
        public const int MaxServices = 16;

        private readonly int blocks;
        private readonly int services;
        private readonly WorkshopDifficulty difficulty;

        public int Blocks => blocks == 0 ? 18 : blocks;
        public int Services => services == 0 ? 3 : services;
        public WorkshopDifficulty Difficulty => blocks == 0 ? WorkshopDifficulty.Middle : difficulty;
        public static WorkshopLevelOptions Default => default;

        public static int MinBlocksFor(int services) =>
            Mathf.Max(MinBlocks, 2 * Mathf.Clamp(services, MinServices, MaxServices));

        public WorkshopLevelOptions(int blocks, int services, WorkshopDifficulty difficulty = WorkshopDifficulty.Middle)
        {
            this.services = Mathf.Clamp(services, MinServices, MaxServices);
            this.blocks = Mathf.Clamp(blocks, MinBlocksFor(this.services), MaxBlocks);
            this.difficulty = (WorkshopDifficulty)Mathf.Clamp((int)difficulty,
                (int)WorkshopDifficulty.Junior, (int)WorkshopDifficulty.Senior);
        }
    }
}
