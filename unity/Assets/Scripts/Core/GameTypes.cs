namespace GridGame.Core
{
    public enum TileType { Floor, Wall, Coin, Exit }

    public enum Direction { Up, Down, Left, Right }

    public enum GameStatus { Playing, Won, Lost }

    public struct Vec2Int
    {
        public int x, y;
        public Vec2Int(int x, int y) { this.x = x; this.y = y; }

        public Vec2Int Move(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return new Vec2Int(x, y + 1);
                case Direction.Down: return new Vec2Int(x, y - 1);
                case Direction.Left: return new Vec2Int(x - 1, y);
                default: return new Vec2Int(x + 1, y);
            }
        }
    }

    public class MoveResult
    {
        public bool Success;          // Has it actually moved?
        public string Event;          // "moved" / "hit_wall" / "coin" / "exit" / "out_of_steps" / "game_over"
        public string Message;        //  Description for the Agent
    }
}