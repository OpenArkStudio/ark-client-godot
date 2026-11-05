using Ark.Client.Core;

namespace Ark.Client.Testing;

/// <summary>A finite schedule of complete state transitions, shared by the runner and its permanent tests.</summary>
internal static class SoakWorkload
{
    public const int TickMilliseconds = 250;
    public const int CycleLength = 12;
    public const int CycleSeconds = CycleLength * TickMilliseconds / 1000;
    public static readonly IReadOnlyList<string> Kinds = Array.AsReadOnly(new[]
    { "damage", "skill", "rest", "rest", "use_potion", "use_potion", "pickup", "pickup", "equip", "upgrade", "equip", "upgrade" });

    public static int Cycles(int seconds, bool churn)
    {
        Program.Check(seconds is >= 30 and <= 3600, "Duration must be 30..3600 seconds.");
        if (!churn)
        {
            return (seconds + CycleSeconds - 1) / CycleSeconds;
        }
        int budget = (seconds - 5) * 1000, cycles = budget / (CycleSeconds * 1000);
        while (cycles > 0 && cycles * CycleSeconds * 1000 + (cycles - 1) / 2 * 250 > budget)
        {
            cycles--;
        }
        return cycles;
    }

    public static void RequireLocalOutput(string directory)
    {
        Program.Check(Path.IsPathFullyQualified(directory) && Directory.Exists(directory), "An existing absolute local output directory is required.");
        for (DirectoryInfo? ancestor = new(directory); ancestor is not null; ancestor = ancestor.Parent)
        {
            string git = Path.Combine(ancestor.FullName, ".git");
            Program.Check(!File.Exists(git) && !Directory.Exists(git), "Soak evidence must stay outside Git repositories.");
        }
    }
}

/// <summary>Observes each committed state, including frames drained together; rejects skipped or incorrect transitions.</summary>
internal sealed class SoakState
{
    private readonly Dictionary<uint, EquipmentItem> _weapons = new();
    private ulong _rowWatermark;
    public PlayerState Current
    {
        get; private set;
    }
    public ulong RowWatermark => _rowWatermark;
    public SoakState(PlayerState initial)
    {
        Program.Check(initial.Hp == 100 && initial.Mp == 100 && initial.HpMax == 100 && initial.MpMax == 100 && initial.Sequence == 0 && initial.ActionSequence == 0 &&
            initial.Inventory.Count == 1 && initial.Inventory.Single() is { Row: 1, ItemId: 1, Quantity: 2, Incarnation: > 0 } &&
            initial.Equipment.Count == 1 && initial.Equipment.Single() is { Slot: 1, ItemId: 1001, Power: 10, EntityId: > 0, Incarnation: > 0 },
            "A fresh stream did not install initial nested state.");
        Current = initial;
        _rowWatermark = initial.Inventory.Single().Incarnation;
        _weapons.Add(1001, initial.Equipment.Single());
    }

    public bool Observe(PlayerState next)
    {
        PlayerState old = Current;
        Program.Check(next.Id == old.Id && next.EntityId == old.EntityId && next.Name == old.Name && next.HpMax == 100 && next.MpMax == 100,
            "A committed workload stream changed its binding or attribute maxima.");
        Program.Check(next.Sequence == old.Sequence || (old.Sequence != uint.MaxValue && next.Sequence == old.Sequence + 1), "A move sequence was skipped or regressed.");
        if (next.Sequence == old.Sequence)
        {
            Program.Check(next.X == old.X && next.Y == old.Y, "Position changed without a move acknowledgement.");
        }
        else
        {
            int tick = checked((int)next.Sequence - 1);
            Program.Check(next.X == 80 + tick * 17 % 800 && next.Y == 60 + tick * 11 % 420, "Peer movement differs from its finite workload proposal.");
        }
        Program.Check(next.ActionSequence == old.ActionSequence || (old.ActionSequence != uint.MaxValue && next.ActionSequence == old.ActionSequence + 1),
            "A committed action phase was skipped or regressed.");
        bool advanced = next.ActionSequence != old.ActionSequence;
        int phase = advanced ? checked((int)(next.ActionSequence - 1) % SoakWorkload.CycleLength) : -1;
        uint hp = old.Hp, mp = old.Mp;
        if (phase == 0)
        {
            hp -= Math.Min(15u, hp);
        }
        if (phase == 1)
        {
            Program.Check(mp >= 20, "The plan requested an unaffordable skill.");
            mp -= 20;
        }
        if (phase is 2 or 3)
        {
            hp = Math.Min(100, hp + 20);
            mp = Math.Min(100, mp + 20);
        }
        if (phase is 4 or 5)
        {
            hp = Math.Min(100, hp + 10);
        }
        Program.Check(next.Hp == hp && next.Mp == mp, "HP/MP do not match the committed action phase.");

        InventoryItem? beforeRow = old.Inventory.SingleOrDefault(), row = next.Inventory.SingleOrDefault();
        if (phase is 4 or 5)
        {
            Program.Check(beforeRow is not null, "The plan consumed an empty inventory.");
            Program.Check(beforeRow!.Quantity == 1 ? row is null : row == (beforeRow with
            {
                Quantity = beforeRow.Quantity - 1
            }), "Potion consumption did not patch or remove the row.");
        }
        else if (phase is 6 or 7)
        {
            if (beforeRow is null)
            {
                Program.Check(row is { Row: 1, ItemId: 1, Quantity: 1 } && row.Incarnation > _rowWatermark, "Row reuse did not advance its historical incarnation.");
            }
            else
            {
                Program.Check(row == (beforeRow with
                {
                    Quantity = beforeRow.Quantity + 1
                }), "Pickup changed the existing row identity or wrong quantity.");
            }
        }
        else
        {
            Program.Check(row == beforeRow, "An unrelated action changed inventory state.");
        }

        EquipmentItem beforeWeapon = old.Equipment.Single(), weapon = next.Equipment.Single();
        if (phase is 8 or 10)
        {
            uint target = beforeWeapon.ItemId == 1001 ? 1002u : 1001u;
            Program.Check(weapon.ItemId == target && weapon.Slot == 1 && weapon.EntityId != beforeWeapon.EntityId && weapon.Incarnation != 0, "Equipment membership was not replaced.");
            if (_weapons.TryGetValue(target, out EquipmentItem? expected))
            {
                Program.Check(weapon == expected, "Returning equipment changed its GUID, incarnation or retained power.");
            }
            else
            {
                Program.Check(target == 1002 && weapon.Power == 20, "The first Staff state is invalid.");
            }
        }
        else if (phase is 9 or 11)
        {
            Program.Check(weapon == (beforeWeapon with
            {
                Power = Math.Min(100, beforeWeapon.Power + 1)
            }), "Child power update changed its identity/lifetime or incorrect power.");
        }
        else
        {
            Program.Check(weapon == beforeWeapon, "An unrelated action changed equipment state.");
        }

        if (row is not null)
        {
            _rowWatermark = Math.Max(_rowWatermark, row.Incarnation);
        }
        _weapons[weapon.ItemId] = weapon;
        Current = next;
        return advanced;
    }
}
