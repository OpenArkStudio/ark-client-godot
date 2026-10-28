namespace Ark.Client.Core;

public sealed record AttributeChange(string PlayerId, long HpDelta, long MpDelta);

/// <summary>Feedback originates only from differences between valid committed authoritative room states.</summary>
public sealed class ConfirmedAttributes
{
    private Dictionary<string, PlayerState> _previous = new(StringComparer.Ordinal);

    public IReadOnlyList<AttributeChange> Observe(IReadOnlyList<PlayerState> players)
    {
        var changes = new List<AttributeChange>();
        foreach (PlayerState player in players)
        {
            if (_previous.TryGetValue(player.Id, out PlayerState? old))
            {
                long hp = (long)player.Hp - old.Hp, mp = (long)player.Mp - old.Mp;
                if (hp != 0 || mp != 0)
                {
                    changes.Add(new AttributeChange(player.Id, hp, mp));
                }
            }
        }
        _previous = players.ToDictionary(player => player.Id, StringComparer.Ordinal);
        return changes;
    }

    public void Reset() => _previous.Clear();
}
