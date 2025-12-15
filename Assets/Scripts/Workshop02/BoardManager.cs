using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace AI_Workshops.Workshop02
{
    public class BoardManager : MonoBehaviour
    {

        [SerializeField, Min(1)]
        private int     _width = 10;
        [SerializeField, Min(1)]
        private int     _height = 10;
        [SerializeField]
        private int     _seed;

        [SerializeField, Range(0f, 1f)]
        private float _obstaclePercent = 0.2f;

        // Grid Data
        private bool[]    _walkable;
        private byte[]    _terrainCost;
        private Color32[] _baseCellColors;
        private Color32[] _cellColors;
        private Texture2D _gridTexture;


        public int Width => _width;
        public int Height => _height;
        public int CellCount => _width * _height;




        private void Awake()
        {
            ValidateGridSize();
        
            int cellCount   = _width * _height;
            _walkable       = new bool[cellCount];
            _terrainCost    = new byte[cellCount];
            _baseCellColors = new Color32[cellCount];
            _cellColors     = new Color32[cellCount];

            // Initialize all cells as walkable with default terrain cost
            for (int i = 0; i < cellCount; i++)
            {
                _walkable[i] = true;
                _terrainCost[i] = 10;
                _baseCellColors[i] = new Color32(255, 255, 255, 255); // White color
            }

            _gridTexture = new Texture2D(_width, _height);
            _gridTexture.SetPixels32(_cellColors);
            _gridTexture.Apply(); 

        }



        public bool IsValidCell(int x, int y) => (uint)x < (uint)_width && (uint)y < (uint)_height;
        public bool IsValidCell(int index) => (uint)index < (uint)CellCount;

        public bool GetWalkable(int x, int y) => _walkable[ToIndex(x, y)];
        public bool GetWalkable(int index) => _walkable[index];
        public void SetWalkable (int x, int y, bool walkable)
        {
            _walkable[ToIndex(x, y)] = walkable;
        }

        public byte GetTerrainCost(int x, int y) => _terrainCost[ToIndex(x, y)];
        public byte GetTerrainCost(int index) => _terrainCost[index];
        public void SetTerrainCost(int x, int y, byte terrainCost)
        {
            int index = ToIndex(x, y);
            _terrainCost[index] = terrainCost;
        }

        public void SetCellData(int x, int y, bool walkable, byte terrainCost)
        {
            int index = ToIndex(x, y);
            _walkable[index] = walkable;
            _terrainCost[index] = terrainCost;
        }



        // Public version with exception on out of bounds
        public int ToIndex(int x, int y)
        {
            if (!TryToIndex(x, y, out int idx))
                throw new ArgumentOutOfRangeException();
            return idx;
        }

        // Safe version with bounds checking, use when not sure coordinates are valid
        private bool TryToIndex(int x, int y, out int index)
        {
            if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) { index = -1; return false; }
            index = x + y * _width;
            return true;
        }

        // Unsafe version, no bounds checking - Use only when already guaranteed bounds. 
        private int ToIndexUnchecked(int x, int y) => x + y * _width;

        public void ToXY(int index, out int x, out int y)
        {
            x = index % _width;
            y = index / _width;
        }



        private void ValidateGridSize()
        {
            if (_width <= 0) throw new ArgumentOutOfRangeException(nameof(_width));
            if (_height <= 0) throw new ArgumentOutOfRangeException(nameof(_height));

            long count = (long)_width * _height;
            if (count > int.MaxValue) 
                throw new OverflowException("Grid too large for int indexing.");
        }








#if UNITY_EDITOR
        private void OnValidate() => ValidateGridSize();
#endif

    }

}
