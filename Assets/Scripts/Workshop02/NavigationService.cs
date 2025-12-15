using AI_Workshops.Workshop02;
using UnityEngine;

public class NavigationService : MonoBehaviour
{
    private BoardManager _boardManager;

    private int _totalCells;
    private int[] _gCost;
    private int[] _parentIndex;
    private ushort[] _seenID;
    private ushort _currentSeenID;


    private void Awake()
    {
        _boardManager = FindFirstObjectByType<BoardManager>();
        _totalCells = _boardManager.Width * _boardManager.Height;
        _gCost = new int[_totalCells];
        _parentIndex = new int[_totalCells];
        _seenID = new ushort[_totalCells];
        _currentSeenID = 0;
    }


    public int GetGCost(int x, int y) => _gCost[_boardManager.ToIndex(x, y)];
    public int GetParentIndex(int x, int y) => _parentIndex[_boardManager.ToIndex(x, y)];

    public void SetGCost(int x, int y, int gCost)
    {
        int index = _boardManager.ToIndex(x, y);
        _gCost[index] = gCost;
    }

}
