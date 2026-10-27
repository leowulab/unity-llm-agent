using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace GridGame.Core
{
    public enum ObservationMode { VisibleOnly, Explored }

    public class Observation
    {
        [JsonProperty("status")] public string Status;
        [JsonProperty("player")] public Vec2Int Player;
        [JsonProperty("coins_collected")] public int CoinsCollected;
        [JsonProperty("steps_left")] public int StepsLeft;
        [JsonProperty("steps_used")] public int StepsUsed;
        [JsonProperty("grid_width")] public int GridWidth;
        [JsonProperty("grid_height")] public int GridHeight;
        [JsonProperty("exit_position")] public Vec2Int? ExitPosition;   // If does not find = null
        [JsonProperty("map")] public string[] Map;
    }

    public static class ObservationBuilder
    {
        public static Observation Build(GridGame g, ObservationMode mode)
        {
            Vec2Int? exit = null;
            for (int x = 0; x < g.Width; x++)
                for (int y = 0; y < g.Height; y++)
                    if (IsKnown(g, x, y, mode) && g.Tiles[x, y] == TileType.Exit)
                        exit = new Vec2Int(x, y);

            return new Observation
            {
                Status = g.Status.ToString().ToLowerInvariant(),
                Player = g.Player,
                CoinsCollected = g.CoinsCollected,
                StepsLeft = g.StepsLeft,
                StepsUsed = g.StepsUsed,
                GridWidth = g.Width,
                GridHeight = g.Height,
                ExitPosition = exit,
                Map = BuildMap(g, mode)
            };
        }

        public static string ToJson(GridGame g, ObservationMode mode, bool indented = false)
        {
            return JsonConvert.SerializeObject(
                Build(g, mode),
                indented ? Formatting.Indented : Formatting.None);
        }

        private static bool IsKnown(GridGame g, int x, int y, ObservationMode mode)
        {
            return mode == ObservationMode.Explored ? g.Explored[x, y] : g.IsVisible(x, y);
        }

        private static char Symbol(TileType t)
        {
            switch (t)
            {
                case TileType.Wall: return '#';
                case TileType.Coin: return 'C';
                case TileType.Exit: return 'E';
                default: return '.';
            }
        }

        // First column represents X-axis, then from top to bottom, y ranges from Height-1 to 0 (y is postive when it moves upwards.)
        // 第一列是 x 軸標示,之後由上到下是 y = Height-1 ... 0(y 向上為正)
        private static string[] BuildMap(GridGame g, ObservationMode mode)
        {
            var rows = new List<string>();

            var header = new StringBuilder("   | ");
            for (int x = 0; x < g.Width; x++)
            {
                header.Append(x % 10);
                if (x < g.Width - 1) header.Append(' ');
            }
            rows.Add(header.ToString());

            for (int y = g.Height - 1; y >= 0; y--)
            {
                var sb = new StringBuilder();
                sb.Append(y.ToString().PadLeft(2)).Append(" | ");
                for (int x = 0; x < g.Width; x++)
                {
                    char c;
                    if (x == g.Player.x && y == g.Player.y) c = '@';
                    else if (!IsKnown(g, x, y, mode)) c = '?';
                    else c = Symbol(g.Tiles[x, y]);

                    sb.Append(c);
                    if (x < g.Width - 1) sb.Append(' ');
                }
                rows.Add(sb.ToString());
            }
            return rows.ToArray();
        }
    }
}