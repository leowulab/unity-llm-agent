using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using GridGame.Core;

public class GameServer : MonoBehaviour
{
    [SerializeField] private GridView view;
    [SerializeField] private int port = 8765;

    private class PendingCommand
    {
        public string Json;
        public readonly TaskCompletionSource<string> Tcs =
            new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly ConcurrentQueue<PendingCommand> _queue = new ConcurrentQueue<PendingCommand>();
    private TcpListener _listener;
    private CancellationTokenSource _cts;

    private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    // ───────────── 生命週期(主執行緒)─────────────

    private void Start()
    {
        Application.runInBackground = true;
        _cts = new CancellationTokenSource();

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameServer] Failed to start on port {port}: {e.Message}");
            return;
        }

        Debug.Log($"[GameServer] Listening on ws://127.0.0.1:{port}/");
        var ct = _cts.Token;
        Task.Run(() => AcceptLoop(ct));
    }

    private void Update()
    {
        int budget = 16;
        while (budget-- > 0 && _queue.TryDequeue(out var cmd))
            cmd.Tcs.SetResult(Execute(cmd.Json));
    }

    private void OnDestroy()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }

        while (_queue.TryDequeue(out var cmd))
            cmd.Tcs.TrySetResult(Error(null, "server_stopped", "Game server is shutting down."));
    }

    // ───────────── 網路(背景執行緒,不可碰 Unity API,Debug.Log 除外)─────────────

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                    Debug.LogWarning($"[GameServer] Accept loop ended: {e.Message}");
                break;
            }
            _ = HandleClient(client, ct);
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        // 停止 Play 時關閉連線,避免卡在 ReadAsync
        using (ct.Register(() => { try { client.Close(); } catch { } }))
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();

                if (!await Handshake(stream, ct)) { client.Close(); return; }
                Debug.Log("[GameServer] Client connected.");

                while (!ct.IsCancellationRequested)
                {
                    var frame = await ReadFrame(stream, ct);
                    if (frame == null) break;

                    byte opcode = frame.Item1;
                    byte[] payload = frame.Item2;

                    if (opcode == 0x8)        // close
                    {
                        await WriteFrame(stream, 0x8, payload, ct);
                        break;
                    }
                    if (opcode == 0x9)        // ping -> pong
                    {
                        await WriteFrame(stream, 0xA, payload, ct);
                        continue;
                    }
                    if (opcode != 0x1) continue;   // 只處理文字訊息

                    var cmd = new PendingCommand { Json = Encoding.UTF8.GetString(payload) };
                    _queue.Enqueue(cmd);
                    string reply = await cmd.Tcs.Task;   // 等主執行緒處理完
                    await WriteFrame(stream, 0x1, Encoding.UTF8.GetBytes(reply), ct);
                }
            }
            catch (Exception e)
            {
                if (!ct.IsCancellationRequested)
                    Debug.LogWarning($"[GameServer] Client error: {e}");
            }
            finally
            {
                try { client.Close(); } catch { }
                Debug.Log("[GameServer] Client disconnected.");
            }
        }
    }

    // ── WebSocket 握手 ──
    private static async Task<bool> Handshake(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[4096];
        var sb = new StringBuilder();
        while (!sb.ToString().Contains("\r\n\r\n"))
        {
            int n = await stream.ReadAsync(buf, 0, buf.Length, ct);
            if (n <= 0) return false;
            sb.Append(Encoding.ASCII.GetString(buf, 0, n));
            if (sb.Length > 16384) return false;
        }

        var m = Regex.Match(sb.ToString(), @"Sec-WebSocket-Key:\s*(.+?)\r\n", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            var bad = Encoding.ASCII.GetBytes(
                "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(bad, 0, bad.Length, ct);
            return false;
        }

        string key = m.Groups[1].Value.Trim();
        string accept;
        using (var sha1 = SHA1.Create())
            accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WsGuid)));

        var resp = Encoding.ASCII.GetBytes(
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
        await stream.WriteAsync(resp, 0, resp.Length, ct);
        return true;
    }

    // ── WebSocket 封包 ──
    private static async Task<bool> ReadExact(NetworkStream s, byte[] buf, int count, CancellationToken ct)
    {
        int off = 0;
        while (off < count)
        {
            int n = await s.ReadAsync(buf, off, count - off, ct);
            if (n <= 0) return false;
            off += n;
        }
        return true;
    }

    private static async Task<Tuple<byte, byte[]>> ReadFrame(NetworkStream s, CancellationToken ct)
    {
        var h = new byte[2];
        if (!await ReadExact(s, h, 2, ct)) return null;

        byte opcode = (byte)(h[0] & 0x0F);
        bool masked = (h[1] & 0x80) != 0;
        long len = h[1] & 0x7F;

        if (len == 126)
        {
            var e = new byte[2];
            if (!await ReadExact(s, e, 2, ct)) return null;
            len = (e[0] << 8) | e[1];
        }
        else if (len == 127)
        {
            var e = new byte[8];
            if (!await ReadExact(s, e, 8, ct)) return null;
            len = 0;
            for (int i = 0; i < 8; i++) len = (len << 8) | e[i];
        }
        if (len > 1000000) return null;   // 單一訊息上限 1 MB

        var mask = new byte[4];
        if (masked && !await ReadExact(s, mask, 4, ct)) return null;

        var payload = new byte[len];
        if (len > 0 && !await ReadExact(s, payload, (int)len, ct)) return null;
        if (masked)
            for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

        return Tuple.Create(opcode, payload);
    }

    private static async Task WriteFrame(NetworkStream s, byte opcode, byte[] payload, CancellationToken ct)
    {
        using (var ms = new MemoryStream())
        {
            ms.WriteByte((byte)(0x80 | opcode));   // FIN + opcode,伺服器送出的封包不加 mask
            int len = payload.Length;
            if (len < 126) ms.WriteByte((byte)len);
            else if (len <= 65535)
            {
                ms.WriteByte(126);
                ms.WriteByte((byte)(len >> 8));
                ms.WriteByte((byte)(len & 0xFF));
            }
            else
            {
                ms.WriteByte(127);
                for (int i = 7; i >= 0; i--)
                    ms.WriteByte((byte)(((long)len >> (8 * i)) & 0xFF));
            }
            ms.Write(payload, 0, len);

            var bytes = ms.ToArray();
            await s.WriteAsync(bytes, 0, bytes.Length, ct);
        }
    }

    // ───────────── 指令處理(只在主執行緒呼叫,與先前相同)─────────────

    private string Execute(string json)
    {
        JObject req;
        try { req = JObject.Parse(json); }
        catch (Exception)
        {
            return Error(null, "invalid_json", "Request must be a valid JSON object.");
        }

        JToken id = req["id"];
        try
        {
            string cmd = (string)req["cmd"];
            switch (cmd)
            {
                case "get_state": return Reply(id, true, new JObject { ["observation"] = Obs() });
                case "move": return HandleMove(id, req);
                case "reset": return HandleReset(id, req);
                default:
                    return Error(id, "unknown_cmd", $"Unknown cmd '{cmd}'. Valid: get_state, move, reset.");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return Error(id, "server_error", e.Message);
        }
    }

    private string HandleMove(JToken id, JObject req)
    {
        Direction dir;
        switch (((string)req["direction"])?.ToLowerInvariant())
        {
            case "up": dir = Direction.Up; break;
            case "down": dir = Direction.Down; break;
            case "left": dir = Direction.Left; break;
            case "right": dir = Direction.Right; break;
            default:
                return Error(id, "invalid_direction", "direction must be one of: up, down, left, right.");
        }

        MoveResult r = view.DoMove(dir);
        return Reply(id, true, new JObject
        {
            ["success"] = r.Success,
            ["event"] = r.Event,
            ["message"] = r.Message,
            ["observation"] = Obs()
        });
    }

    private string HandleReset(JToken id, JObject req)
    {
        int seed = (req["seed"] != null && req["seed"].Type == JTokenType.Integer)
            ? (int)req["seed"]
            : UnityEngine.Random.Range(0, 100000);

        string modeStr = (string)req["mode"];
        if (modeStr != null)
        {
            switch (modeStr.ToLowerInvariant())
            {
                case "visible_only": view.Mode = ObservationMode.VisibleOnly; break;
                case "explored": view.Mode = ObservationMode.Explored; break;
                default:
                    return Error(id, "invalid_mode", "mode must be 'visible_only' or 'explored'.");
            }
        }

        view.DoReset(seed);
        return Reply(id, true, new JObject
        {
            ["seed"] = seed,
            ["mode"] = view.Mode == ObservationMode.VisibleOnly ? "visible_only" : "explored",
            ["observation"] = Obs()
        });
    }

    private JObject Obs() =>
        JObject.FromObject(ObservationBuilder.Build(view.Game, view.Mode));

    private static string Reply(JToken id, bool ok, JObject body)
    {
        var o = new JObject
        {
            ["id"] = id != null ? id.DeepClone() : JValue.CreateNull(),
            ["ok"] = ok
        };
        o.Merge(body);
        return o.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static string Error(JToken id, string code, string message) =>
        Reply(id, false, new JObject { ["error"] = code, ["message"] = message });
}