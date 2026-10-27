using System.Text;
using System.Text.Json;

namespace Ark.Client.Core;

public sealed record PlayerState(string Id, string Name, float X, float Y, uint Sequence);
public sealed record MoveProposal(uint Sequence, float X, float Y);

public sealed class RoomSession
{
    public const ushort Join = 60000, Accepted = 60001, Move = 60002,
        Snapshot = 60003, Error = 60004, Ping = 60005, Pong = 60006;
    public const int Width = 960, Height = 540;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private uint _sequence;
    public string? PlayerId
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

    public static byte[] JoinFrame(string name)
    {
        ValidateName(name);
        return JsonFrame(Join, new
        {
            name,
            room = "lobby"
        });
    }

    public byte[] MoveFrame(float x, float y)
    {
        if (PlayerId is null)
        {
            throw new InvalidOperationException("Join before moving.");
        }

        ValidatePosition(x, y);
        if (_sequence == uint.MaxValue)
        {
            throw new InvalidOperationException("Movement sequence exhausted; reconnect.");
        }

        LastProposedMove = new MoveProposal(++_sequence, x, y);
        return JsonFrame(Move, new
        {
            seq = _sequence,
            x,
            y
        });
    }

    public static byte[] PingFrame(uint nonce) => JsonFrame(Ping, new { nonce });
    public static byte[] JsonFrame<T>(ushort id, T body) => FrameCodec.Encode(id, JsonSerializer.SerializeToUtf8Bytes(body));

    public void Reset()
    {
        _sequence = 0;
        PlayerId = null;
        Name = null;
        Players = Array.Empty<PlayerState>();
        LastError = null;
        HasSnapshot = false;
        LastPong = 0;
        LastProposedMove = null;
    }

    public string Apply(Frame frame)
    {
        try
        {
            string text = StrictUtf8.GetString(frame.Body);
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Expected a JSON object.");
            }

            switch (frame.MessageId)
            {
                case Accepted:
                    if (PlayerId is not null)
                    {
                        throw new InvalidDataException("Duplicate join acceptance.");
                    }

                    RequireLobby(root);
                    if (root.GetProperty("width").GetInt32() != Width || root.GetProperty("height").GetInt32() != Height)
                    {
                        throw new InvalidDataException("Unsupported room dimensions.");
                    }

                    string id = ReadId(root, "player_id");
                    string name = root.GetProperty("name").GetString() ?? "";
                    ValidateName(name);
                    PlayerId = id;
                    Name = name;
                    return "accepted";
                case Snapshot:
                    if (PlayerId is null)
                    {
                        throw new InvalidDataException("Snapshot arrived before join acceptance.");
                    }

                    RequireLobby(root);
                    JsonElement array = root.GetProperty("players");
                    if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 16)
                    {
                        throw new InvalidDataException("Invalid player roster.");
                    }

                    var players = new List<PlayerState>();
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        string playerId = ReadId(item, "id");
                        if (!ids.Add(playerId))
                        {
                            throw new InvalidDataException("Duplicate player ID.");
                        }

                        string playerName = item.GetProperty("name").GetString() ?? "";
                        ValidateName(playerName);
                        float x = item.GetProperty("x").GetSingle(), y = item.GetProperty("y").GetSingle();
                        ValidatePosition(x, y);
                        uint seq = item.GetProperty("seq").GetUInt32();
                        PlayerState? previous = Players.FirstOrDefault(player => player.Id == playerId);
                        if (previous is not null && seq < previous.Sequence)
                        {
                            throw new InvalidDataException("Snapshot sequence moved backwards.");
                        }

                        players.Add(new PlayerState(playerId, playerName, x, y, seq));
                    }
                    if (!ids.Contains(PlayerId))
                    {
                        throw new InvalidDataException("Snapshot does not contain this player.");
                    }

                    Players = players;
                    HasSnapshot = true;
                    return "snapshot";
                case Error:
                    string code = root.GetProperty("code").GetString() ?? "";
                    string message = root.GetProperty("message").GetString() ?? "";
                    if (code.Length == 0 || code.Length > 64 || message.Length > 256)
                    {
                        throw new InvalidDataException("Invalid server error.");
                    }

                    LastError = code + ": " + message;
                    return "error";
                case Pong:
                    LastPong = root.GetProperty("nonce").GetUInt32();
                    return "pong";
                default:
                    throw new InvalidDataException("Unsupported server message ID.");
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or DecoderFallbackException or ArgumentException)
        {
            throw new InvalidDataException("Invalid room message: " + error.Message, error);
        }
    }

    private static void RequireLobby(JsonElement root)
    {
        if (root.GetProperty("room").GetString() != "lobby")
        {
            throw new InvalidDataException("Unsupported room.");
        }
    }

    private static string ReadId(JsonElement root, string property)
    {
        string value = root.GetProperty(property).GetString() ?? "";
        if (value.Length == 0 || StrictUtf8.GetByteCount(value) > 64 || value.Any(char.IsControl))
        {
            throw new InvalidDataException("Invalid player ID.");
        }

        return value;
    }

    public static void ValidateName(string value)
    {
        if (value.Length == 0 || StrictUtf8.GetByteCount(value) > 64 || value.Any(char.IsControl))
        {
            throw new ArgumentException("Use a nonempty name of at most 64 UTF-8 bytes without control characters.");
        }
    }

    public static void ValidatePosition(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x > Width || y < 0 || y > Height)
        {
            throw new InvalidDataException("Position is outside room bounds.");
        }
    }
}
