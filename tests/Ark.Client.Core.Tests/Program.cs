using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ark.Client.Core;

internal static class Program
{
    private static int _passed;
    public static async Task<int> Main()
    {
        try
        {
            Test("shared golden vectors", GoldenVectors);
            Test("every partial split and multiple frames", FragmentedFrames);
            Test("maximum body and malformed headers", HeaderBounds);
            Test("reconnect clears partial frame", ResetCodec);
            Test("authoritative state and fresh identity", SessionState);
            Test("invalid room messages do not replace roster", InvalidState);
            Test("bounded player label truncation and collision layout", LabelLayout);
            await NetworkSessions();
            Console.WriteLine($"PASS {_passed} checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Test(string name, Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    private static void Check(bool value, string reason)
    {
        if (!value)
        {
            throw new Exception(reason);
        }
    }
    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception("Expected malformed input rejection.");
    }
    private static Frame Decode(byte[] bytes) => new FrameCodec().Append(bytes).Single();
    private static Frame Json(ushort id, string body) => new(id, Encoding.UTF8.GetBytes(body));
    private static Frame Accepted(string id) => Json(RoomSession.Accepted,
        $$"""{"player_id":"{{id}}","name":"Alice","room":"lobby","width":960,"height":540}""");
    private static Frame Snapshot(string id, uint seq = 1) => Json(RoomSession.Snapshot,
        $$"""{"room":"lobby","players":[{"id":"{{id}}","name":"Alice","x":120,"y":100,"seq":{{seq}}}]}""");

    private static void GoldenVectors()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wire-vectors.json")));
        foreach (JsonElement vector in fixture.RootElement.GetProperty("vectors").EnumerateArray())
        {
            ushort id = vector.GetProperty("message_id").GetUInt16();
            byte[] body = Encoding.UTF8.GetBytes(vector.GetProperty("body").GetString()!);
            byte[] expected = Convert.FromHexString(vector.GetProperty("frame_hex").GetString()!);
            Check(FrameCodec.Encode(id, body).SequenceEqual(expected), "Golden encoding mismatch.");
            Frame decoded = Decode(expected);
            Check(decoded.MessageId == id && decoded.Body.SequenceEqual(body), "Golden decoding mismatch.");
        }
    }
    private static void FragmentedFrames()
    {
        byte[] first = RoomSession.JoinFrame("Alice"), second = RoomSession.PingFrame(2);
        byte[] combined = first.Concat(second).ToArray();
        for (int split = 0; split <= combined.Length; split++)
        {
            var codec = new FrameCodec();
            Frame[] frames = codec.Append(combined.AsSpan(0, split)).Concat(codec.Append(combined.AsSpan(split))).ToArray();
            Check(frames.Length == 2 && frames[0].MessageId == RoomSession.Join && frames[1].MessageId == RoomSession.Ping,
                "Lost a frame at split " + split);
        }
        var bytesCodec = new FrameCodec();
        int count = combined.Sum(value => bytesCodec.Append(new[] { value }).Count);
        Check(count == 2, "Byte-at-a-time framing failed.");
        Check(new FrameCodec().Append(Enumerable.Range(0, 100).SelectMany(_ => second).ToArray()).Count == 100,
            "Coalesced frames exceeding internal buffer failed.");
    }
    private static void HeaderBounds()
    {
        Check(Decode(FrameCodec.Encode(60000, new byte[4096])).Body.Length == 4096, "4096-byte boundary failed.");
        Reject(() => FrameCodec.Encode(60000, new byte[4097]));
        byte[] magic = FrameCodec.Encode(60000, Array.Empty<byte>());
        magic[0] = 0;
        Reject(() => new FrameCodec().Append(magic));
        byte[] flags = FrameCodec.Encode(60000, Array.Empty<byte>());
        flags[7] = 1;
        Reject(() => new FrameCodec().Append(flags));
        byte[] large = FrameCodec.Encode(60000, Array.Empty<byte>());
        large[10] = 0x10;
        large[11] = 1;
        Reject(() => new FrameCodec().Append(large));
        large[8] = 0xff;
        large[9] = 0xff;
        Reject(() => new FrameCodec().Append(large));
    }
    private static void ResetCodec()
    {
        var codec = new FrameCodec();
        byte[] frame = RoomSession.JoinFrame("Alice");
        Check(codec.Append(frame.AsSpan(0, 15)).Count == 0, "Incomplete frame emitted.");
        codec.Reset();
        Check(codec.Append(RoomSession.PingFrame(9)).Single().MessageId == RoomSession.Ping, "Stale reconnect buffer retained.");
    }
    private static void SessionState()
    {
        var room = new RoomSession();
        room.Apply(Accepted("old"));
        room.Apply(Snapshot("old"));
        Check(room.HasSnapshot && room.Players.Single().Sequence == 1, "Authoritative snapshot missing.");
        using (JsonDocument move = JsonDocument.Parse(Decode(room.MoveFrame(120, 100)).Body))
        {
            Check(move.RootElement.GetProperty("seq").GetUInt32() == 1, "First sequence must be one.");
        }

        room.Reset();
        Check(room.PlayerId is null && room.Players.Count == 0 && !room.HasSnapshot, "Reconnect retained old room.");
        room.Apply(Accepted("new"));
        room.Apply(Snapshot("new", 0));
        using JsonDocument fresh = JsonDocument.Parse(Decode(room.MoveFrame(130, 110)).Body);
        Check(room.PlayerId == "new" && fresh.RootElement.GetProperty("seq").GetUInt32() == 1,
            "Fresh identity did not reset sequence.");
    }
    private static void InvalidState()
    {
        var room = new RoomSession();
        room.Apply(Accepted("1"));
        room.Apply(Snapshot("1", 4));
        Reject(() => room.Apply(Snapshot("1", 3)));
        Reject(() => room.Apply(Json(RoomSession.Snapshot,
            "{\"room\":\"lobby\",\"players\":[{\"id\":\"1\",\"name\":\"Alice\",\"x\":961,\"y\":1,\"seq\":5}]}")));
        Reject(() => room.Apply(new Frame(RoomSession.Snapshot, new byte[] { 0xff })));
        Reject(() => room.Apply(Json(RoomSession.Snapshot, "[]")));
        Reject(() => room.Apply(Json(RoomSession.Snapshot, "{\"room\":\"lobby\",\"players\":[]}")));
        Reject(() => room.MoveFrame(float.NaN, 10));
        Check(room.Players.Single().Sequence == 4, "Rejected snapshot mutated roster.");
    }

    private static void LabelLayout()
    {
        static float Measure(string text) => System.Globalization.StringInfo.ParseCombiningCharacters(text).Length * 8;
        string combining = PlayerLabels.FitText("A\u0301B\u0301C\u0301D\u0301", 24, Measure);
        Check(combining == "A\u0301B\u0301…", "Truncation split a Unicode text element.");
        Check(PlayerLabels.FitText("Alice", 160, Measure) == "Alice", "Short names changed.");
        var nearby = new[] {
            new PlayerState("9", "VisualAlice", 80, 300, 0),
            new PlayerState("10", "VisualBob", 180, 300, 0)
        };
        IReadOnlyList<PlayerLabel> pair = PlayerLabels.Arrange(nearby, 20, Measure);
        Check(!pair[0].Bounds.Intersects(pair[1].Bounds), "Nearby labels overlap.");
        var clustered = Enumerable.Range(1, 16)
            .Select(id => new PlayerState(id.ToString(), new string('W', 64), 960, 540, 0)).ToArray();
        IReadOnlyList<PlayerLabel> labels = PlayerLabels.Arrange(clustered, 20, Measure);
        Check(labels.Count == 16, "Clustered label layout lost players.");
        for (int index = 0; index < labels.Count; index++)
        {
            PlayerLabel label = labels[index];
            LabelRectangle bounds = label.Bounds;
            Check(label.Text.EndsWith("…", StringComparison.Ordinal) && Measure(label.Text) <= PlayerLabels.MaximumWidth,
                "Long label exceeded its measured width.");
            Check(bounds.X >= 0 && bounds.Y >= 0 && bounds.X + bounds.Width <= RoomSession.Width &&
                bounds.Y + bounds.Height <= RoomSession.Height, "Label escaped the arena.");
            Check(labels.Take(index).All(previous => !bounds.Intersects(previous.Bounds)), "Clustered labels overlap.");
        }
        Check(clustered.All(player => player.Name.Length == 64), "Display truncation changed the full name.");
    }

    private static async Task<TransportEvent> Event(TcpConnection connection, string kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            while (connection.TryDequeue(out TransportEvent? value))
            {
                if (value!.Kind == kind)
                {
                    return value;
                }

                if (value.Kind == "closed")
                {
                    throw new Exception(value.Message);
                }
            }
            await Task.Delay(5, timeout.Token);
        }
        throw new TimeoutException();
    }
    private static async Task NetworkSessions()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            using (var first = new TcpConnection())
            {
                Task run = first.RunAsync("127.0.0.1", port);
                using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Event(first, "connected");
                byte[] response = RoomSession.PingFrame(41);
                await peer.GetStream().WriteAsync(response.AsMemory(0, 3));
                await peer.GetStream().WriteAsync(response.AsMemory(3));
                Check((await Event(first, "frame")).Frame!.MessageId == RoomSession.Ping, "TCP fragmented receive failed.");
                await peer.GetStream().WriteAsync(response.AsMemory(0, 5));
                first.Dispose();
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            using (var second = new TcpConnection())
            {
                Task run = second.RunAsync("127.0.0.1", port);
                using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Event(second, "connected");
                byte[] expected = RoomSession.PingFrame(99);
                await second.SendAsync(expected);
                var received = new byte[expected.Length];
                await peer.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Check(received.SequenceEqual(expected), "TCP send bytes mismatch.");
                await peer.GetStream().WriteAsync(expected);
                Check((await Event(second, "frame")).Frame!.Body.SequenceEqual(Decode(expected).Body), "Reconnect retained stale frame bytes.");
                peer.Dispose();
                await Event(second, "closed");
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            using (var malformed = new TcpConnection())
            {
                Task run = malformed.RunAsync("127.0.0.1", port);
                using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Event(malformed, "connected");
                byte[] bad = FrameCodec.Encode(RoomSession.Pong, Array.Empty<byte>());
                bad[7] = 1;
                await peer.GetStream().WriteAsync(bad);
                TransportEvent closed = await Event(malformed, "closed");
                Check(closed.Message!.Contains("flags", StringComparison.Ordinal), "Malformed frame failure was not visible.");
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            _passed++;
            Console.WriteLine("PASS real loopback transport, malformed frame and reconnect");
        }
        finally
        {
            listener.Stop();
        }
        using var refused = new TcpConnection();
        await refused.RunAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromSeconds(5));
        Check((await Event(refused, "closed")).Message is not null, "Connection failure was silent.");
        _passed++;
        Console.WriteLine("PASS visible connection failure");
    }
}
