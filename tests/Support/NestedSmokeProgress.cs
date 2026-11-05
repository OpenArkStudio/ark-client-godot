using Ark.Client.Core;

namespace Ark.Client.Testing;

/// <summary>Records committed row lifetimes and child identities in both client views.</summary>
public sealed class NestedSmokeProgress
{
    public static readonly string[] Kinds = { "use_potion", "use_potion", "pickup", "equip", "upgrade", "equip" };
    private readonly Dictionary<string, int> _observed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ulong Row, long Equipment, ulong EquipmentIncarnation)> _initial = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long EntityId, ulong Incarnation)> _replacement = new(StringComparer.Ordinal);

    public void Observe(IReadOnlyList<PlayerState> players)
    {
        foreach (PlayerState player in players)
        {
            InventoryItem? potion = player.Inventory.FirstOrDefault();
            EquipmentItem? equipment = player.Equipment.FirstOrDefault();
            if (equipment is null)
            {
                continue;
            }
            if (player.ActionSequence == 0 && potion is { Quantity: 2, ItemId: 1 } &&
                equipment is { ItemId: 1001, Power: 10 })
            {
                _initial.TryAdd(player.Id, (potion.Incarnation, equipment.EntityId, equipment.Incarnation));
            }
            if (!_initial.TryGetValue(player.Id, out var initial))
            {
                continue;
            }
            int phase = player.ActionSequence switch
            {
                4 when potion is { Quantity: 1 } && potion.Incarnation == initial.Row => 0,
                5 when player.Inventory.Count == 0 => 1,
                6 when potion is { Quantity: 1, Row: 1 } && potion.Incarnation > initial.Row => 2,
                7 when equipment is { ItemId: 1002, Power: 20 } && equipment.EntityId != initial.Equipment => 3,
                8 when equipment is { ItemId: 1002, Power: 21 } &&
                    _replacement.GetValueOrDefault(player.Id) == (equipment.EntityId, equipment.Incarnation) => 4,
                9 when equipment is { ItemId: 1001, Power: 10 } && equipment.EntityId == initial.Equipment &&
                    equipment.Incarnation == initial.EquipmentIncarnation => 5,
                _ => -1
            };
            if (phase == 3)
            {
                _replacement[player.Id] = (equipment.EntityId, equipment.Incarnation);
            }
            if (phase >= 0)
            {
                _observed[player.Id] = _observed.GetValueOrDefault(player.Id) | (1 << phase);
            }
        }
    }

    public bool SawBoth(string ownId, string peerId, int phase) =>
        (_observed.GetValueOrDefault(ownId) & (1 << phase)) != 0 &&
        (_observed.GetValueOrDefault(peerId) & (1 << phase)) != 0;

    public bool Completed(string ownId, string peerId) =>
        Enumerable.Range(0, Kinds.Length).All(phase => SawBoth(ownId, peerId, phase));

    public bool SawFresh(string id) => _initial.ContainsKey(id);
}
