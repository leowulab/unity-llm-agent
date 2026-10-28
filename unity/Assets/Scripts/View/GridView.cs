using System.Collections.Generic;
using UnityEngine;
using GridGame.Core;

public class GridView : MonoBehaviour
{
    [SerializeField] private Sprite squareSprite;   //Create a square of 2d Sprite and put in.
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 10;
    [SerializeField] private int maxSteps = 40;
    [SerializeField] private int seed = 1;
    //Step 2:
    [SerializeField] private int visionRadius = 2;
    [SerializeField] private ObservationMode observationMode = ObservationMode.VisibleOnly;
    [SerializeField] private bool showFullMapDebug = false;   // develope model can see all map, when turn off model can see the fog

    public GridGame.Core.GridGame Game { get; private set; }

    public ObservationMode Mode
    {
        get => observationMode;
        set => observationMode = value;
    }

    private SpriteRenderer[,] _tileRenderers;
    private SpriteRenderer _playerRenderer;

    private void Start()
    {
        Game = new GridGame.Core.GridGame(width, height, maxSteps, seed, visionRadius);
        BuildView();
        Refresh();
    }

    private void BuildView()
    {
        _tileRenderers = new SpriteRenderer[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _tileRenderers[x, y] = CreateSquare($"Tile_{x}_{y}", x, y, 0);

        _playerRenderer = CreateSquare("Player", 0, 0, 1);
        _playerRenderer.color = Color.cyan;
        _playerRenderer.transform.localScale = Vector3.one * 0.6f;

        // Center the camera 
        Camera.main.transform.position = new Vector3(width / 2f - 0.5f, height / 2f - 0.5f, -10);
        Camera.main.orthographicSize = Mathf.Max(width, height) / 2f + 1f;
    }

    private SpriteRenderer CreateSquare(string objName, int x, int y, int order)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(x, y, 0);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = squareSprite;
        sr.sortingOrder = order;
        return sr;
    }

    public void Refresh()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                Color c = TileColor(Game.Tiles[x, y]);
                if (!showFullMapDebug)
                {
                    if (!Game.Explored[x, y])
                        c = Color.black;
                    else if (!Game.IsVisible(x, y))
                        c = new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 1f);   // If explored floor does not in the view, turn it dark. #探索過但不在視野內:變暗
                }
                _tileRenderers[x, y].color = c;
            }

        _playerRenderer.transform.position = new Vector3(Game.Player.x, Game.Player.y, 0);
    }

    private static Color TileColor(TileType t)
    {
        switch (t)
        {
            case TileType.Wall: return new Color(0.2f, 0.2f, 0.25f);
            case TileType.Coin: return Color.yellow;
            case TileType.Exit: return Color.green;
            default: return new Color(0.85f, 0.85f, 0.85f);
        }
    }

    public MoveResult DoMove(Direction dir)
    {
        var result = Game.TryMove(dir);
        Refresh();
        Debug.Log($"[{Game.Status}] {result.Event}: {result.Message} | coins={Game.CoinsCollected}, steps_left={Game.StepsLeft}");
        return result;
    }

    public void DoReset(int newSeed)
    {
        Game.Reset(newSeed);
        Refresh();
    }

    // Step 2:
    public void DebugPrintObservation()
    {
        Debug.Log(ObservationBuilder.ToJson(Game, observationMode, true));
        Debug.Log("\n" + string.Join("\n", ObservationBuilder.Build(Game, observationMode).Map));
    }
}