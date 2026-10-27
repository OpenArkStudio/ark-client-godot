using Ark.Client.Core;

namespace Ark.Client.Testing;

/// <summary>Records every applied snapshot so batched receive events cannot skip peer evidence.</summary>
public sealed class AttributeSmokeProgress
{
    public static readonly string[] Kinds = { "damage", "skill", "rest" };
    private readonly Dictionary<string, int> _observed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fresh = new(StringComparer.Ordinal);

    public void Observe(IReadOnlyList<PlayerState> players)
    {
        foreach (PlayerState player in players)
        {
            if (player.HpMax != 100 || player.MpMax != 100)
            {
                continue;
            }
            if (player.ActionSequence == 0 && player.Hp == 100 && player.Mp == 100)
            {
                _fresh.Add(player.Id);
            }
            int phase = player.ActionSequence switch
            {
                1 when player.Hp == 85 && player.Mp == 100 => 0,
                2 when player.Hp == 85 && player.Mp == 80 => 1,
                3 when player.Hp == 100 && player.Mp == 100 => 2,
                _ => -1
            };
            if (phase >= 0)
            {
                _observed[player.Id] = _observed.GetValueOrDefault(player.Id) | (1 << phase);
            }
        }
    }

    public bool Saw(string id, int phase) => (_observed.GetValueOrDefault(id) & (1 << phase)) != 0;
    public bool SawBoth(string ownId, string peerId, int phase) => Saw(ownId, phase) && Saw(peerId, phase);
    public bool SawFresh(string id) => _fresh.Contains(id);
    public bool Completed(string ownId, string peerId) => Enumerable.Range(0, 3).All(phase => SawBoth(ownId, peerId, phase));
}
