namespace Ark.Client.Core;

public sealed record DisplayPlayer(PlayerState ServerState, float X, float Y);

/// <summary>Display-only easing toward received state; never predicts or changes room state.</summary>
public sealed class PlayerPresentation
{
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
                next = new DisplayPlayer(state, previous.X + (state.X - previous.X) * weight,
                    previous.Y + (state.Y - previous.Y) * weight);
            }
            else
            {
                next = new DisplayPlayer(state, state.X, state.Y);
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
