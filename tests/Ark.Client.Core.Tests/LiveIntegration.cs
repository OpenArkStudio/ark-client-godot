using Ark.Client.Core;

internal static class LiveIntegration
{
    private sealed class Client : IDisposable
    {
        public TcpConnection Connection { get; } = new();
        public RoomSession Room { get; } = new();
        public string Name
        {
            get;
        }
        private Task? _run;
        public Client(string name)
        {
            Name = name;
        }
        public async Task Connect(string host, int port)
        {
            _run = Connection.RunAsync(host, port);
            await Until(() => Connection.IsConnected);
            await Connection.SendAsync(RoomSession.JoinFrame(Name));
            await Until(() => Room.HasSnapshot);
        }
        public void Poll()
        {
            while (Connection.TryDequeue(out TransportEvent? item))
            {
                if (item!.Kind == "closed")
                {
                    throw new IOException(Name + ": " + item.Message);
                }
                if (item.Frame is { } frame)
                {
                    try
                    {
                        Room.Apply(frame);
                    }
                    catch { Connection.Dispose(); throw; }
                    if (Room.LastError is not null)
                    {
                        throw new IOException(Room.LastError);
                    }
                }
            }
        }
        public async Task Until(Func<bool> predicate, Client? other = null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            do
            {
                Poll();
                other?.Poll();
                if (predicate())
                {
                    return;
                }
                await Task.Delay(5, timeout.Token);
            } while (true);
        }
        public async Task Close()
        {
            Connection.Dispose();
            if (_run is not null)
            {
                await _run.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        public void Dispose()
        {
            Connection.Dispose();
        }
    }
    public static async Task<int> Run(string[] args)
    {
        string host = args.Length > 1 ? args[1] : "127.0.0.1";
        int port = args.Length > 2 ? int.Parse(args[2]) : 19100;
        using var alice = new Client("Alice");
        await alice.Connect(host, port);
        ulong oldEntity = alice.Room.EntityId;
        string oldId = alice.Room.PlayerId!;
        Program.Check(alice.Room.Players.Count == 1, "Live runner requires an initially empty room.");
        using var bob = new Client("Bob");
        await bob.Connect(host, port);
        await alice.Until(() => alice.Room.Players.Count == 2 && bob.Room.Players.Count == 2, bob);
        Program.Check(alice.Room.BaselineCount == 1 && bob.Room.BaselineCount == 1 && alice.Room.DeltaCount >= 1, "Late join did not use added baseline.");
        await alice.Connection.SendAsync(alice.Room.MoveFrame(120, 100));
        await bob.Connection.SendAsync(bob.Room.MoveFrame(240, 180));
        await alice.Until(() => alice.Room.Players.All(player => player.Sequence == 1) && bob.Room.Players.All(player => player.Sequence == 1), bob);
        foreach ((string kind, uint hp, uint mp, uint sequence) in new[] { ("damage", 85u, 100u, 1u), ("skill", 85u, 80u, 2u), ("rest", 100u, 100u, 3u), ("rest", 100u, 100u, 4u) })
        {
            await alice.Connection.SendAsync(alice.Room.ActionFrame(kind));
            await bob.Connection.SendAsync(bob.Room.ActionFrame(kind));
            await alice.Until(() => alice.Room.Players.All(player => player.ActionSequence == sequence && player.Hp == hp && player.Mp == mp) &&
                bob.Room.Players.All(player => player.ActionSequence == sequence && player.Hp == hp && player.Mp == mp), bob);
            Console.WriteLine($"LIVE ACTION {kind} hp={hp} mp={mp} action_seq={sequence} peers=2");
        }
        Program.Check(alice.Room.BaselineCount == 1 && bob.Room.BaselineCount == 1, "Movement/actions broadcast full room baselines.");
        await alice.Close();
        await bob.Until(() => bob.Room.Players.Count == 1);
        Program.Check(bob.Room.Players.Single().Id == bob.Room.PlayerId, "Removal retained disconnected player.");
        using var fresh = new Client("Alice");
        await fresh.Connect(host, port);
        await bob.Until(() => bob.Room.Players.Count == 2 && fresh.Room.Players.Count == 2, fresh);
        Program.Check(fresh.Room.EntityId != oldEntity && fresh.Room.PlayerId != oldId && fresh.Room.BaselineCount == 1 &&
            fresh.Room.Players.Single(player => player.EntityId == fresh.Room.EntityId) is { Hp: 100, Mp: 100, Sequence: 0, ActionSequence: 0 }, "Reconnect did not install fresh identity/baseline.");
        await fresh.Connection.SendAsync(RoomSession.PingFrame(55));
        await fresh.Until(() => fresh.Room.LastPong == 55, bob);
        Console.WriteLine($"LIVE PASS join late_join delta_move hp_mp no_op_action removal reconnect ping old_entity={oldEntity} new_entity={fresh.Room.EntityId} alice_baselines={alice.Room.BaselineCount} bob_baselines={bob.Room.BaselineCount} fresh_baselines={fresh.Room.BaselineCount}");
        await fresh.Close();
        await bob.Close();
        return 0;
    }
}
