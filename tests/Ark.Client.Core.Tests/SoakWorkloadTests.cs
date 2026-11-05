using Ark.Client.Core;

namespace Ark.Client.Testing;

internal static class SoakWorkloadTests
{
    private static PlayerState State(uint sequence, uint hp = 100, uint mp = 100, uint quantity = 2, ulong row = 1,
        uint item = 1001, uint power = 10, long equipment = 43, ulong incarnation = 7) => new("1", "Alice",
            sequence == 0 ? 100 : 80 + ((int)sequence - 1) * 17 % 800,
            sequence == 0 ? 100 : 60 + ((int)sequence - 1) * 11 % 420, sequence, hp, mp, 100, 100, sequence, 42)
        {
            Inventory = quantity == 0 ? Array.Empty<InventoryItem>() : Array.AsReadOnly(new[] { new InventoryItem(1, row, 1, quantity) }),
            Equipment = Array.AsReadOnly(new[] { new EquipmentItem(1, equipment, incarnation, item, power) })
        };

    private static PlayerState[] Cycle(uint cycle)
    {
        uint first = cycle * SoakWorkload.CycleLength;
        uint blade = Math.Min(100, 10 + cycle), staff = Math.Min(100, 20 + cycle);
        return new[]
        {
            State(first + 1, 85, 100, row: cycle + 1, power: blade),
            State(first + 2, 85, 80, row: cycle + 1, power: blade),
            State(first + 3, row: cycle + 1, power: blade),
            State(first + 4, row: cycle + 1, power: blade),
            State(first + 5, quantity: 1, row: cycle + 1, power: blade),
            State(first + 6, quantity: 0, power: blade),
            State(first + 7, quantity: 1, row: cycle + 2, power: blade),
            State(first + 8, row: cycle + 2, power: blade),
            State(first + 9, row: cycle + 2, item: 1002, power: staff, equipment: 44, incarnation: 8),
            State(first + 10, row: cycle + 2, item: 1002, power: Math.Min(100, staff + 1), equipment: 44, incarnation: 8),
            State(first + 11, row: cycle + 2, power: blade),
            State(first + 12, row: cycle + 2, power: Math.Min(100, blade + 1))
        };
    }
    private static SoakState Through(int phases)
    {
        var observer = new SoakState(State(0));
        foreach (PlayerState next in Cycle(0).Take(phases))
        {
            observer.Observe(next);
        }
        return observer;
    }
    private static void Reject(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error.GetType() == typeof(Exception) && error.Message != "Expected workload rejection.") { return; }
        throw new Exception("Expected workload rejection.");
    }
    public static void Verify()
    {
        foreach (int seconds in new[] { 30, 31, 600, 3600 })
        {
            int resident = SoakWorkload.Cycles(seconds, false), churn = SoakWorkload.Cycles(seconds, true);
            Program.Check(resident * 3 >= seconds && resident * 3 <= seconds + 2 && churn >= 2 &&
                churn * 3000 + (churn - 1) / 2 * 250 <= (seconds - 5) * 1000, "Finite cycle plan exceeded its duration/settling bounds.");
        }
        Reject(() => SoakWorkload.Cycles(29, false));
        Reject(() => SoakWorkload.Cycles(3601, true));
        Program.Check(SoakWorkload.Kinds.Count == 12 && SoakWorkload.Kinds.Count(kind => kind == "equip") == 2 &&
            SoakWorkload.Kinds.Count(kind => kind == "upgrade") == 2, "Nested cycle omitted identity/return phases.");
        Reject(() => SoakWorkload.RequireLocalOutput("relative-output"));
        Reject(() => SoakWorkload.RequireLocalOutput(ProtocolParity.FindRoot()));
        SoakWorkload.RequireLocalOutput(Path.GetTempPath());

        var state = new SoakState(State(0));
        PlayerState retained = state.Current;
        for (uint cycle = 0; cycle < 100; cycle++)
        {
            foreach (PlayerState next in Cycle(cycle))
            {
                Program.Check(state.Observe(next), "Valid complete nested phase was not recorded.");
                Program.Check(!state.Observe(next), "Repeated committed observation counted another action.");
            }
        }
        Program.Check(state.Current.ActionSequence == 1200 && state.Current.Equipment.Single().Power == 100 && state.RowWatermark == 101 &&
            retained.Inventory.Single().Incarnation == 1 && retained.Equipment.Single().Power == 10, "Capped upgrades, row history or retained state changed.");
        Reject(() => new SoakState(State(0, quantity: 1)));
        Reject(() => Through(0).Observe(Cycle(0)[1]));
        Reject(() => Through(0).Observe(Cycle(0)[0] with { X = 81 }));
        Reject(() => Through(4).Observe(Cycle(0)[4] with { Inventory = new[] { new InventoryItem(1, 2, 1, 1) } }));
        Reject(() => Through(6).Observe(Cycle(0)[6] with { Inventory = new[] { new InventoryItem(1, 1, 1, 1) } }));
        Reject(() => Through(9).Observe(Cycle(0)[9] with { Equipment = new[] { new EquipmentItem(1, 44, 9, 1002, 21) } }));
        Reject(() => Through(10).Observe(Cycle(0)[10] with { Equipment = new[] { new EquipmentItem(1, 45, 7, 1001, 10) } }));
        Reject(() => Through(10).Observe(Cycle(0)[10] with { Equipment = new[] { new EquipmentItem(1, 43, 7, 1001, 20) } }));
    }
}
