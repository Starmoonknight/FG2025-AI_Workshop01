using System;
using UnityEngine;

namespace AI_Workshops.Workshop02
{
    public class BoardManager : MonoBehaviour
    {

        [Header("Game Camera Settings")]
        [SerializeField]
        private Camera   _mainCamera;
        [SerializeField]
        private float    _cameraPadding = 1f;

        [Header("Board Settings")]
        [SerializeField]
        private Renderer _quadRenderer;
        [SerializeField, Min(1)]
        private int      _width = 10;
        [SerializeField, Min(1)]
        private int      _height = 10;

        [Header("Map Generation Settings")]
        [SerializeField]
        private int   _seed;
        [SerializeField, Range(0f, 1f)]
        private float _obstaclePercent = 0.2f;
        [SerializeField, Range(0f, 1f)] 
        private float _minReachablePercent = 0.75f;
        [SerializeField] 
        private int _maxGenerateAttempts = 50;

        private System.Random _genRng;
        private System.Random _goalRng;

        // Accessability Data
        private int[] _bfsQueue; 
        private int[] _reachStamp;
        private int   _reachStampId;

        // Grid Data
        private int       _cellCount;
        private bool[]    _walkable;
        private byte[]    _terrainCost;

        // Grid Visualization
        private Color32[] _baseCellColors;
        private Color32[] _cellColors;
        private Texture2D _gridTexture;
        private bool      _textureDirty; 

        [Header("Colors")]
        [SerializeField] 
        private Color32 _walkableColor = new(255, 255, 255, 255);    // White
        [SerializeField] 
        private Color32 _obstacleColor = new(0, 0, 0, 255);           // Black
        [SerializeField]
        private Color32 _unReachableColor = new(255, 150, 150, 255); // Light Red


        public int Width => _width;
        public int Height => _height;
        public int CellCount => _cellCount;


        // Neighbor offsets for 8-directional movement with associated step costs, dx stands for change in x, dy for change in y
        private static readonly (int dx, int dy, int stepCost)[] Neighbors8 =
        {
            (-1,  0, 10),  //Left
            ( 1,  0, 10),  //Right
            ( 0, -1, 10),  //Down
            ( 0,  1, 10),  //Up
            (-1, -1, 14),  //Bottom-Left
            ( 1, -1, 14),  //Bottom-Right
            (-1,  1, 14),  //Top-Left
            ( 1,  1, 14)   //Top-Right
        };



        private void Awake()
        {
            ValidateGridSize();
        
            _cellCount      = _width * _height;

            _walkable       = new bool[_cellCount];
            _terrainCost    = new byte[_cellCount];
            _baseCellColors = new Color32[_cellCount];
            _cellColors     = new Color32[_cellCount];

            int seed = (_seed != 0) ? _seed : Environment.TickCount;
            _genRng = new System.Random(seed);
            _goalRng = new System.Random(seed ^ unchecked((int)0x9E3779B9));

            // Initialize all cells as walkable with default terrain cost
            for (int i = 0; i < _cellCount; i++)
            {
                _walkable[i]        = true;
                _terrainCost[i]     = 10;
                _baseCellColors[i]  = _walkableColor; 
            }
            
            GenerateSeededObstaclesUntilAcceptable();

            _gridTexture            = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
            _gridTexture.filterMode = FilterMode.Point;
            _gridTexture.wrapMode   = TextureWrapMode.Clamp;

            RebuildCellColorsFromBase();    
            FlushTexture();

            _quadRenderer.transform.localScale = new Vector3(_width, _height, 1f);
            _quadRenderer.transform.position = new Vector3(_width * 0.5f, _height * 0.5f, 0f);   // Center the quad, works in XY plane
            //_quadRenderer.transform.position = new Vector3(_width * 0.5f, 0f, _height * 0.5f);   // Center the quad, works in XZ plane

            var mat = _quadRenderer.material;

            // set mainTexture (should cover multiple shaders)
            mat.mainTexture = _gridTexture;

            // set whichever property the shader actually uses
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _gridTexture);   // URP
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _gridTexture);   // Built-in

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            FitCameraOrthoTopDown();
        }


        private void LateUpdate()
        {
            if (!_textureDirty) return;
            _textureDirty = false;
            RefreshTexture();
        }



        #region Cell Data Getters

        // Checks if cell coordinates or index are within bounds
        public bool IsValidCell(int x, int y) => (uint)x < (uint)_width && (uint)y < (uint)_height;
        public bool IsValidCell(int index) => (uint)index < (uint)_cellCount;


        // check if cell is walkable
        public bool GetWalkable(int x, int y) => GetWalkable(CoordToIndex(x, y));
        public bool GetWalkable(int index)
        {
            if (!IsValidCell(index)) throw new ArgumentOutOfRangeException(nameof(index));
            return _walkable[index];
        }


        // check terrain cost
        public byte GetTerrainCost(int x, int y) => GetTerrainCost(CoordToIndex(x, y));
        public byte GetTerrainCost(int index)
        {
            if (!IsValidCell(index)) throw new ArgumentOutOfRangeException(nameof(index));
            return _terrainCost[index];
        }


        public int BuildReachableFrom(int startX, int startY) => BuildReachableFrom(CoordToIndex(startX, startY));
        public int BuildReachableFrom(int startIndex)
        {
            EnsureReachBuffers();
            if (!IsValidCell(startIndex) || !_walkable[startIndex])
                return 0;

            // Prevent stamp id overflow, rare but possible
            if (_reachStampId == int.MaxValue)
            {
                Array.Clear(_reachStamp, 0, _reachStamp.Length);
                _reachStampId = 0; // so next ++ becomes 1
            }

            _reachStampId++;

            int head = 0;
            int tail = 0;

            _bfsQueue[tail++] = startIndex;
            _reachStamp[startIndex] = _reachStampId;

            int reachableCount = 1;

            while (head < tail)
            {
                int currentIndex = _bfsQueue[head++];
                IndexToXY(currentIndex, out int coordX, out int coordY);

                foreach (var (dx, dy, _) in Neighbors8)
                {
                    if (dx != 0 && dy != 0)
                    {
                        // Diagonal movement, check for corner cutting
                        if (!TryToIndex(coordX + dx, coordY, out int sideIndexA) || !_walkable[sideIndexA])
                            continue;
                        if (!TryToIndex(coordX, coordY + dy, out int sideIndexB) || !_walkable[sideIndexB])
                            continue;
                    }

                    TryEnqueue(coordX + dx, coordY + dy);
                }
            }

            return reachableCount;

            void TryEnqueue(int newX, int newY)
            {
                if (!TryToIndex(newX, newY, out int ni)) return;
                if (!_walkable[ni]) return;
                if (_reachStamp[ni] == _reachStampId) return;

                _reachStamp[ni] = _reachStampId;
                _bfsQueue[tail++] = ni;
                reachableCount++;
            }

        }


        // If I want the goal to be far-ish away, can also pick minManhattan as something like (_width + _height) / 4. 
        // This ensures the goal is at least a quarter of the board’s perimeter away from the start.
        public void BuildVisualReachableFrom(int startX, int startY) => BuildVisualReachableFrom(CoordToIndex(startX, startY));
        public void BuildVisualReachableFrom(int startIndex)
        {
            int reachableCount = BuildReachableFrom(startIndex);

            RebuildCellColorsFromBase();

            for (int i = 0; i < _cellCount; i++)
            {
                if (!_walkable[i]) continue;

                bool isReachable = (_reachStamp[i] == _reachStampId);
                if (!isReachable)
                {
                    IndexToXY(i, out int x, out int y);
                    bool odd = ((x + y) & 1) == 1;
                    _cellColors[i] = ApplyGridShading(_unReachableColor, odd);
                }
            }

            _textureDirty = true;
        }


        public bool TryPickRandomReachableGoal(int startX, int startY, int minManhattan, out int goalIndex) => 
            TryPickRandomReachableGoal(CoordToIndex(startX, startY), minManhattan, out goalIndex);
        public bool TryPickRandomReachableGoal(int startIndex, int minManhattan, out int goalIndex)
        {
            goalIndex = -1;

            int reachableCount = BuildReachableFrom(startIndex);
            if (reachableCount <= 1) return false;

            IndexToXY(startIndex, out int startX, out int startY);

            int candidateCount = 0;

            for (int i = 0; i < _cellCount; i++)
            {
                if (!_walkable[i]) continue;                    // skip unwalkable cells
                if (_reachStamp[i] != _reachStampId) continue;  // if not reachable in current step
                if (i == startIndex) continue;                  // skip starting cell

                IndexToXY(i, out int cellX, out int cellY);
                int manhattan = Math.Abs(cellX - startX) + Math.Abs(cellY - startY);
                if (manhattan < minManhattan) continue;

                candidateCount++;

                // Reservoir sampling: each candidate has a 1/candidateCount chance to be selected
                if (_goalRng.Next(candidateCount) == 0)
                    goalIndex = i;
            }

            return goalIndex != -1;
        }

        #endregion


        #region Cell Data Setter

        public void SetWalkable (int x, int y, bool walkable) => SetWalkable(CoordToIndex(x, y), walkable);
        public void SetWalkable(int index, bool walkable)
        {
            if (!IsValidCell(index)) throw new ArgumentOutOfRangeException(nameof(index));

            _walkable[index] = walkable;
            _baseCellColors[index] = walkable 
                ? _walkableColor 
                : _obstacleColor;

            IndexToXY(index, out int coordX, out int coordY);
            bool odd = ((coordX + coordY) & 1) == 1;
            _cellColors[index] = ApplyGridShading(_baseCellColors[index], odd);

            _textureDirty = true;   
        }

        public void SetTerrainCost(int x, int y, byte terrainCost) => SetTerrainCost(CoordToIndex(x, y), terrainCost);
        public void SetTerrainCost(int index, byte terrainCost)
        {
            if (!IsValidCell(index)) throw new ArgumentOutOfRangeException(nameof(index));
            _terrainCost[index] = terrainCost;
        }

        public void SetCellData(int x, int y, bool walkable, byte terrainCost) => SetCellData(CoordToIndex(x, y), walkable, terrainCost);
        public void SetCellData(int index, bool walkable, byte terrainCost)
        {
            if (!IsValidCell(index)) throw new ArgumentOutOfRangeException(nameof(index));
            _walkable[index] = walkable;
            _terrainCost[index] = terrainCost;
        }

        public void PaintCell(int x, int y, Color32 color, bool shadeLikeGrid = true, bool skipIfObstacle = true) => 
            PaintCell(CoordToIndex(x, y), color, shadeLikeGrid, skipIfObstacle);
        public void PaintCell(int index, Color32 color, bool shadeLikeGrid = true, bool skipIfObstacle = true)
        {
            if (!IsValidCell(index)) throw new ArgumentOutOfRangeException(nameof(index));
            if (skipIfObstacle && !_walkable[index]) return;

            if (shadeLikeGrid)
            {
                IndexToXY(index, out int coordX, out int coordY);
                bool odd = ((coordX + coordY) & 1) == 1;
                _cellColors[index] = ApplyGridShading(color, odd);
            }
            else
            {
                _cellColors[index] = color;
            }

            _textureDirty = true;
        }

        public void PaintCells(ReadOnlySpan<int> indices, Color32 color, bool shadeLikeGrid = true, bool skipIfObstacle = true)
        {
            for (int i = 0; i < indices.Length; i++)
                PaintCell(indices[i], color, shadeLikeGrid, skipIfObstacle);
        }


        public void ResetColorsToBase()
        {
            RebuildCellColorsFromBase();
            _textureDirty = true;

        }

        public void FlushTexture()
        {
            _textureDirty = false;
            RefreshTexture();
        }

        #endregion


        #region Other Utilities

        // Public version with exception on out of bounds
        public int CoordToIndex(int x, int y)
        {
            if (!TryToIndex(x, y, out int idx))
                throw new ArgumentOutOfRangeException();
            return idx;
        }

        public void IndexToXY(int index, out int x, out int y)
        {
            x = index % _width;
            y = index / _width;
        }

        #endregion


        // Safe version with bounds checking, use when not sure coordinates are valid
        private bool TryToIndex(int x, int y, out int index)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) { index = -1; return false; }
            index = x + y * _width;
            return true;
        }

        // Unsafe version, no bounds checking - Use only when already guaranteed bounds. 
        private int ToIndexUnchecked(int x, int y) => x + y * _width;



        private void GenerateSeededObstaclesUntilAcceptable()
        {
            EnsureReachBuffers();

            int startIndex = CoordToIndex(_width / 2, _height / 2);

            for (int attempt = 0; attempt < _maxGenerateAttempts; attempt++)
            {
                int walkableCount = GenerateSeededObstaclesWithAttempt(attempt);

                if (!_walkable[startIndex])     // force starting cell to be walkable
                {
                    _walkable[startIndex] = true;
                    _baseCellColors[startIndex] = _walkableColor;
                    walkableCount++;            // if it was an obstacle before fix the count
                }

                int reachableCount = BuildReachableFrom(startIndex);

                float reachablePercent = walkableCount == 0 
                    ? 0f 
                    : (reachableCount / (float)walkableCount);

                if (reachablePercent >= _minReachablePercent)
                {
                    return;
                }
            }

            Debug.LogWarning("Failed to generate acceptable obstacle layout within max attempts.");
        }

        private int GenerateSeededObstaclesWithAttempt(int attempt)
        {
            var attemptRng = (_seed != 0) 
                ? new System.Random(_seed + attempt) 
                : _genRng;

            int walkableCount = 0;

            for (int i = 0; i < _cellCount; i++)
            {
                bool isObstacle = attemptRng.NextDouble() < _obstaclePercent;

                _walkable[i] = !isObstacle;
                _terrainCost[i] = 10;
                _baseCellColors[i] = isObstacle ? _obstacleColor : _walkableColor;

                if (!isObstacle)
                    walkableCount++;
            }

            return walkableCount;
        }


        private void EnsureReachBuffers()
        {
            if (_bfsQueue == null || _bfsQueue.Length != _cellCount)
                _bfsQueue = new int[_cellCount];

            if (_reachStamp == null || _reachStamp.Length != _cellCount)
                _reachStamp = new int[_cellCount];
        }


        private void RefreshTexture()
        {
            _gridTexture.SetPixels32(_cellColors);
            _gridTexture.Apply(false);
        }

        private void RebuildCellColorsFromBase()
        {
            for (int i = 0; i < _cellCount; i++)
            {
                IndexToXY(i, out int x, out int y);
                bool odd = ((x + y) & 1) == 1;
                _cellColors[i] = ApplyGridShading(_baseCellColors[i], odd);
            }
        }

        private static Color32 ApplyGridShading(Color32 c, bool odd)
        {
            // Small change so it’s visible but not ugly
            const int delta = 12;

            int d = odd ? +delta : -delta;

            byte r = (byte)Mathf.Clamp(c.r + d, 0, 255);
            byte g = (byte)Mathf.Clamp(c.g + d, 0, 255);
            byte b = (byte)Mathf.Clamp(c.b + d, 0, 255);

            return new Color32(r, g, b, c.a);
        }


        private void ValidateGridSize()
        {
            if (_width <= 0) throw new ArgumentOutOfRangeException(nameof(_width));
            if (_height <= 0) throw new ArgumentOutOfRangeException(nameof(_height));

            long count = (long)_width * _height;
            if (count > int.MaxValue) 
                throw new OverflowException("Grid too large for int indexing.");
        }




        private void FitCameraOrthoTopDown()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            _mainCamera.orthographic = true;

            // Center of the board in world space (Quad centered at its transform)
            Vector3 center = _quadRenderer.transform.position;

            // Place camera above the board (assuming quad is in XY plane facing camera OR rotated to XZ)
            _mainCamera.transform.position = center + new Vector3(0f, 0f, -10f); // if viewing in XY plane
            _mainCamera.transform.rotation = Quaternion.identity;

            // Fit whole board in view (orthographicSize is half of vertical size)
            float halfH = _height * 0.5f;
            float halfW = _width * 0.5f;

            float aspect = _mainCamera.aspect; // width / height
            float sizeToFitHeight = halfH;
            float sizeToFitWidth = halfW / aspect;

            _mainCamera.orthographicSize = Mathf.Max(sizeToFitHeight, sizeToFitWidth) + _cameraPadding;
        }



#if UNITY_EDITOR
        private void OnValidate() => ValidateGridSize();
#endif

    }

}
