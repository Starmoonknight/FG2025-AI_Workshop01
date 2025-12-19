using System;
using System.Collections.Generic;
using UnityEngine;



namespace AI_Workshop02
{

    public sealed class GenScratch
    {
        public int[] queue;
        public int[] stamp;
        public int stampId;
        public readonly List<int> cells = new(4096);
        public readonly List<int> temp = new(2048);
    }


    public sealed class BoardGenerator
    {
            
        private readonly GenScratch _scratch = new();

        private int _width;
        private int _height;
        private int _cellCount;

        private bool[] _blocked;
        private int[]  _terrainCost;
        private Color32[] _baseColors;
        private byte[] _terrainId;

        private Color32 _baseWalkableColor;
        private int _baseWalkableCost;

        private System.Random _rng;

        private static readonly (int dirX, int dirY)[] Neighbors4 =
        {
            (-1, 0), ( 1, 0), (0, -1), (0,  1)
        };

        public void Generate(
            int width,
            int height,
            bool[] blocked,
            int[] terrainCost,
            Color32[] baseColors,
            byte[] terrainId,
            Color32 baseWalkableColor,
            byte baseWalkableCost,
            int seed,
            TerrainRule[] terrainRules,
            int maxGenerateAttempts,
            float minReachablePercent,
            Func<int, int> buildReachableFrom // callback into BoardManager
        )
        {

            // --- Get reference pointers to current game board ---
            _width = width;
            _height = height;
            _cellCount = checked(width * height);

            _blocked = blocked ?? throw new ArgumentNullException(nameof(blocked));
            _terrainCost = terrainCost ?? throw new ArgumentNullException(nameof(terrainCost));
            _baseColors = baseColors ?? throw new ArgumentNullException(nameof(baseColors));
            _terrainId = terrainId ?? throw new ArgumentNullException(nameof(terrainId));

            if (_blocked.Length != _cellCount || _terrainCost.Length != _cellCount ||
                _baseColors.Length != _cellCount || _terrainId.Length != _cellCount)
                throw new ArgumentException("Board arrays length mismatch.");

            _baseWalkableColor = baseWalkableColor;
            _baseWalkableCost = baseWalkableCost;

            _rng = new System.Random(seed);

            terrainRules ??= Array.Empty<TerrainRule>();


            // --- Organize all terrains in use  ---
            Array.Sort(terrainRules, (a, b) => (a?.Order ?? 0).CompareTo(b?.Order ?? 0));
                
        
            var ruleId = new Dictionary<TerrainRule, byte>(terrainRules.Length);            // terrainId is assigned by list order after sorting, 0 reserved for base
            byte nextId = 1;
            for (int i = 0; i < terrainRules.Length; i++)
            {
                if (terrainRules[i] == null) continue;
                if (nextId == byte.MaxValue) break;                                         // safety cap, if too many terrain types are listed 
                ruleId[terrainRules[i]] = nextId++;
            }

            int startIndex = CoordToIndex(_width / 2, _height / 2);

            // --- Atempt to generate base game map ---
            for (int attempt = 0; attempt < Math.Max(1, maxGenerateAttempts); attempt++)    // attempt placement loop
            {
                ResetToBase();
                        
                for (int i = 0; i < terrainRules.Length; i++)                               // obstacle placement before other terrain types
                {
                    var rule = terrainRules[i];
                    if (rule == null || !rule.IsObstacle) continue;

                    byte id = ruleId.TryGetValue(rule, out var terrId) ? terrId : (byte)0;
                    ApplyRule(rule, id, isObstacle: true);
                }
                        
                if (_blocked[startIndex]) continue;                                         // BuildReachableFrom() will use startIndex to validate the board’s navigability,
                                                                                            // that BFS needs a walkable starting node to measure “how connected is this map” from center,
                                                                                            // if it fails, generate a new map. 

                int walkableCount = CountWalkable();
                if (walkableCount <= 0) continue;

                int reachableCount = buildReachableFrom(startIndex);
                float reachablePercent = reachableCount / (float)walkableCount;

                if (reachablePercent >= minReachablePercent)
                {
                    ResetWalkableToBaseOnly();                                              // reset walkable tiles to base visuals/cost/id so terrain can build from clean base

                    for (int i = 0; i < terrainRules.Length; i++)
                    {
                        var rule = terrainRules[i];
                        if (rule == null || rule.IsObstacle) continue;

                        byte id = ruleId.TryGetValue(rule, out var tid) ? tid : (byte)0;
                        ApplyRule(rule, id, isObstacle: false);
                    }

                    return;
                }
            }

            // --- Fallback if to many attempts, keep last version and ensure walkable visuals are consistent ---
            ResetWalkableToBaseOnly();
            for (int i = 0; i < terrainRules.Length; i++)
            {
                var rule = terrainRules[i];
                if (rule == null || rule.IsObstacle) continue;

                byte id = ruleId.TryGetValue(rule, out var tid) ? tid : (byte)0;
                ApplyRule(rule, id, isObstacle: false);
            }
        }




        #region Cell Data

        private void ApplyRule(TerrainRule rule, byte ruleTerrainId, bool isObstacle)
        {
            _scratch.cells.Clear();

            switch (rule.Mode)                                                              // what "paint brush" is used to generate this tiles structure
            {
                case PlacementMode.Static:
                    ExpandRandomStatic(rule, _scratch.cells);
                    break;

                case PlacementMode.Blob:
                    GenerateBlobs(rule, _scratch.cells);
                    break;

                case PlacementMode.Lichtenberg:
                    GenerateLichtenberg(rule, _scratch.cells);
                    break;
            }

            if (_scratch.cells.Count == 0) return;

            if (isObstacle)
                ApplyObstacles(rule, ruleTerrainId, _scratch.cells);
            else
                ApplyTerrain(rule, ruleTerrainId, _scratch.cells);
        }

        
        private void GenerateBlobs(TerrainRule rule, List<int> outCells)
        {
            outCells.Clear();

            int desiredCells = Mathf.RoundToInt(rule.CoveragePercent * _cellCount);
            if (desiredCells <= 0) return;

            int avgSize = Mathf.Max(1, rule.Blob.AvgSize);
            int blobCount = desiredCells / avgSize;
            blobCount = Mathf.Clamp(blobCount, rule.Blob.MinBlobs, rule.Blob.MaxBlobs);

            for (int b = 0; b < blobCount; b++)
            {
                if (!TryPickRandomValidCell(rule, out int seed, 256))
                    break;

                int size = avgSize + _rng.Next(-rule.Blob.SizeJitter, rule.Blob.SizeJitter + 1);
                size = Mathf.Max(10, size);

                _scratch.temp.Clear();
                ExpandRandomBlob(rule, seed, size, _scratch.temp);
                outCells.AddRange(_scratch.temp);
            }
        }

        
        private void GenerateLichtenberg(TerrainRule rule, List<int> outCells)
        {
            outCells.Clear();

            int desiredCells = Mathf.RoundToInt(rule.CoveragePercent * _cellCount);
            if (desiredCells <= 0) return;

            int perPath = Mathf.Max(1, rule.Lichtenberg.CellsPerPath);
            int pathCount = desiredCells / perPath;
            pathCount = Mathf.Clamp(pathCount, rule.Lichtenberg.MinPaths, rule.Lichtenberg.MaxPaths);

            int maxSteps = Mathf.RoundToInt((_width + _height) * rule.Lichtenberg.StepsScale);

            for (int r = 0; r < pathCount; r++)
            {
                if (!TryPickRandomEdgeValidCell(rule, out int start, 256)) break;
                if (!TryPickRandomEdgeValidCell(rule, out int goal, 256)) break;

                _scratch.temp.Clear();
                ExpandRandomLichtenberg(rule, start, goal, maxSteps, _scratch.temp);

                // NOTE: Should be put behind a condition and not hard co
                for (int p = 0; p < rule.Lichtenberg.WidenPasses; p++)
                    WidenOnce(rule, _scratch.temp);

                outCells.AddRange(_scratch.temp);
            }
        }

        #endregion


        #region Update and Overwrite Cell Data

        private void ApplyTerrain(TerrainRule rule, byte ruleTerrainId, List<int> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                int index = cells[i];
                if (!IsValidCell(index)) continue;

                if (!CanUseCell(rule, index)) continue;

                if (_blocked[index] && rule.AllowOverwriteObstacle)     // ? or should I use: _blocked[index] = rule.IsObstacle;
                    _blocked[index] = false;
                                
                _terrainCost[index] = rule.Cost;
                _baseColors[index] = rule.Color;
                _terrainId[index] = ruleTerrainId;
            }
        }


        // MAIN DESIGN QUESTION:
        // Should obstacles overwrite _terrainId? (do obstacles preserve underlying terrainId, or do they force it to 0?)
        // Otherwise I could use only one Apply metgod instead of seperate once for terrain and obstacles
        private void ApplyObstacles(TerrainRule rule, byte ruleTerrainId, List<int> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                int index = cells[i];
                if (!IsValidCell(index)) continue;

                if (!CanUseCell(rule, index)) continue;

                _blocked[index] = true;
                _terrainCost[index] = 0;
                _terrainId[index] = 0;
                _baseColors[index] = rule.Color;
            }
        }

        #endregion


        #region Expansion Algorithms  - rng and modifier 

        private void ExpandRandomStatic(
            TerrainRule rule, 
            List<int> outCells)
        {
            outCells.Clear();
            float chance = Mathf.Clamp01(rule.Static.Chance);

            for (int i = 0; i < _cellCount; i++)
            {
                if (!CanUseCell(rule, i)) continue;
                if (_rng.NextDouble() <= chance)
                    outCells.Add(i);
            }
        }

        
        private void ExpandRandomBlob(
            TerrainRule rule,
            int seedIndex, 
            int maxCells, 
            List<int> outCells)
        {
            outCells.Clear();
            if (!IsValidCell(seedIndex)) return;
            if (!CanUseCell(rule, seedIndex)) return;

            EnsureGenBuffers();
            int stampId = NextStampId();

            int head = 0;
            int tail = 0;

            _scratch.stamp[seedIndex] = stampId;
            _scratch.queue[tail++] = seedIndex;
            outCells.Add(seedIndex);

            float growChance = Mathf.Clamp01(rule.Blob.GrowChance);
            int smoothPasses = rule.Blob.SmoothPasses; 
            maxCells = Mathf.Max(1, maxCells);

            // BFS-like growth
            while (head < tail && outCells.Count < maxCells)
            {
                int current = _scratch.queue[head++];
                IndexToXY(current, out int x, out int y);

                for (int neighbor = 0; neighbor < Neighbors4.Length; neighbor++)
                {
                    var (dirX, dirY) = Neighbors4[neighbor];
                    if (!TryCoordToIndex(x + dirX, y + dirY, out int next)) continue;
                    if (_scratch.stamp[next] == stampId) continue;
                    if (!CanUseCell(rule, next)) continue;
                    if (_rng.NextDouble() > growChance) continue;

                    _scratch.stamp[next] = stampId;
                    _scratch.queue[tail++] = next;
                    outCells.Add(next);

                    if (outCells.Count >= maxCells) break;
                }
            }

            // Smoothing passes to fill in small gaps
            for (int pass = 0; pass < smoothPasses; pass++)
            {
                int before = outCells.Count;
                for (int i = 0; i < before; i++) _scratch.queue[i] = outCells[i];

                for (int i = 0; i < before && outCells.Count < maxCells; i++)
                {
                    int current = _scratch.queue[i];
                    IndexToXY(current, out int x, out int y);

                    for (int neighbor = 0; neighbor < Neighbors4.Length && outCells.Count < maxCells; neighbor++)
                    {
                        var (dirX, dirY) = Neighbors4[neighbor];
                        if (!TryCoordToIndex(x + dirX, y + dirY, out int next)) continue;
                        if (_scratch.stamp[next] == stampId) continue;
                        if (!CanUseCell(rule, next)) continue;

                        _scratch.stamp[next] = stampId;
                        outCells.Add(next);
                    }
                }
            }
        }


        // private void ExpandRandomBlob(int seedIndex, int maxCells, float growChance, int smoothPasses, List<int> outCells, bool requireWalkable)

        private void ExpandRandomLichtenberg(
            TerrainRule rule,
            int startIndex, 
            int targetIndex, 
            int maxSteps, 
            List<int> outCells) 
        {
            outCells.Clear();
            if (!IsValidCell(startIndex) || !IsValidCell(targetIndex)) return;
            if (!CanUseCell(rule, startIndex)) return;
            if (!CanUseCell(rule, targetIndex)) return;

            EnsureGenBuffers();
            int stampId = NextStampId();

            float towardTargetBias = Mathf.Clamp01(rule.Lichtenberg.TowardTargetBias);
            float branchChance = Mathf.Clamp01(rule.Lichtenberg.BranchChance);
            int maxWalkers = Mathf.Clamp(rule.Lichtenberg.MaxWalkers, 1, 64);
            maxSteps = Mathf.Max(1, maxSteps);

            int walkerCount = 1;
            _scratch.queue[0] = startIndex;

            _scratch.stamp[startIndex] = stampId;
            outCells.Add(startIndex);

            IndexToXY(targetIndex, out int targetX, out int targetY);

            for (int step = 0; step < maxSteps; step++)
            {
                int walkerThisStep = step % walkerCount;
                int current = _scratch.queue[walkerThisStep];

                if (current == targetIndex) break;

                IndexToXY(current, out int x, out int y);

                int stepX = Math.Sign(targetX - x);
                int stepY = Math.Sign(targetY - y);

                // Code memo to remember new words;
                // Span is a stack only ref struct
                // Stackalloc allocates a block of memory on the stack, not the heap. It’s extremely fast and automatically freed when the method scope ends (no GC, no pooling).    
                Span<(int dirX, int dirY)> candidates = stackalloc (int, int)[8];
                int cCount = 0;

                bool hasX = stepX != 0;
                bool hasY = stepY != 0;

                if (hasX) candidates[cCount++] = (stepX, 0);
                if (hasY) candidates[cCount++] = (0, stepY);

                if (hasX) { candidates[cCount++] = (stepX, 1); candidates[cCount++] = (stepX, -1); }
                if (hasY) { candidates[cCount++] = (1, stepY); candidates[cCount++] = (-1, stepY); }

                if (hasX) candidates[cCount++] = (-stepX, 0);
                if (hasY) candidates[cCount++] = (0, -stepY);

                int nextIndex = -1;

                // simple shuffle-ish by random start
                int startC = _rng.Next(0, Math.Max(1, cCount));

                for (int k = 0; k < cCount; k++)
                {
                    int c = (startC + k) % cCount;
                    var (dirX, dirY) = candidates[c];

                    if (!TryCoordToIndex(x + dirX, y + dirY, out int cand)) continue;
                    if (!CanUseCell(rule, cand)) continue;

                    bool toward =
                        (dirX == stepX && dirY == 0) ||
                        (dirX == 0 && dirY == stepY);
                    double roll = _rng.NextDouble();

                    if (toward)
                    {
                        if (roll <= towardTargetBias) { nextIndex = cand; break; }
                    }
                    else
                    {
                        if (roll > towardTargetBias) { nextIndex = cand; break; }
                    }

                    if (nextIndex < 0 && _scratch.stamp[cand] != stampId)
                        nextIndex = cand;
                }

                if (nextIndex < 0) break;

                _scratch.queue[walkerThisStep] = nextIndex;

                if (_scratch.stamp[nextIndex] != stampId)
                {
                    _scratch.stamp[nextIndex] = stampId;
                    outCells.Add(nextIndex);
                }

                if (walkerCount < maxWalkers && _rng.NextDouble() <= branchChance)
                {
                    _scratch.queue[walkerCount++] = nextIndex;
                }
            }
        }

        private void WidenOnce(TerrainRule rule, List<int> cells)
        {
            EnsureGenBuffers();
            int stampId = NextStampId();

            // Mark existing
            for (int i = 0; i < cells.Count; i++)
            {
                int index = cells[i];
                if (IsValidCell(index))
                    _scratch.stamp[index] = stampId;
            }

            int originalCount = cells.Count;
            for (int i = 0; i < originalCount; i++)
            {
                int current = cells[i];
                IndexToXY(current, out int x, out int y);

                for (int neighbor = 0; neighbor < Neighbors4.Length; neighbor++)
                {
                    var (dirX, dirY) = Neighbors4[neighbor];
                    if (!TryCoordToIndex(x + dirX, y + dirY, out int next)) continue;
                    if (_scratch.stamp[next] == stampId) continue;
                    if (!CanUseCell(rule, next)) continue;

                    _scratch.stamp[next] = stampId;
                    cells.Add(next);
                }
            }
        }

        #endregion


        #region Pickers

        private bool TryPickRandomValidCell(TerrainRule rule, out int index, int tries)
        {
            index = -1;

            for (int t = 0; t < tries; t++)
            {
                int i = _rng.Next(0, _cellCount);
                if (!CanPickCell(rule, i)) continue;
                index = i;
                return true;
            }

            return false;
        }

        private bool TryPickRandomEdgeValidCell(TerrainRule rule, out int index, int tries)
        {
            index = -1;

            for (int t = 0; t < tries; t++)
            {
                int side = _rng.Next(0, 4);
                int x, y;

                switch (side)
                {
                    case 0: x = 0; y = _rng.Next(0, _height); break;           // left
                    case 1: x = _width - 1; y = _rng.Next(0, _height); break;  // right
                    case 2: x = _rng.Next(0, _width); y = 0; break;            // bottom
                    default: x = _rng.Next(0, _width); y = _height - 1; break; // top
                }

                int i = CoordToIndex(x, y);
                if (!CanPickCell(rule, i)) continue;
                index = i;
                return true;
            }
            return false;
        }

        private bool CanUseCell(TerrainRule rule, int idx)
        {
            // Hard-block overwrite policy
            if (_blocked[idx] && !rule.AllowOverwriteObstacle) return false;    // if the rule doesn't allow overwriting obstacles, blocked cells are forbidden.  

            // Underlying terrain overwrite policy  (_terrainId gating)                     
            if (rule.OnlyAffectBase)
            {
                return _terrainId[idx] == 0;                // can this only effect base terrain tile?  
            }
            else if (!rule.AllowOverwriteTerrain)
            {
                return _terrainId[idx] == 0;                // if terrain is not a base tile, may it overwrite it?
            }

            return true;
        }

        private bool CanPickCell(TerrainRule rule, int idx)
        {
            if (rule.ForceUnblockedSeed && _blocked[idx]) return false;
            return CanUseCell(rule, idx);
        }

        #endregion


        #region Reset Helpers

        private void ResetToBase()
        {
            EnsureGenBuffers();

            for (int i = 0; i < _cellCount; i++)
            {
                _blocked[i] = false;
                _terrainCost[i] = _baseWalkableCost;
                _baseColors[i] = _baseWalkableColor;
                _terrainId[i] = 0;
            }
        }

        private void ResetWalkableToBaseOnly()
        {
            for (int i = 0; i < _cellCount; i++)
            {
                if (_blocked[i]) continue;
                _terrainCost[i] = _baseWalkableCost;
                _baseColors[i] = _baseWalkableColor;
                _terrainId[i] = 0;
            }
        }

        private int CountWalkable()
        {
            int count = 0;
            for (int i = 0; i < _cellCount; i++)
                if (!_blocked[i]) count++;
            return count;
        }

        #endregion


        #region Coordinate and Stamp data 

        private bool IsValidCell(int index) => (uint)index < (uint)_cellCount;

        private int CoordToIndex(int x, int y) => x + y * _width;

        private bool TryCoordToIndex(int x, int y, out int index)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            {
                index = -1;
                return false;
            }
            index = x + y * _width;
            return true;
        }

        private void IndexToXY(int index, out int x, out int y)
        {
            x = index % _width;
            y = index / _width;
        }

        private void EnsureGenBuffers()
        {
            if (_scratch.queue == null || _scratch.queue.Length != _cellCount)
                _scratch.queue = new int[_cellCount];

            if (_scratch.stamp == null || _scratch.stamp.Length != _cellCount)
                _scratch.stamp = new int[_cellCount];
        }

        private int NextStampId()
        {
            _scratch.stampId++;
            if (_scratch.stampId == int.MaxValue)
            {
                Array.Clear(_scratch.stamp, 0, _scratch.stamp.Length);
                _scratch.stampId = 1;
            }
            return _scratch.stampId;
        }

        #endregion

    }


}

