using System.Globalization;
using System.Text;
using Ark.Client.Protocol;
using Google.Protobuf;
using Wire = Ark.Client.Protocol.EntitySync;

namespace Ark.Client.Core;

public sealed record InventoryItem(uint Row, ulong Incarnation, uint ItemId, uint Quantity);
public sealed record EquipmentItem(uint Slot, long EntityId, ulong Incarnation, uint ItemId, uint Power);
public sealed record PlayerState(string Id, string Name, double X, double Y, uint Sequence,
    uint Hp = 100, uint Mp = 100, uint HpMax = 100, uint MpMax = 100, uint ActionSequence = 0,
    ulong EntityId = 0)
{
    public IReadOnlyList<InventoryItem> Inventory { get; init; } = Array.Empty<InventoryItem>();
    public IReadOnlyList<EquipmentItem> Equipment { get; init; } = Array.Empty<EquipmentItem>();
}
public sealed record MoveProposal(uint Sequence, double X, double Y);
public sealed record ActionProposal(uint Sequence, string Kind);

/// <summary>Owner-thread replica. Each room envelope validates against a private candidate before commit.</summary>
public sealed class RoomSession
{
    public const ushort Join = 60000, Accepted = 60001, Move = 60002,
        Snapshot = 60003, Error = 60004, Ping = 60005, Pong = 60006, Action = 60008, Delta = 60009;
    public const int Width = 960, Height = 540;
    public const string SchemaId = "ark.guest-player", ClassName = "GuestPlayer";
    public static NestedStreamLimits EntityLimits { get; } = new(new NestedSnapshotLimits(1, 3, 8, 64, 512), 128, 64, 512, 64);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private sealed record Replica(PlayerState State, NestedReplica Receiver, IReadOnlyList<long> EquipmentIds, bool Removed = false);
    private Dictionary<ulong, Replica> _replicas = new();
    private uint _sequence, _actionSequence;
    public string? PlayerId
    {
        get; private set;
    }
    public ulong EntityId
    {
        get; private set;
    }
    public string? Name
    {
        get; private set;
    }
    public IReadOnlyList<PlayerState> Players { get; private set; } = Array.Empty<PlayerState>();
    public string? LastError
    {
        get; private set;
    }
    public bool HasSnapshot
    {
        get; private set;
    }
    public uint LastPong
    {
        get; private set;
    }
    public MoveProposal? LastProposedMove
    {
        get; private set;
    }
    public ActionProposal? PendingAction
    {
        get; private set;
    }
    public ActionProposal? LastProposedAction
    {
        get; private set;
    }
    public string? ActionFeedback
    {
        get; private set;
    }
    public ulong BaselineCount
    {
        get; private set;
    }
    public ulong DeltaCount
    {
        get; private set;
    }

    public static byte[] MessageFrame(ushort id, IMessage body) => FrameCodec.Encode(id, body.ToByteArray());
    public static byte[] JoinFrame(string name)
    {
        ValidateName(name);
        return MessageFrame(Join, new join_request { Name = name, Room = "lobby", ProtocolVersion = 2 });
    }
    public byte[] MoveFrame(double x, double y)
    {
        if (!HasSnapshot)
        {
            throw new InvalidOperationException("Join before moving.");
        }
        ValidatePosition(x, y);
        if (_sequence == uint.MaxValue)
        {
            throw new InvalidOperationException("Movement sequence exhausted; reconnect.");
        }
        LastProposedMove = new MoveProposal(++_sequence, x, y);
        return MessageFrame(Move, new move_request { Sequence = _sequence, X = x, Y = y });
    }
    public static byte[] PingFrame(uint nonce) => MessageFrame(Ping, new heartbeat { Nonce = nonce });
    public byte[] ActionFrame(string kind)
    {
        if (!HasSnapshot)
        {
            throw new InvalidOperationException("Join before requesting an action.");
        }
        if (PendingAction is not null)
        {
            throw new InvalidOperationException("Wait for the pending action result.");
        }
        action_kind action = kind switch
        {
            "damage" => action_kind.ActionDamage,
            "skill" => action_kind.ActionSkill,
            "rest" => action_kind.ActionRest,
            "pickup" => action_kind.ActionPickup,
            "use_potion" => action_kind.ActionUsePotion,
            "equip" => action_kind.ActionEquip,
            "upgrade" => action_kind.ActionUpgrade,
            _ => throw new ArgumentException("Unsupported action kind.")
        };
        if (_actionSequence == uint.MaxValue)
        {
            throw new InvalidOperationException("Action sequence exhausted; reconnect.");
        }
        var proposal = new ActionProposal(++_actionSequence, kind);
        byte[] frame = MessageFrame(Action, new action_request { Sequence = proposal.Sequence, Kind = action });
        PendingAction = LastProposedAction = proposal;
        ActionFeedback = "Awaiting server confirmation…";
        return frame;
    }
    public void Reset()
    {
        _replicas = new();
        _sequence = _actionSequence = 0;
        PlayerId = Name = LastError = ActionFeedback = null;
        EntityId = BaselineCount = DeltaCount = 0;
        Players = Array.Empty<PlayerState>();
        HasSnapshot = false;
        LastPong = 0;
        LastProposedMove = null;
        PendingAction = LastProposedAction = null;
    }
    public string Apply(Frame frame)
    {
        try
        {
            switch (frame.MessageId)
            {
                case Accepted:
                    var accepted = ProtobufMessages.Parse(frame.Body, join_accepted.Parser, join_accepted.Descriptor);
                    if (PlayerId is not null || accepted.Room != "lobby" || accepted.ProtocolVersion != 2 ||
                        accepted.Width != Width || accepted.Height != Height || accepted.EntityId == 0 || accepted.EntityId > long.MaxValue)
                    {
                        throw new InvalidDataException("Invalid join acceptance.");
                    }
                    ValidateId(accepted.PlayerId);
                    ValidateName(accepted.Name);
                    PlayerId = accepted.PlayerId;
                    EntityId = accepted.EntityId;
                    Name = accepted.Name;
                    return "accepted";
                case Snapshot:
                    var baseline = ProtobufMessages.Parse(frame.Body, room_baseline.Parser, room_baseline.Descriptor);
                    RequireRoom(baseline.Room);
                    if (HasSnapshot)
                    {
                        throw new InvalidDataException("Unexpected replacement baseline; reconnect first.");
                    }
                    var initial = new Dictionary<ulong, Replica>();
                    foreach (player_baseline player in baseline.Players)
                    {
                        Add(initial, player);
                    }
                    Commit(initial);
                    BaselineCount++;
                    return "snapshot";
                case Delta:
                    var delta = ProtobufMessages.Parse(frame.Body, room_delta.Parser, room_delta.Descriptor);
                    RequireRoom(delta.Room);
                    if (!HasSnapshot)
                    {
                        throw new InvalidDataException("Delta arrived before baseline.");
                    }
                    var candidate = new Dictionary<ulong, Replica>(_replicas);
                    var envelopeIds = new HashSet<ulong>();
                    foreach (player_baseline player in delta.Added)
                    {
                        if (player.Binding is null || !envelopeIds.Add(player.Binding.EntityId))
                        {
                            throw new InvalidDataException("Duplicate room entity.");
                        }
                        Add(candidate, player);
                    }
                    foreach (Wire.nested_delta change in delta.Changed)
                    {
                        if (change.Root is null || change.Root.EntityId <= 0 || !envelopeIds.Add((ulong)change.Root.EntityId))
                        {
                            throw new InvalidDataException("Duplicate room entity.");
                        }
                        Change(candidate, change);
                    }
                    Commit(candidate);
                    DeltaCount++;
                    return "snapshot";
                case Error:
                    var error = ProtobufMessages.Parse(frame.Body, error_response.Parser, error_response.Descriptor);
                    if (error.Code.Length == 0 || StrictUtf8.GetByteCount(error.Code) > 64 || StrictUtf8.GetByteCount(error.Message) > 256)
                    {
                        throw new InvalidDataException("Invalid server error.");
                    }
                    LastError = error.Code + ": " + error.Message;
                    if (error.HasActionSequence)
                    {
                        if (PendingAction?.Sequence == error.ActionSequence)
                        {
                            PendingAction = null;
                        }
                        ActionFeedback = LastError;
                        return "action_error";
                    }
                    return "error";
                case Pong:
                    LastPong = ProtobufMessages.Parse(frame.Body, heartbeat.Parser, heartbeat.Descriptor).Nonce;
                    return "pong";
                default:
                    throw new InvalidDataException("Unsupported server message ID.");
            }
        }
        catch (Exception error) when (error is ArgumentException or DecoderFallbackException or OverflowException)
        {
            throw new InvalidDataException("Invalid room message: " + error.Message, error);
        }
    }
    private void RequireRoom(string room)
    {
        if (PlayerId is null || room != "lobby")
        {
            throw new InvalidDataException("Room message arrived before acceptance or names an unsupported room.");
        }
    }
    /// <summary>The sample's fixed field/type projection. Remote values cannot expand it.</summary>
    public static NestedStreamBinding PlayerBinding(long entityId, ulong incarnation, ulong generation, IReadOnlyList<long> equipmentIds)
    {
        if (entityId <= 0 || incarnation == 0 || generation == 0 || equipmentIds.Count != 2 ||
            equipmentIds.Any(id => id <= 0 || id == entityId) || equipmentIds[0] == equipmentIds[1])
        {
            throw new InvalidDataException("Invalid player instance binding.");
        }
        var equipment = new NestedEntityLayout("GuestEquipment", new Dictionary<uint, NestedScalarType> { [1] = NestedScalarType.UInt32, [2] = NestedScalarType.UInt32 });
        var fields = new Dictionary<uint, NestedScalarType> { [100] = NestedScalarType.Bytes, [101] = NestedScalarType.Double, [102] = NestedScalarType.Double };
        for (uint index = 103; index <= 108; index++)
        {
            fields.Add(index, NestedScalarType.UInt32);
        }
        return new NestedStreamBinding(SchemaId, entityId, incarnation, generation, new NestedEntityLayout(ClassName, fields,
            new Dictionary<uint, NestedTableLayout> { [200] = new("InventoryItem", new Dictionary<uint, NestedScalarType> { [1] = NestedScalarType.UInt32, [2] = NestedScalarType.UInt32 }) },
            new Dictionary<uint, IReadOnlyDictionary<long, NestedEntityLayout>> { [300] = new Dictionary<long, NestedEntityLayout> { [equipmentIds[0]] = equipment, [equipmentIds[1]] = equipment } }));
    }
    private static void Add(Dictionary<ulong, Replica> candidate, player_baseline player)
    {
        ValidateId(player.PlayerId);
        player_binding instance = player.Binding ?? throw new InvalidDataException("Missing player binding.");
        if (instance.EntityId == 0 || instance.EntityId > long.MaxValue || candidate.ContainsKey(instance.EntityId) ||
            candidate.Values.Any(entry => !entry.Removed && entry.State.Id == player.PlayerId))
        {
            throw new InvalidDataException("Duplicate or resurrected identity.");
        }
        long[] equipmentIds = instance.EquipmentIds.ToArray();
        NestedStreamBinding binding = PlayerBinding((long)instance.EntityId, instance.Incarnation, instance.Generation, equipmentIds);
        var receiver = new NestedReplica(binding, EntityLimits);
        if (player.State is null || receiver.Apply(NestedProtobuf.Decode(player.State, EntityLimits)) != NestedApplyResult.Applied)
        {
            throw new InvalidDataException("Invalid player baseline.");
        }
        PlayerState state = Project(player.PlayerId, receiver.State!, equipmentIds);
        candidate.Add(instance.EntityId, new Replica(state, receiver, Array.AsReadOnly(equipmentIds)));
    }
    private static void Change(Dictionary<ulong, Replica> candidate, Wire.nested_delta input)
    {
        NestedDelta delta = NestedProtobuf.Decode(input, EntityLimits);
        if (delta.Root.EntityId <= 0 || !candidate.TryGetValue((ulong)delta.Root.EntityId, out Replica? previous))
        {
            throw new InvalidDataException("Delta names an unknown player root.");
        }
        NestedReplica receiver = previous.Receiver.Fork();
        NestedApplyResult result = receiver.Apply(delta);
        if (result == NestedApplyResult.Rejected)
        {
            throw new InvalidDataException("Invalid nested player delta.");
        }
        if (result == NestedApplyResult.Duplicate)
        {
            return;
        }
        PlayerState state = delta.Removed ? previous.State : Project(previous.State.Id, receiver.State!, previous.EquipmentIds);
        if (state.Sequence < previous.State.Sequence || state.ActionSequence < previous.State.ActionSequence)
        {
            throw new InvalidDataException("Authoritative sequence moved backwards.");
        }
        candidate[(ulong)delta.Root.EntityId] = new Replica(state, receiver, previous.EquipmentIds, delta.Removed);
    }
    private static PlayerState Project(string playerId, NestedSnapshot snapshot, IReadOnlyList<long> equipmentIds)
    {
        NestedEntitySnapshot root = snapshot.Root;
        var fields = root.Fields;
        var state = new PlayerState(playerId, StrictUtf8.GetString(fields[100].AsBytes()), fields[101].AsDouble(), fields[102].AsDouble(), fields[103].AsUInt32(),
            fields[104].AsUInt32(), fields[105].AsUInt32(), fields[106].AsUInt32(), fields[107].AsUInt32(), fields[108].AsUInt32(), (ulong)root.EntityId);
        ValidateName(state.Name);
        ValidatePosition(state.X, state.Y);
        if (state.HpMax == 0 || state.MpMax == 0 || state.Hp > state.HpMax || state.Mp > state.MpMax)
        {
            throw new InvalidDataException("Invalid authoritative HP/MP range.");
        }
        InventoryItem[] inventory = root.Tables[200].Rows.Select(pair => new InventoryItem(pair.Key, pair.Value.Incarnation,
            pair.Value.Fields[1].AsUInt32(), pair.Value.Fields[2].AsUInt32())).ToArray();
        EquipmentItem[] equipment = root.Containers[300].Select(pair => new EquipmentItem(pair.Key, pair.Value.EntityId, pair.Value.Incarnation,
            pair.Value.Fields[1].AsUInt32(), pair.Value.Fields[2].AsUInt32())).ToArray();
        if (inventory.Length > 1 || inventory.Any(item => item.Row != 1 || item.ItemId != 1 || item.Quantity is < 1 or > 99) ||
            equipment.Length != 1 || equipment.Any(item => item.Slot != 1 || item.ItemId is not (1001 or 1002) || item.Power > 100 ||
                item.EntityId != equipmentIds[(int)item.ItemId - 1001]))
        {
            throw new InvalidDataException("Invalid authoritative inventory/equipment projection.");
        }
        return state with
        {
            Inventory = Array.AsReadOnly(inventory),
            Equipment = Array.AsReadOnly(equipment)
        };
    }
    private void Commit(Dictionary<ulong, Replica> candidate)
    {
        PlayerState[] players = candidate.Values.Where(entry => !entry.Removed).Select(entry => entry.State).OrderBy(player => player.EntityId).ToArray();
        if (players.Length is < 1 or > 16 || candidate.Count > 4096 ||
            !players.Any(player => player.Id == PlayerId && player.EntityId == EntityId && player.Name == Name))
        {
            throw new InvalidDataException("Invalid roster or own identity binding.");
        }
        var identities = new HashSet<long>();
        foreach (Replica entry in candidate.Values)
        {
            if (!identities.Add((long)entry.State.EntityId) || entry.EquipmentIds.Any(id => !identities.Add(id)))
            {
                throw new InvalidDataException("Overlapping player Entity bindings.");
            }
        }
        // No externally visible state changes before all envelope validations finish.
        _replicas = candidate;
        Players = Array.AsReadOnly(players);
        HasSnapshot = true;
        PlayerState own = players.Single(player => player.EntityId == EntityId);
        _sequence = Math.Max(_sequence, own.Sequence);
        _actionSequence = Math.Max(_actionSequence, own.ActionSequence);
        if (PendingAction is ActionProposal pending && own.ActionSequence >= pending.Sequence)
        {
            ActionFeedback = pending.Kind + " confirmed by server";
            PendingAction = null;
        }
    }
    private static void ValidateId(string value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) || number == 0 ||
            value != number.ToString(CultureInfo.InvariantCulture))
        {
            throw new InvalidDataException("Invalid logical player ID.");
        }
    }
    public static void ValidateName(string value)
    {
        if (value.Length == 0 || StrictUtf8.GetByteCount(value) > 64 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Use a nonempty name of at most 64 UTF-8 bytes without control characters.");
        }
    }
    public static void ValidatePosition(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > Width || y < 0 || y > Height)
        {
            throw new InvalidDataException("Position is outside room bounds.");
        }
    }
}
