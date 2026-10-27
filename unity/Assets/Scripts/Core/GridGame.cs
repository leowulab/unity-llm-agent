using System;

namespace GridGame.Core
{
    public class GridGame
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public TileType[,] Tiles { get; private set; }
        public Vec2Int Player { get; private set; }
        public int CoinsCollected { get; private set; }
        public int TotalCoins { get; private set; }
        public int StepsLeft { get; private set; }
        public int StepsUsed { get; private set; }
        public GameStatus Status { get; private set; }
        public int Seed { get; private set; }

        //Step 2:
        public int VisionRadius { get; private set; }
        public bool[,] Explored { get; private set; }
        private readonly int _maxSteps;

        public GridGame(int width, int height, int maxSteps, int seed, int visionRadius = 2)
        {
            Width = width;
            Height = height;
            _maxSteps = maxSteps;
            Seed = seed;
            VisionRadius = visionRadius; // Step 2
            Reset(seed);
        }

        public void Reset(int seed)
        {
            Seed = seed;
            var rng = new Random(seed);

            Tiles = new TileType[Width, Height];
            CoinsCollected = 0;
            StepsLeft = _maxSteps;
            StepsUsed = 0;
            Status = GameStatus.Playing;

            // 1. Outer walls, inner floor *四周圍牆,內部地板
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    Tiles[x, y] = (x == 0 || y == 0 || x == Width - 1 || y == Height - 1)
                        ? TileType.Wall : TileType.Floor;

            // 2. Outer walls, inner floor *隨機內牆(約 15%)
            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                    if (rng.NextDouble() < 0.15) Tiles[x, y] = TileType.Wall;

            // 3. Player starts at bottom-left, exit at top-right; ensure both cells are empty *玩家起點固定左下,出口固定右上,並確保這兩格是空的
            Player = new Vec2Int(1, 1);
            Tiles[1, 1] = TileType.Floor;
            var exit = new Vec2Int(Width - 2, Height - 2);
            Tiles[exit.x, exit.y] = TileType.Exit;

            // 4. Randomly place coins (~10% of floor tiles) *隨機放金幣(約 10% 的地板) 
            TotalCoins = 0;
            for (int x = 1; x < Width - 1; x++)
                for (int y = 1; y < Height - 1; y++)
                    if (Tiles[x, y] == TileType.Floor
                        && !(x == Player.x && y == Player.y)
                        && rng.NextDouble() < 0.10)
                    {
                        Tiles[x, y] = TileType.Coin;
                        TotalCoins++;
                    }

            Explored = new bool[Width, Height];
            RevealAround(Player);
            // TODO (Phase 6): Use BFS to ensure a valid path exists from start to exit; regenerate if unreachable *TODO(階段 6):用 BFS 確認起點到出口一定連通,不連通就重新生成
        }

        public bool IsVisible(int x, int y)
        {
            return Math.Abs(x - Player.x) <= VisionRadius
                && Math.Abs(y - Player.y) <= VisionRadius;
        }

        private void RevealAround(Vec2Int center)
        {
            for (int dx = -VisionRadius; dx <= VisionRadius; dx++)
                for (int dy = -VisionRadius; dy <= VisionRadius; dy++)
                {
                    int x = center.x + dx, y = center.y + dy;
                    if (InBounds(x, y)) Explored[x, y] = true;
                }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public MoveResult TryMove(Direction dir)
        {
            if (Status != GameStatus.Playing)
                return new MoveResult
                {
                    Success = false,
                    Event = "game_over",
                    Message = "Game already ended. Call reset_game to start again."
                };

            var next = Player.Move(dir);

            // Hitting walls or out of bounds: Does not consume steps (tweakable parameter to allow consumption) *撞牆或出界:不消耗步數(也可以改成消耗,是個可調旋鈕)
            if (!InBounds(next.x, next.y) || Tiles[next.x, next.y] == TileType.Wall)
                return new MoveResult
                {
                    Success = false,
                    Event = "hit_wall",
                    Message = $"Cannot move {dir}: blocked by a wall."
                };

            Player = next;
            RevealAround(Player);   // Step 2
            StepsLeft--;
            StepsLeft--;
            StepsUsed++;

            var result = new MoveResult { Success = true, Event = "moved", Message = $"Moved {dir}." };

            if (Tiles[next.x, next.y] == TileType.Coin)
            {
                Tiles[next.x, next.y] = TileType.Floor;
                CoinsCollected++;
                result.Event = "coin";
                result.Message = $"Moved {dir} and collected a coin.";
            }
            else if (Tiles[next.x, next.y] == TileType.Exit)
            {
                Status = GameStatus.Won;
                result.Event = "exit";
                result.Message = "Reached the exit. You win!";
            }

            if (Status == GameStatus.Playing && StepsLeft <= 0)
            {
                Status = GameStatus.Lost;
                result.Event = "out_of_steps";
                result.Message = "Out of steps. You lose.";
            }

            return result;
        }


    }
}