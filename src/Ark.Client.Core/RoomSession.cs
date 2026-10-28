using System.Globalization;
using System.Text;
using Ark.Client.Protocol;
using Google.Protobuf;

namespace Ark.Client.Core;

public sealed record PlayerState(string Id, string Name, double X, double Y, uint Sequence,
    uint Hp = 100, uint Mp = 100, uint HpMax = 100, uint MpMax = 100, uint ActionSequence = 0,
    ulong EntityId = 0);
public sealed record MoveProposal(uint Sequence, double X, double Y);
public sealed record ActionProposal(uint Sequence, string Kind);

/// <summary>Owner-thread replica. Each room envelope validates against a private candidate before commit.</summary>
public sealed class RoomSession
{
    public const ushort Join = 60000, Accepted = 60001, Move = 60002,
        Snapshot = 60003, Error = 60004, Ping = 60005, Pong = 60006, Action = 60008, Delta = 60009;
    public const int Width = 960, Height = 540;
    public const string SchemaId = "ark.guest-player.v1", ClassName = "GuestPlayer";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private sealed record Replica(PlayerState State, ulong Generation, ulong Revision, bool Removed = false);
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
        return MessageFrame(Join, new join_request { Name = name, Room = "lobby", ProtocolVersion = 1 });
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
                    if (PlayerId is not null || accepted.Room != "lobby" || accepted.ProtocolVersion != 1 ||
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
                        if (player.State is null || !envelopeIds.Add(player.State.EntityId))
                        {
                            throw new InvalidDataException("Duplicate room entity.");
                        }
                        Add(candidate, player);
                    }
                    foreach (scalar_delta change in delta.Changed)
                    {
                        if (!envelopeIds.Add(change.EntityId))
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
    private static void ValidateMetadata(string schema, string className, ulong entity, ulong generation)
    {
        if (schema != SchemaId || className != ClassName || entity == 0 || entity > long.MaxValue || generation == 0)
        {
            throw new InvalidDataException("Unsupported scalar identity/schema/generation.");
        }
    }
    private static void Add(Dictionary<ulong, Replica> candidate, player_baseline player)
    {
        ValidateId(player.PlayerId);
        scalar_baseline baseline = player.State ?? throw new InvalidDataException("Missing player state.");
        ValidateMetadata(baseline.SchemaId, baseline.ClassName, baseline.EntityId, baseline.Generation);
        if (baseline.Revision == 0)
        {
            throw new InvalidDataException("Baseline revision must be positive.");
        }
        if (candidate.ContainsKey(baseline.EntityId) || candidate.Values.Any(entry => !entry.Removed && entry.State.Id == player.PlayerId))
        {
            throw new InvalidDataException("Duplicate or resurrected identity.");
        }
        PlayerState state = ReadFields(new PlayerState(player.PlayerId, "", 0, 0, 0, EntityId: baseline.EntityId), baseline.Fields, true);
        candidate.Add(baseline.EntityId, new Replica(state, baseline.Generation, baseline.Revision));
    }
    private static void Change(Dictionary<ulong, Replica> candidate, scalar_delta delta)
    {
        ValidateMetadata(delta.SchemaId, delta.ClassName, delta.EntityId, delta.Generation);
        if (!candidate.TryGetValue(delta.EntityId, out Replica? previous))
        {
            throw new InvalidDataException("Delta names an unknown entity.");
        }
        if (previous.Generation != delta.Generation ||
            delta.RequiredRevision == 0 || delta.RequiredRevision == ulong.MaxValue || delta.Revision != delta.RequiredRevision + 1 ||
            (delta.Removed && delta.Fields.Count != 0) || (!delta.Removed && delta.Fields.Count == 0))
        {
            throw new InvalidDataException("Scalar revision gap, generation mismatch or invalid tombstone.");
        }
        PlayerState state = delta.Removed ? previous.State : ReadFields(previous.State, delta.Fields, false);
        // Valid stale/duplicate publications never regress state or resurrect a tombstone.
        if (delta.Revision <= previous.Revision)
        {
            return;
        }
        if (previous.Removed || previous.Revision != delta.RequiredRevision)
        {
            throw new InvalidDataException("Scalar revision gap or resurrection.");
        }
        if (state.Sequence < previous.State.Sequence || state.ActionSequence < previous.State.ActionSequence)
        {
            throw new InvalidDataException("Authoritative sequence moved backwards.");
        }
        candidate[delta.EntityId] = new Replica(state, delta.Generation, delta.Revision, delta.Removed);
    }
    private static PlayerState ReadFields(PlayerState state, IEnumerable<scalar_field> fields, bool complete)
    {
        var seen = new HashSet<uint>();
        foreach (scalar_field field in fields)
        {
            if (!seen.Add(field.Index))
            {
                throw new InvalidDataException("Duplicate scalar index.");
            }
            scalar_field.ValueOneofCase expected = field.Index switch
            {
                100 => scalar_field.ValueOneofCase.StringValue,
                101 or 102 => scalar_field.ValueOneofCase.DoubleValue,
                >= 103 and <= 108 => scalar_field.ValueOneofCase.Uint32Value,
                _ => throw new InvalidDataException("Unknown scalar index.")
            };
            if (field.ValueCase != expected)
            {
                throw new InvalidDataException("Wrong scalar value type.");
            }
            state = field.Index switch
            {
                100 => state with { Name = StrictUtf8.GetString(field.StringValue.Span) },
                101 => state with { X = field.DoubleValue },
                102 => state with { Y = field.DoubleValue },
                103 => state with { Sequence = field.Uint32Value },
                104 => state with { Hp = field.Uint32Value },
                105 => state with { Mp = field.Uint32Value },
                106 => state with { HpMax = field.Uint32Value },
                107 => state with { MpMax = field.Uint32Value },
                108 => state with { ActionSequence = field.Uint32Value },
                _ => state
            };
        }
        if (complete && seen.Count != 9)
        {
            throw new InvalidDataException("Incomplete scalar baseline.");
        }
        ValidateName(state.Name);
        ValidatePosition(state.X, state.Y);
        if (state.HpMax == 0 || state.MpMax == 0 || state.Hp > state.HpMax || state.Mp > state.MpMax)
        {
            throw new InvalidDataException("Invalid authoritative HP/MP range.");
        }
        return state;
    }
    private void Commit(Dictionary<ulong, Replica> candidate)
    {
        PlayerState[] players = candidate.Values.Where(entry => !entry.Removed).Select(entry => entry.State).OrderBy(player => player.EntityId).ToArray();
        if (players.Length is < 1 or > 16 || candidate.Count > 4096 ||
            !players.Any(player => player.Id == PlayerId && player.EntityId == EntityId && player.Name == Name))
        {
            throw new InvalidDataException("Invalid roster or own identity binding.");
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
