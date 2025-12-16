using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;


namespace AI_Workshops.Workshop02
{
    public class NavigationService : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private BoardManager _boardManager;

        [Header("A* Settings")]
        [SerializeField] 
        private bool _allowDiagonals = true;

        [Tooltip("Delay (seconds) between each current node' selection, for visualization.")]
        [SerializeField, Min(0f)] 
        private float _stepDelay = 0.4f;

        [Header("Visualization Colors")]
        [SerializeField] 
        private Color32 _triedColor    = new(185, 0, 255, 255);    // closed,          purple
        [SerializeField] 
        private Color32 _frontierColor = new(180, 170, 255, 255);  // open (optional), light purple-blue
        [SerializeField] 
        private Color32 _pathColor     = new(6, 225, 25, 255);     // final path,      green

        // Internal data
        private int      _totalCells;
        private int      _searchId;
        private MinHeap  _open; 

        private int[]    _fCost;
        private int[]    _gCost;
        private int[]    _hCost;
        private int[]    _parent;
        private ushort[] _seenId;
        private byte[]   _state;



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
            if (_boardManager == null) _boardManager = FindFirstObjectByType<BoardManager>();
            EnsureCapacity();
        }

        private void EnsureCapacity()
        {
            _totalCells = _boardManager.CellCount;

            if (_fCost == null  || _fCost.Length  != _totalCells) _fCost  = new int[_totalCells];
            if (_gCost == null  || _gCost.Length  != _totalCells) _gCost  = new int[_totalCells];
            if (_hCost == null  || _hCost.Length  != _totalCells) _hCost  = new int[_totalCells];
            if (_parent == null || _parent.Length != _totalCells) _parent = new int[_totalCells];
            if (_seenId == null || _seenId.Length != _totalCells) _seenId = new ushort[_totalCells];
            if (_state == null  || _state.Length  != _totalCells) _state  = new byte[_totalCells];

            if (_open == null || _open.Capacity < _totalCells) _open = new MinHeap(_totalCells);

        }


        #region Public API

        #endregion



        #region A* Implementation

        #endregion



        #region Minimal Heap Implementation (priority queue)

        private sealed class MinHeap
        {
            private int[] _items;       // node indices
            private int[] _priority;    // priorities (fCost)
            private int[] _heapPos;     // nodeIndex -> heap position, -1 if not in heap
            private int _count;

            public int Count => _count;
            public int Capacity => _items.Length;

            public MinHeap(int capacity)
            {
                _items    = new int[capacity];
                _priority = new int[capacity];
                _heapPos  = new int[capacity];
                Array.Fill(_heapPos, -1);
                _count = 0;
            }

            public void Clear()
            {
                for (int i = 0; i < _count; i++)
                    _heapPos[_items[i]] = -1;
                _count = 0;

            }

            public void Push(int nodeIndex, int priority)
            {
                int pos = _heapPos[nodeIndex];
                if (pos != -1)
                {
                    DecreaseKeyIfBetter(nodeIndex, priority);
                    return;
                }

                int i               = _count++;
                _items[i]           = nodeIndex;
                _priority[i]        = priority;
                _heapPos[nodeIndex] = i;
                SiftUp(i);
            }

            public int PopMin()
            {
                int min = _items[0];
                _heapPos[min] = -1;

                _count--;
                if (_count > 0)
                {
                    _items[0]           = _items[_count];
                    _priority[0]        = _priority[_count];
                    _heapPos[_items[0]] = 0;
                    SiftDown(0);
                }

                return min;
            }

            public void DecreaseKeyIfBetter(int nodeIndex, int newPriority)
            {
                int pos = _heapPos[nodeIndex];
                if (pos == -1 || _priority[pos] <= newPriority)
                    return;

                _priority[pos] = newPriority;
                SiftUp(pos);
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (_priority[index] >= _priority[parent]) break;
                    Swap(index, parent);
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                while(true)
                {
                    int leftChild  = (index << 1) + 1;
                    int rightChild = leftChild + 1;
                    int smallest   = index;

                    if (leftChild < _count && _priority[leftChild] < _priority[smallest])
                        smallest = leftChild;

                    if (rightChild < _count && _priority[rightChild] < _priority[smallest])
                        smallest = rightChild;

                    if (smallest == index) break;

                    Swap(index, smallest);
                    index = smallest;
                }
            }

            private void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_priority[a], _priority[b]) = (_priority[b], _priority[a]);

                _heapPos[_items[a]] = a;
                _heapPos[_items[b]] = b;
            }
        }

        #endregion



        /*
        public int GetGCost(int x, int y) => _gCost[_boardManager.CoordToIndex(x, y)];
        public int GetParentIndex(int x, int y) => _parentIndex[_boardManager.CoordToIndex(x, y)];

        public void SetGCost(int x, int y, int gCost)
        {
            int index = _boardManager.CoordToIndex(x, y);
            _gCost[index] = gCost;
        }
        */

    }
}
