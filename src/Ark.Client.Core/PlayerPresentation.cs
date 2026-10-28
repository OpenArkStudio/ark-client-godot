namespace Ark.Client.Core;

public enum PlayerRole
{
    Local, Peer
}

public sealed record DisplayPlayer(PlayerState ServerState, float X, float Y,
    float FacingX = 0, float FacingY = 1, float WalkPhase = 0, bool IsWalking = false);

/// <summary>Display-only easing toward received state; never predicts or changes room state.</summary>
public sealed class PlayerPresentation
{
    public static PlayerRole RoleFor(string playerId, string? localId) =>
        localId is not null && string.Equals(playerId, localId, StringComparison.Ordinal) ? PlayerRole.Local : PlayerRole.Peer;
    private readonly Dictionary<string, DisplayPlayer> _players = new(StringComparer.Ordinal);
    public IReadOnlyList<DisplayPlayer> Players { get; private set; } = Array.Empty<DisplayPlayer>();

    public void Update(IReadOnlyList<PlayerState> authoritative, double deltaSeconds, bool smoothing)
    {
        var liveIds = authoritative.Select(player => player.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string removed in _players.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _players.Remove(removed);
        }
        float weight = smoothing ? (float)(1 - Math.Exp(-12 * Math.Clamp(deltaSeconds, 0, 0.1))) : 1;
        var result = new List<DisplayPlayer>(authoritative.Count);
        foreach (PlayerState state in authoritative)
        {
            DisplayPlayer next;
            if (_players.TryGetValue(state.Id, out DisplayPlayer? previous))
            {
                float x = previous.X + ((float)state.X - previous.X) * weight;
                float y = previous.Y + ((float)state.Y - previous.Y) * weight;
                float dx = x - previous.X, dy = y - previous.Y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                bool walking = distance > 0.03f;
                next = new DisplayPlayer(state, x, y,
                    walking ? dx / distance : previous.FacingX,
                    walking ? dy / distance : previous.FacingY,
                    walking ? (previous.WalkPhase + (float)Math.Clamp(deltaSeconds, 0, 0.1) * 6) % 4 : previous.WalkPhase,
                    walking);
            }
            else
            {
                next = new DisplayPlayer(state, (float)state.X, (float)state.Y);
            }
            _players[state.Id] = next;
            result.Add(next);
        }
        Players = result;
    }

    public void Reset()
    {
        _players.Clear();
        Players = Array.Empty<DisplayPlayer>();
    }
}
