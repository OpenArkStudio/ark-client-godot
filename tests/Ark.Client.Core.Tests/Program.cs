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
            Test("monotonic snapshot age and correlated application RTT reset", DiagnosticsCorrelation);
            Test("display smoothing preserves server state and fresh identities", PresentationState);
            Test("local role markers and walking follow actual displayed movement", PresentationIdentityAndMotion);
            Test("independent action sequence and exact pending correlation", ActionCorrelation);
            Test("required HP/MP ranges, confirmed deltas and reset", AttributeValidation);
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
    private static Frame Snapshot(string id, uint seq = 1, uint hp = 100, uint mp = 100, uint hpMax = 100, uint mpMax = 100, uint actionSeq = 0) => Json(RoomSession.Snapshot,
        $$"""{"room":"lobby","players":[{"id":"{{id}}","name":"Alice","x":120,"y":100,"seq":{{seq}},"hp":{{hp}},"mp":{{mp}},"hp_max":{{hpMax}},"mp_max":{{mpMax}},"action_seq":{{actionSeq}}}]}""");

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
        Check(Decode(FrameCodec.Encode(60000, new byte[8192])).Body.Length == 8192, "8192-byte boundary failed.");
        byte[] maximum = FrameCodec.Encode(60003, new byte[8192]);
        foreach (int split in new[] { 1, 11, 12, 4108, 8203 })
        {
            var codec = new FrameCodec();
            Check(codec.Append(maximum.AsSpan(0, split)).Count == 0 && codec.Append(maximum.AsSpan(split)).Single().Body.Length == 8192,
                "Bounded maximum-body partial frame failed.");
        }
        byte[] roster = JsonSerializer.SerializeToUtf8Bytes(new
        {
            room = "lobby",
            players = Enumerable.Range(1, 16).Select(index => new
            {
                id = index.ToString(),
                name = new string('"', 64),
                x = 960,
                y = 540,
                seq = uint.MaxValue,
                hp = uint.MaxValue,
                mp = uint.MaxValue,
                hp_max = uint.MaxValue,
                mp_max = uint.MaxValue,
                action_seq = uint.MaxValue
            })
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Check(roster.Length > 4096 && roster.Length <= 8192 && Decode(FrameCodec.Encode(RoomSession.Snapshot, roster)).Body.SequenceEqual(roster),
            "Expanded 16-player stat snapshot did not fit ARK2.");
        Reject(() => FrameCodec.Encode(60000, new byte[8193]));
        byte[] magic = FrameCodec.Encode(60000, Array.Empty<byte>());
        magic[0] = 0;
        Reject(() => new FrameCodec().Append(magic));
        byte[] flags = FrameCodec.Encode(60000, Array.Empty<byte>());
        flags[7] = 1;
        Reject(() => new FrameCodec().Append(flags));
        byte[] large = FrameCodec.Encode(60000, Array.Empty<byte>());
        large[10] = 0x20;
        large[11] = 1;
        Reject(() => new FrameCodec().Append(large));
        byte[] oldVersion = FrameCodec.Encode(60000, Array.Empty<byte>());
        oldVersion[3] = (byte)'1';
        Reject(() => new FrameCodec().Append(oldVersion));
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
        using (JsonDocument move = JsonDocument.Parse(Decode(room.MoveFrame(150, 160)).Body))
        {
            Check(move.RootElement.GetProperty("seq").GetUInt32() == 1, "First sequence must be one.");
        }
        Check(room.LastProposedMove == new MoveProposal(1, 150, 160) && room.Players.Single().X == 120,
            "Move proposal changed server-confirmed state.");

        room.Reset();
        Check(room.PlayerId is null && room.Players.Count == 0 && !room.HasSnapshot, "Reconnect retained old room.");
        Check(room.LastProposedMove is null, "Reconnect retained an old proposal.");
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
        IReadOnlyList<PlayerLabel> panels = PlayerLabels.Arrange(clustered, 16, Measure, extraHeight: 28);
        for (int index = 0; index < panels.Count; index++)
        {
            LabelRectangle bounds = panels[index].Bounds;
            Check(bounds.X >= 0 && bounds.Y >= 0 && bounds.X + bounds.Width <= RoomSession.Width &&
                bounds.Y + bounds.Height <= RoomSession.Height &&
                panels.Take(index).All(previous => !bounds.Intersects(previous.Bounds)), "HP/MP panels overlap or escape the arena.");
        }
    }

    private static void ActionCorrelation()
    {
        var room = new RoomSession();
        room.Apply(Accepted("1"));
        room.Apply(Snapshot("1", 0));
        using (JsonDocument request = JsonDocument.Parse(Decode(room.ActionFrame("damage")).Body))
        {
            Check(request.RootElement.EnumerateObject().Count() == 2 && request.RootElement.GetProperty("seq").GetUInt32() == 1 &&
                request.RootElement.GetProperty("kind").GetString() == "damage", "Client sent amounts/targets or an incorrect action sequence.");
        }
        Check(room.Players.Single().Hp == 100 && room.PendingAction?.Sequence == 1, "Proposal predicted an HP change.");
        try
        {
            room.ActionFrame("skill");
            throw new Exception("A second action replaced the pending request.");
        }
        catch (InvalidOperationException) { }
        room.Apply(Snapshot("1", 1));
        Check(room.PendingAction?.Sequence == 1, "Movement snapshot prematurely confirmed an action.");
        Check(room.Apply(Json(RoomSession.Error, "{\"code\":\"invalid_action\",\"message\":\"unrelated\",\"action_seq\":99}")) == "action_error" &&
            room.PendingAction?.Sequence == 1 && room.HasSnapshot, "Unmatched rejection cleared pending state or disconnected.");
        room.Apply(Json(RoomSession.Error, "{\"code\":\"invalid_action\",\"message\":\"invalid seq\",\"action_seq\":0}"));
        Check(room.PendingAction?.Sequence == 1, "Sentinel rejection confirmed a valid request.");
        room.Apply(Snapshot("1", 1, hp: 85, actionSeq: 1));
        Check(room.PendingAction is null && room.Players.Single().Hp == 85, "Confirmed action did not update server-owned HP.");
        room.ActionFrame("skill");
        using (JsonDocument move = JsonDocument.Parse(Decode(room.MoveFrame(140, 110)).Body))
        {
            Check(move.RootElement.GetProperty("seq").GetUInt32() == 1 && room.PendingAction?.Sequence == 2,
                "Action and move sequences are coupled.");
        }
        room.Apply(Json(RoomSession.Error, "{\"code\":\"insufficient_resource\",\"message\":\"not enough MP\",\"action_seq\":2}"));
        Check(room.PendingAction is null && room.HasSnapshot && room.Players.Single().ActionSequence == 1 && room.Players.Single().Mp == 100,
            "Business rejection changed authoritative state or left the session unusable.");
        room.ActionFrame("rest");
        Check(room.PendingAction?.Sequence == 3, "Rejected requests reused the previous action sequence.");
        room.Apply(Snapshot("1", 1, actionSeq: 3));
        Check(room.PendingAction is null, "Higher accepted action sequence did not resolve the pending request.");
        room.Reset();
        room.Apply(Accepted("new"));
        room.Apply(Snapshot("new", 0));
        Check(room.PendingAction is null && room.LastProposedAction is null && room.ActionFeedback is null,
            "Reconnect retained or retried an action.");
        room.ActionFrame("damage");
        Check(room.PendingAction?.Sequence == 1, "Fresh identity did not reset action sequence.");
    }

    private static void AttributeValidation()
    {
        var room = new RoomSession();
        room.Apply(Accepted("1"));
        room.Apply(Snapshot("1", 0));
        var feedback = new ConfirmedAttributes();
        Check(feedback.Observe(room.Players).Count == 0, "Initial snapshot fabricated HP/MP feedback.");
        room.ActionFrame("damage");
        Check(feedback.Observe(room.Players).Count == 0, "Action proposal generated visual damage.");
        room.Apply(Snapshot("1", 0, hp: 85, mp: 80, actionSeq: 1));
        Check(feedback.Observe(room.Players).Single() == new AttributeChange("1", -15, -20), "Feedback is not based on confirmed deltas.");
        room.ActionFrame("rest");
        room.Apply(Snapshot("1", 0, actionSeq: 2));
        Check(feedback.Observe(room.Players).Single() == new AttributeChange("1", 15, 20), "Capped restore feedback invented an amount.");
        room.ActionFrame("rest");
        room.Apply(Snapshot("1", 0, actionSeq: 3));
        Check(room.PendingAction is null && feedback.Observe(room.Players).Count == 0, "Accepted clamp/no-op generated fake attribute feedback.");
        PlayerState previous = room.Players.Single();
        Reject(() => room.Apply(Snapshot("1", 0, hp: 101, actionSeq: 3)));
        Reject(() => room.Apply(Snapshot("1", 0, hpMax: 0, actionSeq: 3)));
        Reject(() => room.Apply(Snapshot("1", 0, mp: 101, actionSeq: 3)));
        Reject(() => room.Apply(Snapshot("1", 0, mpMax: 0, actionSeq: 3)));
        Reject(() => room.Apply(Snapshot("1", 0, actionSeq: 2)));
        string complete = Encoding.UTF8.GetString(Snapshot("1", 0, actionSeq: 3).Body);
        foreach (string field in new[] { "hp", "mp", "hp_max", "mp_max" })
        {
            Reject(() => room.Apply(Json(RoomSession.Snapshot, complete.Replace($"\"{field}\":100,", "", StringComparison.Ordinal))));
        }
        Reject(() => room.Apply(Json(RoomSession.Snapshot, complete.Replace(",\"action_seq\":3", "", StringComparison.Ordinal))));
        Reject(() => room.Apply(Json(RoomSession.Snapshot, complete.Replace("\"hp\":100", "\"hp\":-1", StringComparison.Ordinal))));
        Reject(() => room.Apply(Json(RoomSession.Snapshot, complete.Replace("\"hp\":100", "\"hp\":4294967296", StringComparison.Ordinal))));
        Reject(() => room.Apply(Json(RoomSession.Snapshot, complete.Replace("\"mp\":100", "\"mp\":1.5", StringComparison.Ordinal))));
        Check(room.Players.Single() == previous, "Invalid stats partially replaced the roster.");
        room.ActionFrame("rest");
        string ownNext = Encoding.UTF8.GetString(Snapshot("1", 0, actionSeq: 4).Body);
        string invalidPeer = "{\"id\":\"2\",\"name\":\"Bob\",\"x\":1,\"y\":1,\"seq\":0,\"hp\":101,\"mp\":100,\"hp_max\":100,\"mp_max\":100,\"action_seq\":0}";
        Reject(() => room.Apply(Json(RoomSession.Snapshot, ownNext.Replace("]}", "," + invalidPeer + "]}", StringComparison.Ordinal))));
        Check(room.PendingAction?.Sequence == 4 && room.Players.Single() == previous,
            "Invalid peer stats prematurely confirmed the own action.");
        room.Apply(Snapshot("1", 0, hp: 0, actionSeq: 4));
        room.MoveFrame(10, 10);
        Check(room.LastProposedMove is not null, "Zero HP disabled demo movement.");
        room.Apply(Snapshot("1", 0, hp: uint.MaxValue, mp: uint.MaxValue, hpMax: uint.MaxValue, mpMax: uint.MaxValue, actionSeq: uint.MaxValue));
        try
        {
            room.ActionFrame("rest");
            throw new Exception("Exhausted action sequence wrapped.");
        }
        catch (InvalidOperationException) { }
        feedback.Reset();
        Check(feedback.Observe(room.Players).Count == 0, "Fresh observation retained old HP/MP deltas.");
        room.Apply(Snapshot("1", 0, hp: 0, actionSeq: uint.MaxValue));
        Check(feedback.Observe(room.Players).Single().HpDelta == -(long)uint.MaxValue, "Large uint delta overflowed.");
    }

    private static void DiagnosticsCorrelation()
    {
        double clock = 100;
        var diagnostics = new SessionDiagnostics(() => clock);
        Check(diagnostics.Capture() == new SessionMetrics(null, null, null, null), "New session has fabricated observations.");
        diagnostics.SnapshotApplied();
        diagnostics.BeginPing(7);
        clock = 125;
        Check(!diagnostics.MatchPong(6), "An unmatched nonce produced an RTT.");
        Check(diagnostics.MatchPong(7), "Matching pong was ignored.");
        SessionMetrics first = diagnostics.Capture();
        Check(first.ApplicationRttMilliseconds == 25 && first.SnapshotAgeMilliseconds == 25 &&
            first.MatchedPongNonce == 7 && first.PendingPingNonce is null, "RTT/age scope is incorrect.");
        clock = 150;
        Check(!diagnostics.MatchPong(7) && diagnostics.Capture().ApplicationRttMilliseconds == 25,
            "Duplicate pong changed the completed sample.");
        diagnostics.BeginPing(8);
        clock = 170;
        Check(!diagnostics.MatchPong(7) && diagnostics.MatchPong(8), "Stale pong matched a new request.");
        Check(diagnostics.Capture().ApplicationRttMilliseconds == 20 && first.ApplicationRttMilliseconds == 25 &&
            first.SnapshotAgeMilliseconds == 25, "Immutable observation changed later.");
        diagnostics.Reset();
        Check(!diagnostics.MatchPong(8) && diagnostics.Capture() == new SessionMetrics(null, null, null, null),
            "Fresh rejoin retained nonce, RTT or snapshot age.");
    }

    private static void PresentationState()
    {
        var presentation = new PlayerPresentation();
        var initial = new PlayerState("old", "Alice", 100, 100, 0);
        presentation.Update(new[] { initial }, 0.016, true);
        var moved = initial with
        {
            X = 200,
            Sequence = 1
        };
        presentation.Update(new[] { moved }, 0.016, true);
        DisplayPlayer display = presentation.Players.Single();
        Check(display.X > 100 && display.X < 200 && moved.X == 200 && display.ServerState == moved,
            "Smoothing predicted or changed authoritative state.");
        presentation.Update(new[] { moved }, 0.016, false);
        Check(presentation.Players.Single().X == 200, "Raw display did not snap to server state.");
        presentation.Update(Array.Empty<PlayerState>(), 0.016, true);
        Check(presentation.Players.Count == 0, "Removed player remained visible.");
        var fresh = moved with
        {
            Id = "new",
            X = 40
        };
        presentation.Update(new[] { fresh }, 0.016, true);
        Check(presentation.Players.Single().X == 40, "Fresh identity reused an old display position.");
        presentation.Reset();
        Check(presentation.Players.Count == 0, "Reconnect retained presentation state.");
        presentation.Update(new[] { initial }, 0.016, true);
        Check(presentation.Players.Single().X == 100, "Reset did not discard prior smoothing state.");
    }

    private static void PresentationIdentityAndMotion()
    {
        Check(PlayerPresentation.RoleFor("1", "1") == PlayerRole.Local &&
            PlayerPresentation.RoleFor("2", "1") == PlayerRole.Peer &&
            PlayerPresentation.RoleFor("1", null) == PlayerRole.Peer, "Player roles depend on color or stale identity.");
        var presentation = new PlayerPresentation();
        var initial = new PlayerState("1", "Alice", 100, 100, 0);
        presentation.Update(new[] { initial }, 0.016, true);
        Check(!presentation.Players.Single().IsWalking, "Idle spawn fabricated walking.");
        presentation.Update(new[] { initial }, 0.016, true);
        Check(presentation.Players.Single().WalkPhase == 0, "Idle display advanced animation.");
        presentation.Update(new[] { initial with { X = 120, Sequence = 1 } }, 0.016, true);
        DisplayPlayer moving = presentation.Players.Single();
        Check(moving.IsWalking && moving.WalkPhase > 0 && moving.FacingX > 0.99f && Math.Abs(moving.FacingY) < 0.01,
            "Facing and walking did not follow displayed displacement.");
        presentation.Reset();
        presentation.Update(new[] { initial with { Id = "new" } }, 0.016, true);
        Check(!presentation.Players.Single().IsWalking && presentation.Players.Single().WalkPhase == 0 &&
            PlayerPresentation.RoleFor("1", "new") == PlayerRole.Peer &&
            PlayerPresentation.RoleFor("new", "new") == PlayerRole.Local, "Fresh identity retained animation or a local marker.");
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
                TransportMetrics empty = first.Metrics;
                Check(empty == TransportMetrics.Empty, "New connection counters are not zero.");
                Task run = first.RunAsync("127.0.0.1", port);
                using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Event(first, "connected");
                byte[] response = RoomSession.PingFrame(41);
                await peer.GetStream().WriteAsync(response.AsMemory(0, 3));
                await peer.GetStream().WriteAsync(response.AsMemory(3));
                Check((await Event(first, "frame")).Frame!.MessageId == RoomSession.Ping, "TCP fragmented receive failed.");
                TransportMetrics received = first.Metrics;
                Check(received.RxFrames == 1 && received.RxBytes == response.Length && received.TxFrames == 0 &&
                    received.PeerEndpoint is not null && empty == TransportMetrics.Empty,
                    "Receive counters/endpoint or immutable metrics are incorrect.");
                await peer.GetStream().WriteAsync(response.AsMemory(0, 5));
                first.Dispose();
                await run.WaitAsync(TimeSpan.FromSeconds(5));
            }
            using (var second = new TcpConnection())
            {
                Check(second.Metrics == TransportMetrics.Empty, "Reconnect reused counters or endpoint.");
                Task run = second.RunAsync("127.0.0.1", port);
                using TcpClient peer = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Event(second, "connected");
                byte[] expected = RoomSession.PingFrame(99);
                await second.SendAsync(expected);
                Check(second.Metrics.TxFrames == 1 && second.Metrics.TxBytes == expected.Length,
                    "Completed local frame write was not counted exactly.");
                var received = new byte[expected.Length];
                await peer.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Check(received.SequenceEqual(expected), "TCP send bytes mismatch.");
                await peer.GetStream().WriteAsync(expected);
                Check((await Event(second, "frame")).Frame!.Body.SequenceEqual(Decode(expected).Body), "Reconnect retained stale frame bytes.");
                Check(second.Metrics.RxFrames == 1 && second.Metrics.RxBytes == expected.Length,
                    "New connection receive counters include old bytes.");
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
                Check(malformed.Metrics.RxBytes == bad.Length && malformed.Metrics.RxFrames == 0,
                    "Malformed bytes were presented as a decoded frame.");
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
