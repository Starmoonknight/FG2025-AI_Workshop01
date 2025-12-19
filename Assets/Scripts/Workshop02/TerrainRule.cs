using UnityEngine;


namespace AI_Workshop02
{

    public enum PlacementMode { Static, Blob, Lichtenberg }


    [CreateAssetMenu(menuName = "Board/Terrain Rule")]
    public sealed class TerrainRule : ScriptableObject
    { 

        [Header("Identity")]
        public string DisplayName = "New Rule";

        [Tooltip("Color painted into the board base colors.")]
        public Color32 Color = new Color32(255, 255, 255, 255);

        [Tooltip("Terrain movement cost (ignored for obstacles).")]
        [Min(0)] public int Cost = 10;



        [Header("Classification")]
        [Tooltip("If true: this rule places obstacles (sets walkable=false). If false: paints walkable terrain.")]
        public bool IsObstacle = false;

        // if IsObstacle is a real blocker, should I have a walkable modifier that affects if different unit types can pass or not?
        // Like a water terrain can be walkable if the unit is a boat but not if it is a land unit and so on.
        // Say land tiles VS water tiles, some may not be obstacles but still un-walkable
        // should I introdue a IsWalkable to have an _walkable[idx]? And should it be an enum like: enum Walkable { Ground, Liquid, Gas } etc. /Or enum Walkable { Traversable, Un-Traversable, Water, Underground }
        // Then I could do something like:
        // if (_blocked[n]) continue;                               // hard wall check
        // if (!agent.CanEnterTerrain(_terrainId[n])) continue;     // unit/terrain rule

        // or do I have that allready in the info I save, double check every purpose for all arrays to make sure they are used to full potential!
        // also need to make a rule bool or int in that case to place under the "Rules" segment.


        [Header("Seeding")]
        [Tooltip("When picking initial seed/start/goal cells, require the chosen cells to be unblocked.")]
        public bool ForceUnblockedSeed = false;



        [Header("Placement Model")]
        public PlacementMode Mode = PlacementMode.Static;

        [Tooltip("Used by Blob/Lichtenberg as % of total cells to aim for.")]
        [Range(0f, 1f)] public float CoveragePercent = 0.10f;



        [Header("Rules")]
        [Tooltip("If true: the generated cells may place on blocked cells (obstacles). If false: blocked cells are forbidden from being overwritten.")]
        public bool AllowOverwriteObstacle = false;

        [Tooltip("If true: can only paint on base tiles (terrainId==0).")]
        public bool OnlyAffectBase = true;

        [Tooltip("If true: can overwrite other terrain types (overwriten by onlyOnBase).")]
        public bool AllowOverwriteTerrain = false;

        [Tooltip("Optional ordering: lower first, higher later.")]
        [Min(1)] public int Order = 1;




        [Header("Blob Params")]
        public BlobParams Blob = new BlobParams
        {
            AvgSize = 120,
            SizeJitter = 60,
            MinBlobs = 6,
            MaxBlobs = 30,
            GrowChance = 0.55f,
            SmoothPasses = 1
        };

        [Header("Lichtenberg Params")]
        public LichtenbergParams Lichtenberg = new LichtenbergParams
        {
            MinPaths = 4,
            MaxPaths = 16,
            CellsPerPath = 180,
            StepsScale = 1.8f,
            MaxWalkers = 14,
            TowardTargetBias = 0.72f,
            BranchChance = 0.18f,
            WidenPasses = 0
        };

        [Header("Static Params")]
        public StaticParams Static = new StaticParams
        {
            Chance = 0.55f
        };


        [System.Serializable] public struct StaticParams 
        { 
            [Range(0f, 1f)] public float Chance; 
        }

        [System.Serializable] public struct BlobParams 
        { 
            public int AvgSize;
            public int SizeJitter;
            public int MinBlobs;
            public int MaxBlobs; 
            [Range(0f, 1f)] public float GrowChance;
            [Range(0f, 8)] public int SmoothPasses; 
        }

        [System.Serializable] public struct LichtenbergParams 
        { 
            public int MinPaths;
            public int MaxPaths;
            public int CellsPerPath;
            [Range(0.5f, 6f)] public float StepsScale;
            [Range(1, 64)] public int MaxWalkers;
            [Range(0f, 1f)] public float TowardTargetBias;
            [Range(0f, 1f)] public float BranchChance;
            [Range(0, 6)] public int WidenPasses; 
        }

    }


}

