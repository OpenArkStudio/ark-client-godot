using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ark.Client.Core;
using Ark.Client.Protocol;
using Ark.Client.Testing;
using Google.Protobuf;

internal static class Program
{
    private static int _passed;
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.FirstOrDefault() == "--live")
            {
                return await LiveIntegration.Run(args);
            }
            if (args.FirstOrDefault() == "--protocol-parity")
            {
                ProtocolParity.Verify(args[1], args.ElementAtOrDefault(2));
                return 0;
            }
            Test("schema generated source and shared fixture SHA parity", () => ProtocolParity.Verify(ProtocolParity.FindRoot(), null));
            Test("shared C++/C# protobuf golden frames", GoldenVectors);
            Test("every partial split and multiple frames", FragmentedFrames);
            Test("maximum body and malformed headers", HeaderBounds);
            Test("reconnect clears partial frame", ResetCodec);
            Test("all scalar alternatives retain widths and zero presence", ScalarBoundaries);
            Test("baseline delta add remove duplicate and fresh identity", ReplicaLifecycle);
            Test("whole room rejects atomically including own pending action", AtomicEnvelope);
            Test("schema types ranges gaps generations and resurrection fail", InvalidReplica);
            Test("official protobuf parser rejects truncation duplicate fields and wrong wires", InvalidEncoding);
            Test("independent actions and exact pending correlation", ActionCorrelation);
            Test("confirmed HP/MP feedback and unsigned limits", AttributeValidation);
            Test("bounded player label truncation and collision layout", LabelLayout);
            Test("monotonic snapshot age and correlated application RTT reset", DiagnosticsCorrelation);
            Test("display smoothing preserves server state and fresh identities", PresentationState);
            Test("local role markers and walking follow actual displayed movement", PresentationIdentityAndMotion);
            Test("smoke evidence requires every own and peer action snapshot", AttributeSmokeEvidence);
            Test("busy send defers proposal and heartbeat state before preparation", DeferredSendPreparation);
            await NetworkSessions();
            Console.WriteLine($"PASS {_passed} checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static void Test(string name, System.Action action)
    {
        action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }
    internal static void Check(bool value, string reason)
    {
        if (!value)
        {
            throw new Exception(reason);
        }
    }
    private static void Reject(System.Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected malformed input rejection.");
    }
    internal static Frame Decode(byte[] bytes) => new FrameCodec().Append(bytes).Single();
    internal static Frame Message(ushort id, IMessage body) => new(id, body.ToByteArray());
    internal static scalar_field UInt(uint index, uint value) => new() { Index = index, Uint32Value = value };
    internal static scalar_field Double(uint index, double value) => new() { Index = index, DoubleValue = value };
    internal static player_baseline Player(string id = "1", ulong entity = 42, string name = "Alice", ulong revision = 1)
    {
        var state = new scalar_baseline { SchemaId = RoomSession.SchemaId, ClassName = RoomSession.ClassName, EntityId = entity, Generation = 1, Revision = revision };
        state.Fields.Add(new scalar_field { Index = 100, StringValue = ByteString.CopyFromUtf8(name) });
        state.Fields.Add(Double(101, 120));
        state.Fields.Add(Double(102, 100));
        state.Fields.Add(UInt(103, 0));
        state.Fields.Add(UInt(104, 100));
        state.Fields.Add(UInt(105, 100));
        state.Fields.Add(UInt(106, 100));
        state.Fields.Add(UInt(107, 100));
        state.Fields.Add(UInt(108, 0));
        return new player_baseline { PlayerId = id, State = state };
    }
    private static Frame Accepted(string id = "1", ulong entity = 42) => Message(RoomSession.Accepted,
        new join_accepted { PlayerId = id, EntityId = entity, Name = "Alice", Room = "lobby", Width = 960, Height = 540, ProtocolVersion = 1 });
    private static Frame Snapshot(string id, uint seq = 0)
    {
        player_baseline player = Player(id);
        player.State.Fields[3].Uint32Value = seq;
        return Message(RoomSession.Snapshot, new room_baseline { Room = "lobby", Players = { player } });
    }
    internal static scalar_delta Change(ulong entity = 42, ulong required = 1, params scalar_field[] fields)
    {
        var change = new scalar_delta { SchemaId = RoomSession.SchemaId, ClassName = RoomSession.ClassName, EntityId = entity, Generation = 1, RequiredRevision = required, Revision = required + 1 };
        change.Fields.Add(fields);
        return change;
    }
    internal static Frame Delta(params scalar_delta[] changes) => Message(RoomSession.Delta, new room_delta { Room = "lobby", Changed = { changes } });
    private static RoomSession Joined(params player_baseline[] peers)
    {
        var room = new RoomSession();
        room.Apply(Accepted());
        var baseline = new room_baseline { Room = "lobby", Players = { Player() } };
        baseline.Players.Add(peers);
        room.Apply(Message(RoomSession.Snapshot, baseline));
        return room;
    }
    private static void GoldenVectors()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wire-vectors.json")));
        Check(fixture.RootElement.GetProperty("format").GetString() == "ark.guest-room.protobuf-v1", "Unexpected shared format.");
        foreach (JsonElement vector in fixture.RootElement.GetProperty("vectors").EnumerateArray())
        {
            ushort id = vector.GetProperty("message_id").GetUInt16();
            byte[] body = Convert.FromHexString(vector.GetProperty("body_hex").GetString()!);
            byte[] expected = Convert.FromHexString(vector.GetProperty("frame_hex").GetString()!);
            Check(FrameCodec.Encode(id, body).SequenceEqual(expected), "Golden frame encoding mismatch.");
            Frame decoded = Decode(expected);
            Check(decoded.MessageId == id && decoded.Body.SequenceEqual(body), "Golden frame decoding mismatch.");
            IMessage parsed = id switch
            {
                RoomSession.Join => ProtobufMessages.Parse(body, join_request.Parser, join_request.Descriptor),
                RoomSession.Accepted => ProtobufMessages.Parse(body, join_accepted.Parser, join_accepted.Descriptor),
                RoomSession.Move => ProtobufMessages.Parse(body, move_request.Parser, move_request.Descriptor),
                RoomSession.Snapshot => ProtobufMessages.Parse(body, room_baseline.Parser, room_baseline.Descriptor),
                RoomSession.Error => ProtobufMessages.Parse(body, error_response.Parser, error_response.Descriptor),
                RoomSession.Ping or RoomSession.Pong => ProtobufMessages.Parse(body, heartbeat.Parser, heartbeat.Descriptor),
                RoomSession.Action => ProtobufMessages.Parse(body, action_request.Parser, action_request.Descriptor),
                RoomSession.Delta => ProtobufMessages.Parse(body, room_delta.Parser, room_delta.Descriptor),
                _ => throw new Exception("Unknown golden message.")
            };
            Check(parsed.ToByteArray().SequenceEqual(body), "Official generated roundtrip mismatch: " + vector.GetProperty("name"));
        }
        foreach (JsonElement vector in fixture.RootElement.GetProperty("scalar_vectors").EnumerateArray())
        {
            byte[] body = Convert.FromHexString(vector.GetProperty("body_hex").GetString()!);
            scalar_field field = ProtobufMessages.Parse(body, scalar_field.Parser, scalar_field.Descriptor);
            Check(field.Index == 100 && field.ValueCase != scalar_field.ValueOneofCase.None && field.ToByteArray().SequenceEqual(body),
                "Shared scalar boundary mismatch: " + vector.GetProperty("name"));
        }
        Check(Decode(RoomSession.JoinFrame("Alice")).Body.SequenceEqual(new join_request { Name = "Alice", Room = "lobby", ProtocolVersion = 1 }.ToByteArray()), "Join version missing.");
    }
    private static void HeaderBounds()
    {
        byte[] maximum = FrameCodec.Encode(RoomSession.Snapshot, new byte[8192]);
        foreach (int split in new[] { 1, 11, 12, 4108, 8203 })
        {
            var codec = new FrameCodec();
            Check(codec.Append(maximum.AsSpan(0, split)).Count == 0 && codec.Append(maximum.AsSpan(split)).Single().Body.Length == 8192, "Maximum-body partial frame failed.");
        }
        var roster = new room_baseline { Room = "lobby" };
        for (ulong id = 1; id <= 16; id++)
        {
            player_baseline player = Player(id.ToString(), id, new string('"', 64));
            foreach (scalar_field field in player.State.Fields.Where(field => field.Index >= 103))
            {
                field.Uint32Value = uint.MaxValue;
            }
            roster.Players.Add(player);
        }
        byte[] body = roster.ToByteArray();
        Check(body.Length <= 8192 && Decode(FrameCodec.Encode(RoomSession.Snapshot, body)).Body.SequenceEqual(body), "Maximum roster exceeded frame bound.");
        Reject(() => FrameCodec.Encode(60000, new byte[8193]));
        foreach (int offset in new[] { 0, 3, 7 })
        {
            byte[] bad = FrameCodec.Encode(60000, Array.Empty<byte>());
            bad[offset] ^= 1;
            Reject(() => new FrameCodec().Append(bad));
        }
        byte[] large = FrameCodec.Encode(60000, Array.Empty<byte>());
        large[10] = 0x20;
        large[11] = 1;
        Reject(() => new FrameCodec().Append(large));
    }
    private static void ScalarBoundaries()
    {
        var fields = new[] {
            new scalar_field { BoolValue = false }, new scalar_field { BoolValue = true },
            new scalar_field { Int32Value = int.MinValue }, new scalar_field { Int32Value = int.MaxValue },
            new scalar_field { Uint32Value = uint.MaxValue }, new scalar_field { Int64Value = long.MinValue },
            new scalar_field { Int64Value = long.MaxValue }, new scalar_field { Uint64Value = ulong.MaxValue },
            new scalar_field { FloatValue = float.MaxValue }, new scalar_field { DoubleValue = double.MaxValue },
            new scalar_field { StringValue = ByteString.CopyFrom(new byte[] { 0, 255, 128 }) }, new scalar_field { GuidValue = long.MaxValue }, new scalar_field { GuidValue = long.MinValue }
        };
        foreach (scalar_field field in fields)
        {
            scalar_field parsed = ProtobufMessages.Parse(field.ToByteArray(), scalar_field.Parser, scalar_field.Descriptor);
            Check(parsed.Equals(field) && parsed.ValueCase != scalar_field.ValueOneofCase.None, "Lost exact scalar width/presence.");
        }
        RoomSession room = Joined();
        room.Apply(Delta(Change(42, 1, Double(101, Math.BitIncrement(120.0)), UInt(103, uint.MaxValue), UInt(104, uint.MaxValue), UInt(105, uint.MaxValue), UInt(106, uint.MaxValue), UInt(107, uint.MaxValue), UInt(108, uint.MaxValue))));
        Check(room.Players.Single().X == Math.BitIncrement(120.0) && room.Players.Single().Hp == uint.MaxValue, "Public fields lost double/uint32 precision.");
        try
        {
            room.MoveFrame(1, 1);
            throw new Exception("Movement sequence wrapped.");
        }
        catch (InvalidOperationException) { }
        try
        {
            room.ActionFrame("rest");
            throw new Exception("Action sequence wrapped.");
        }
        catch (InvalidOperationException) { }
    }
    private static void ReplicaLifecycle()
    {
        var room = Joined();
        byte[] move = room.MoveFrame(150, 160);
        Check(move_request.Parser.ParseFrom(Decode(move).Body).Sequence == 1 && room.Players.Single().X == 120, "Proposal changed authority.");
        var addition = new room_delta { Room = "lobby", Added = { Player("2", 43, "Bob") } };
        room.Apply(Message(RoomSession.Delta, addition));
        scalar_delta change = Change(42, 1, Double(101, 150), Double(102, 160), UInt(103, 1));
        room.Apply(Delta(change));
        room.Apply(Delta(change));
        Check(room.Players.Count == 2 && room.Players[0].X == 150 && room.BaselineCount == 1 && room.DeltaCount == 3, "Incremental/duplicate update failed.");
        scalar_delta removed = Change(43);
        removed.Removed = true;
        room.Apply(Delta(removed));
        room.Apply(Delta(removed));
        Check(room.Players.Count == 1, "Tombstone did not remove peer idempotently.");
        Reject(() => room.Apply(Message(RoomSession.Delta, addition)));
        room.Reset();
        Check(!room.HasSnapshot && room.EntityId == 0 && room.Players.Count == 0 && room.LastProposedMove is null, "Reset retained replica.");
        room.Apply(Accepted("3", 99));
        room.Apply(Message(RoomSession.Snapshot, new room_baseline { Room = "lobby", Players = { Player("3", 99) } }));
        Check(move_request.Parser.ParseFrom(Decode(room.MoveFrame(1, 1)).Body).Sequence == 1 && room.EntityId == 99, "Fresh baseline retained old identity/sequence.");
    }
    private static void AtomicEnvelope()
    {
        var room = Joined(Player("2", 43, "Bob"));
        room.ActionFrame("damage");
        IReadOnlyList<PlayerState> previous = room.Players;
        scalar_delta own = Change(42, 1, UInt(104, 85), UInt(108, 1));
        scalar_delta badPeer = Change(43, 1, UInt(104, 101));
        Reject(() => room.Apply(Delta(own, badPeer)));
        Check(ReferenceEquals(room.Players, previous) && room.PendingAction?.Sequence == 1 && room.DeltaCount == 0, "Invalid peer partially committed own action.");
        scalar_delta zeroRequired = Change(43, 0, UInt(104, 90));
        Reject(() => room.Apply(Delta(own, zeroRequired)));
        Check(ReferenceEquals(room.Players, previous) && room.PendingAction?.Sequence == 1 && room.DeltaCount == 0,
            "Zero required revision partially committed an own action.");
        var zeroBaseline = new room_delta { Room = "lobby", Added = { Player("3", 44, "Third", 0) }, Changed = { own } };
        Reject(() => room.Apply(Message(RoomSession.Delta, zeroBaseline)));
        Check(ReferenceEquals(room.Players, previous) && room.PendingAction?.Sequence == 1 && room.DeltaCount == 0,
            "Zero baseline revision partially committed room state.");
        room.Apply(Delta(own));
        Check(room.PendingAction is null && room.Players[0].Hp == 85, "Atomic failure consumed the revision.");
        var wrongOwn = new RoomSession();
        wrongOwn.Apply(Accepted());
        Reject(() => wrongOwn.Apply(Message(RoomSession.Snapshot, new room_baseline { Room = "lobby", Players = { Player("1", 99) } })));
        Check(!wrongOwn.HasSnapshot && wrongOwn.Players.Count == 0, "Wrong entity bound own identity.");
        wrongOwn.Apply(Snapshot("1"));
        Reject(() => wrongOwn.Apply(Delta(Change(42, 1, new scalar_field { Index = 100, StringValue = ByteString.CopyFromUtf8("Other") }))));
    }
    private static void InvalidReplica()
    {
        foreach (System.Action<scalar_baseline> corrupt in new System.Action<scalar_baseline>[] {
            state => state.SchemaId = "unknown", state => state.ClassName = "guestplayer", state => state.Generation = 0,
            state => state.EntityId = 0, state => state.EntityId = ulong.MaxValue, state => state.Revision = 0,
            state => state.Fields.RemoveAt(8), state => state.Fields.Add(state.Fields[0].Clone()),
            state => state.Fields[0].StringValue = ByteString.CopyFrom(new byte[] { 255 }),
            state => state.Fields[0].StringValue = ByteString.CopyFromUtf8(new string('a', 65)),
            state => state.Fields[0].StringValue = ByteString.CopyFromUtf8("A\n"),
            state => state.Fields[1].DoubleValue = double.NaN, state => state.Fields[1].DoubleValue = double.PositiveInfinity,
            state => state.Fields[1].DoubleValue = -1, state => state.Fields[2].DoubleValue = 541,
            state => state.Fields[4].Uint32Value = 101, state => state.Fields[6].Uint32Value = 0,
            state => state.Fields[5].Uint32Value = 101, state => state.Fields[7].Uint32Value = 0,
            state => state.Fields[0].Index = 99, state => state.Fields[1].FloatValue = 120,
            state => state.Fields[4].Int32Value = 100, state => state.Fields[0].ClearValue()
        })
        {
            var room = new RoomSession();
            room.Apply(Accepted());
            player_baseline player = Player();
            corrupt(player.State);
            Reject(() => room.Apply(Message(RoomSession.Snapshot, new room_baseline { Room = "lobby", Players = { player } })));
            Check(!room.HasSnapshot, "Malformed baseline committed.");
        }
        foreach (System.Action<scalar_delta> corrupt in new System.Action<scalar_delta>[] {
            change => change.Generation = 0, change => change.Generation = 2, change => change.RequiredRevision = 0,
            change => { change.RequiredRevision = 0; change.Revision = 1; },
            change => change.RequiredRevision = 2, change => change.Revision = 3, change => change.EntityId = 99,
            change => change.SchemaId = "unknown", change => change.ClassName = "other", change => change.Fields.Clear(),
            change => change.Removed = true, change => change.Fields.Add(change.Fields[0].Clone())
        })
        {
            var room = Joined();
            scalar_delta change = Change(42, 1, UInt(104, 85));
            corrupt(change);
            Reject(() => room.Apply(Delta(change)));
            Check(room.Players.Single().Hp == 100, "Invalid delta committed.");
        }
        var joined = Joined();
        joined.Apply(Delta(Change(42, 1, UInt(103, 5))));
        Reject(() => joined.Apply(Delta(Change(42, 2, UInt(103, 4)))));
        scalar_delta ownRemoval = Change(42, 2);
        ownRemoval.Removed = true;
        Reject(() => joined.Apply(Delta(ownRemoval)));
        Reject(() => joined.Apply(Delta(Change(42, 2, UInt(104, 90)), Change(42, 2, UInt(105, 90)))));
        Reject(() => joined.Apply(Snapshot("1")));
        var duplicateId = new RoomSession();
        duplicateId.Apply(Accepted());
        Reject(() => duplicateId.Apply(Message(RoomSession.Snapshot, new room_baseline { Room = "lobby", Players = { Player(), Player("1", 43) } })));
        Reject(() => joined.Apply(new Frame(60007, Array.Empty<byte>())));
    }
    private static void InvalidEncoding()
    {
        byte[] accepted = Accepted().Body;
        for (int count = 1; count < accepted.Length; count++)
        {
            var room = new RoomSession();
            byte[] truncated = accepted[..count];
            Reject(() => room.Apply(new Frame(RoomSession.Accepted, truncated)));
        }
        var fresh = new RoomSession();
        Reject(() => fresh.Apply(new Frame(RoomSession.Accepted, accepted.Concat(new byte[] { 0x38, 1 }).ToArray())));
        Reject(() => fresh.Apply(new Frame(RoomSession.Accepted, new byte[] { 0x0a, 0x80 })));
        Reject(() => fresh.Apply(new Frame(RoomSession.Pong, new byte[] { 0x0a, 0 })));
        Reject(() => ProtobufMessages.Parse(new byte[] { 0x20, 0, 0x41, 0, 0, 0, 0, 0, 0, 0, 0 }, scalar_field.Parser, scalar_field.Descriptor));
        Reject(() => fresh.Apply(new Frame(RoomSession.Accepted, new byte[8193])));
        Reject(() => ProtobufMessages.Parse(new byte[] { 0x08, 1, 0x10, 99 }, action_request.Parser, action_request.Descriptor));
        Reject(() => ProtobufMessages.Parse(new byte[] { 0x20, 0x80, 0x80, 0x80, 0x80, 0x10 }, scalar_field.Parser, scalar_field.Descriptor));
        Reject(() => fresh.Apply(new Frame(RoomSession.Accepted, new byte[] { 0x0a, 1, 0xff })));
        Reject(() => ProtobufMessages.Parse(new byte[] { 0x10, 2 }, scalar_field.Parser, scalar_field.Descriptor));
        Reject(() => ProtobufMessages.Parse(new byte[] { 0x30 }.Concat(Enumerable.Repeat((byte)0xff, 9)).Append((byte)3).ToArray(), scalar_field.Parser, scalar_field.Descriptor));
        // Unknown protobuf fields remain forward compatible.
        fresh.Apply(new Frame(RoomSession.Accepted, accepted.Concat(new byte[] { 0xa0, 0x06, 1 }).ToArray()));
        fresh.Apply(Snapshot("1"));
        Check(fresh.HasSnapshot, "Unknown field blocked forward-compatible encoding.");
        var badVersion = join_accepted.Parser.ParseFrom(accepted);
        badVersion.ProtocolVersion = 2;
        Reject(() => new RoomSession().Apply(Message(RoomSession.Accepted, badVersion)));
    }
    private static void ActionCorrelation()
    {
        var room = Joined();
        action_request request = action_request.Parser.ParseFrom(Decode(room.ActionFrame("damage")).Body);
        Check(request.Sequence == 1 && request.Kind == action_kind.ActionDamage && room.Players.Single().Hp == 100, "Incorrect action proposal.");
        try
        {
            room.ActionFrame("skill");
            throw new Exception("Pending action replaced.");
        }
        catch (InvalidOperationException) { }
        room.Apply(Delta(Change(42, 1, UInt(103, 1))));
        Check(room.PendingAction?.Sequence == 1, "Move confirmed action.");
        room.Apply(Message(RoomSession.Error, new error_response { Code = "invalid_action", Message = "unrelated", ActionSequence = 99 }));
        Check(room.PendingAction?.Sequence == 1, "Unmatched error cleared pending.");
        room.Apply(Delta(Change(42, 2, UInt(104, 85), UInt(108, 1))));
        Check(room.PendingAction is null && room.Players.Single().Hp == 85, "Action confirmation failed.");
        room.ActionFrame("skill");
        room.Apply(Message(RoomSession.Error, new error_response { Code = "insufficient_resource", ActionSequence = 2 }));
        Check(room.PendingAction is null && room.HasSnapshot && room.Players.Single().Mp == 100, "Business rejection corrupted room.");
        room.ActionFrame("rest");
        Check(room.PendingAction?.Sequence == 3, "Rejection reused sequence.");
        room.Reset();
        Check(room.PendingAction is null && room.LastProposedAction is null && room.ActionFeedback is null, "Reset retried action.");
    }
    private static void AttributeValidation()
    {
        var room = Joined();
        var feedback = new ConfirmedAttributes();
        Check(feedback.Observe(room.Players).Count == 0, "Initial feedback fabricated.");
        room.ActionFrame("damage");
        Check(feedback.Observe(room.Players).Count == 0, "Proposal generated damage feedback.");
        room.Apply(Delta(Change(42, 1, UInt(104, 85), UInt(105, 80), UInt(108, 1))));
        Check(feedback.Observe(room.Players).Single() == new AttributeChange("1", -15, -20), "Confirmed deltas missing.");
        room.Apply(Delta(Change(42, 2, UInt(104, 100), UInt(105, 100), UInt(108, 2))));
        Check(feedback.Observe(room.Players).Single() == new AttributeChange("1", 15, 20), "Restore feedback incorrect.");
        room.Apply(Delta(Change(42, 3, UInt(108, 3))));
        Check(feedback.Observe(room.Players).Count == 0, "No-op fabricated amount.");
        room.Apply(Delta(Change(42, 4, UInt(104, uint.MaxValue), UInt(106, uint.MaxValue))));
        feedback.Observe(room.Players);
        room.Apply(Delta(Change(42, 5, UInt(104, 0))));
        Check(feedback.Observe(room.Players).Single().HpDelta == -(long)uint.MaxValue, "uint32 feedback overflowed.");
        room.MoveFrame(10, 10);
        feedback.Reset();
        Check(feedback.Observe(room.Players).Count == 0, "Reset retained feedback.");
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
    private static void ResetCodec()
    {
        var codec = new FrameCodec();
        byte[] frame = RoomSession.JoinFrame("Alice");
        Check(codec.Append(frame.AsSpan(0, 15)).Count == 0, "Incomplete frame emitted.");
        codec.Reset();
        Check(codec.Append(RoomSession.PingFrame(9)).Single().MessageId == RoomSession.Ping, "Stale reconnect buffer retained.");
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

    private static void AttributeSmokeEvidence()
    {
        var evidence = new AttributeSmokeProgress();
        var own = new PlayerState("own", "Alice", 120, 100, 1);
        var peer = new PlayerState("peer", "Bob", 240, 180, 1);
        evidence.Observe(new[] { own, peer });
        Check(evidence.SawFresh("own") && evidence.SawFresh("peer") && !evidence.Completed("own", "peer"),
            "Fresh defaults were counted as accepted actions.");
        evidence.Observe(new[] { own with { Hp = 85, ActionSequence = 1 }, peer });
        Check(!evidence.SawBoth("own", "peer", 0), "Own-only HP confirmation fabricated peer evidence.");
        evidence.Observe(new[] { own with { Hp = 85, ActionSequence = 1 }, peer with { Hp = 85, ActionSequence = 1 } });
        evidence.Observe(new[] { own with { Hp = 85, Mp = 80, ActionSequence = 2 }, peer with { Hp = 85, ActionSequence = 1 } });
        evidence.Observe(new[] { own with { ActionSequence = 3 }, peer with { Hp = 85, Mp = 80, ActionSequence = 2 } });
        Check(!evidence.Completed("own", "peer"), "Missing peer rest state was accepted.");
        evidence.Observe(new[] { own with { ActionSequence = 3 }, peer with { ActionSequence = 3 } });
        evidence.Observe(new[] { own with { Id = "new-own" }, peer with { Id = "new-peer" } });
        Check(evidence.Completed("own", "peer") && evidence.SawBoth("own", "peer", 0) &&
            evidence.SawFresh("new-own") && evidence.SawFresh("new-peer") && !evidence.Completed("new-own", "new-peer"),
            "Batched snapshots/rejoin lost evidence or reused old action state.");
    }

    private static void DeferredSendPreparation()
    {
        var room = new RoomSession();
        room.Apply(Accepted("1"));
        room.Apply(Snapshot("1", 0));
        var diagnostics = new SessionDiagnostics(() => 100);
        uint nonce = 0;
        Check(!SendPreparation.TryCreate(true, true, () => room.ActionFrame("damage"), out byte[]? blockedAction) &&
            blockedAction is null && room.PendingAction is null && room.LastProposedAction is null,
            "Busy action click consumed a sequence or became pending without a send.");
        Check(!SendPreparation.TryCreate(true, true, () => room.MoveFrame(150, 160), out _) && room.LastProposedMove is null,
            "Busy movement consumed a proposal.");
        bool preparedPing = SendPreparation.TryCreate(true, true, () =>
        {
            diagnostics.BeginPing(++nonce);
            return RoomSession.PingFrame(nonce);
        }, out _);
        Check(!preparedPing && nonce == 0 && diagnostics.Capture().PendingPingNonce is null,
            "Deferred heartbeat started an unsent RTT/deadline state.");
        Check(!SendPreparation.TryCreate(false, false, () => room.ActionFrame("rest"), out _) && room.PendingAction is null,
            "Disconnected connection prepared an action.");
        Check(SendPreparation.TryCreate(true, false, () => room.ActionFrame("damage"), out byte[]? action) &&
            action is not null && room.PendingAction?.Sequence == 1,
            "Deferred action did not retain the first usable sequence.");
        Check(SendPreparation.TryCreate(true, false, () => room.MoveFrame(150, 160), out _) && room.LastProposedMove?.Sequence == 1,
            "Deferred movement sequence was skipped.");
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
                Check(!second.IsSending, "Completed write left the per-connection send guard busy.");
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
