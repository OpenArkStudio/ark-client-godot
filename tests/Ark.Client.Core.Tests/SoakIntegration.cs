using System.Diagnostics;
using System.Text.Json;
using Ark.Client.Core;

namespace Ark.Client.Testing;

internal static class SoakIntegration
{
    public static void Verify(string directory)
    {
        var results = new[] { "Alice", "Bob", "Churn" }.Select(name =>
            JsonSerializer.Deserialize<Counters>(File.ReadAllText(Path.Combine(directory, "result-" + name + ".json")))!).ToArray();
        Counters churn = results[2];
        foreach (Counters result in results)
        {
            Program.Check(result.Failures == 0 && result.Connections == result.Disconnects && result.Baselines == (ulong)result.Connections &&
                result.Moves == result.Damage + result.Skill + result.Rest + result.CappedRest && result.Moves == result.Pongs &&
                result.TxFrames == result.Connections + 3L * result.Moves, "Incomplete acknowledged workload: " + result.Name);
        }
        Program.Check(churn.Connections > 1 && churn.FreshReconnects == churn.Connections - 1, "Churn did not create fresh connections.");
        foreach (Counters resident in results.Take(2))
        {
            Program.Check(resident.PeerJoins == churn.Connections + 1 && resident.PeerRemovals >= churn.Connections &&
                resident.PeerRemovals <= churn.Connections + 1, "Resident missed a churn join/removal: " + resident.Name);
        }
        Console.WriteLine($"SOAK VERIFIED processes=3 max_joined=3 connections={results.Sum(result => result.Connections)} " +
            $"moves={results.Sum(result => result.Moves)} actions={results.Sum(result => result.Moves)} pongs={results.Sum(result => result.Pongs)} failures=0");
    }

    private sealed class Counters
    {
        public string Name { get; init; } = "";
        public string Role { get; init; } = "";
        public int ProcessId { get; } = Environment.ProcessId;
        public double ElapsedSeconds
        {
            get; set;
        }
        public int Connections
        {
            get; set;
        }
        public int Disconnects
        {
            get; set;
        }
        public int FreshReconnects
        {
            get; set;
        }
        public int Moves
        {
            get; set;
        }
        public int Damage
        {
            get; set;
        }
        public int Skill
        {
            get; set;
        }
        public int Rest
        {
            get; set;
        }
        public int CappedRest
        {
            get; set;
        }
        public int Pongs
        {
            get; set;
        }
        public int PeerJoins
        {
            get; set;
        }
        public int PeerRemovals
        {
            get; set;
        }
        public int PeerMoves
        {
            get; set;
        }
        public int PeerActions
        {
            get; set;
        }
        public int MaxPlayers
        {
            get; set;
        }
        public ulong Baselines
        {
            get; set;
        }
        public ulong Deltas
        {
            get; set;
        }
        public long TxFrames
        {
            get; set;
        }
        public long RxFrames
        {
            get; set;
        }
        public long TxBytes
        {
            get; set;
        }
        public long RxBytes
        {
            get; set;
        }
        public int Failures
        {
            get; set;
        }
    }

    private sealed class Session
    {
        public TcpConnection Connection { get; } = new();
        public RoomSession Room { get; } = new();
        private readonly Counters _counts;
        private Dictionary<ulong, PlayerState> _peers = new();
        private Task? _receive;
        private bool _closed;

        public Session(Counters counts)
        {
            _counts = counts;
        }

        public async Task Connect(string host, int port)
        {
            _receive = Connection.RunAsync(host, port);
            await Until(() => Connection.IsConnected);
            await Connection.SendAsync(RoomSession.JoinFrame(_counts.Name));
            await Until(() => Room.HasSnapshot);
            Program.Check(Room.BaselineCount == 1 && Own() is { Hp: 100, Mp: 100, Sequence: 0, ActionSequence: 0 },
                "New connection did not install a fresh baseline and initial attributes.");
            _counts.Connections++;
        }

        private PlayerState Own() => Room.Players.Single(player => player.EntityId == Room.EntityId);

        public void Poll()
        {
            while (Connection.TryDequeue(out TransportEvent? item))
            {
                if (item!.Kind == "closed")
                {
                    throw new IOException(_counts.Name + ": unexpected transport closure: " + item.Message);
                }
                if (item.Frame is not { } frame)
                {
                    continue;
                }
                Room.Apply(frame);
                Program.Check(Room.LastError is null, "Server rejected activity: " + Room.LastError);
                Program.Check(Room.Players.All(player => player.Name is "Alice" or "Bob" or "Churn"),
                    "Soak runner requires a dedicated empty lobby.");
                _counts.MaxPlayers = Math.Max(_counts.MaxPlayers, Room.Players.Count);
                var peers = Room.Players.Where(player => player.EntityId != Room.EntityId).ToDictionary(player => player.EntityId);
                _counts.PeerRemovals += _peers.Keys.Count(id => !peers.ContainsKey(id));
                foreach ((ulong id, PlayerState player) in peers)
                {
                    if (!_peers.TryGetValue(id, out PlayerState? previous))
                    {
                        _counts.PeerJoins++;
                    }
                    else
                    {
                        if (player.Sequence != previous.Sequence)
                        {
                            _counts.PeerMoves++;
                        }
                        if (player.ActionSequence != previous.ActionSequence)
                        {
                            _counts.PeerActions++;
                        }
                    }
                }
                _peers = peers;
            }
        }

        public async Task Until(Func<bool> predicate)
        {
            var deadline = Stopwatch.StartNew();
            do
            {
                Poll();
                if (predicate())
                {
                    return;
                }
                if (deadline.Elapsed > TimeSpan.FromSeconds(8))
                {
                    throw new TimeoutException(_counts.Name + ": activity or coordination acknowledgement timed out.");
                }
                await Task.Delay(5);
            } while (true);
        }

        public async Task Activity(int tick)
        {
            double x = 80 + tick * 17 % 800;
            double y = 60 + tick * 11 % 420;
            await Connection.SendAsync(Room.MoveFrame(x, y));
            uint moveSequence = Room.LastProposedMove!.Sequence;
            string kind = (tick % 4) switch
            {
                0 => "damage",
                1 => "skill",
                _ => "rest"
            };
            PlayerState before = Own();
            uint hp = kind switch
            {
                "damage" => before.Hp - Math.Min(15u, before.Hp),
                "rest" => Math.Min(before.HpMax, before.Hp + 20),
                _ => before.Hp
            };
            uint mp = kind switch
            {
                "skill" => before.Mp - 20,
                "rest" => Math.Min(before.MpMax, before.Mp + 20),
                _ => before.Mp
            };
            await Connection.SendAsync(Room.ActionFrame(kind));
            uint actionSequence = Room.LastProposedAction!.Sequence;
            uint nonce = (uint)tick + 1;
            await Connection.SendAsync(RoomSession.PingFrame(nonce));
            await Until(() => Own() is var player && player.Sequence == moveSequence && player.X == x && player.Y == y &&
                player.ActionSequence == actionSequence && player.Hp == hp && player.Mp == mp && Room.PendingAction is null && Room.LastPong == nonce);
            _counts.Moves++;
            _counts.Pongs++;
            switch (tick % 4)
            {
                case 0:
                    _counts.Damage++;
                    break;
                case 1:
                    _counts.Skill++;
                    break;
                case 2:
                    _counts.Rest++;
                    break;
                case 3:
                    _counts.CappedRest++;
                    break;
            }
        }

        public async Task Close()
        {
            if (_closed)
            {
                return;
            }
            _closed = true;
            Connection.Dispose();
            if (_receive is not null)
            {
                await _receive.WaitAsync(TimeSpan.FromSeconds(5));
            }
            _counts.Disconnects++;
            _counts.Baselines += Room.BaselineCount;
            _counts.Deltas += Room.DeltaCount;
            TransportMetrics metrics = Connection.Metrics;
            _counts.TxFrames += metrics.TxFrames;
            _counts.RxFrames += metrics.RxFrames;
            _counts.TxBytes += metrics.TxBytes;
            _counts.RxBytes += metrics.RxBytes;
        }
    }

    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 7 || !int.TryParse(args[2], out int port) || port is < 1 or > 65535 ||
            !int.TryParse(args[5], out int seconds) || seconds is < 30 or > 3600 ||
            !((args[3] is "Alice" or "Bob" && args[4] == "resident") || (args[3] == "Churn" && args[4] == "churn")))
        {
            Console.Error.WriteLine("Usage: --soak-client host port Alice|Bob|Churn resident|churn seconds control-directory");
            return 2;
        }
        string host = args[1], name = args[3], role = args[4], control = args[6];
        var counts = new Counters { Name = name, Role = role };
        var elapsed = new Stopwatch();
        Session? session = null;
        try
        {
            session = new Session(counts);
            await session.Connect(host, port);
            await session.Until(() => session.Room.Players.Count == 3);
            File.WriteAllText(Path.Combine(control, "ready-" + name), "ready\n");
            await session.Until(() => File.Exists(Path.Combine(control, "begin")));
            elapsed.Start();
            int tick = 0, nextProgress = 30;
            double sessionStart = 0;
            var identities = new HashSet<(string?, ulong)> { (session.Room.PlayerId, session.Room.EntityId) };
            int activeSeconds = role == "churn" ? seconds - 5 : seconds;
            while (elapsed.Elapsed.TotalSeconds < activeSeconds)
            {
                double tickStart = elapsed.Elapsed.TotalMilliseconds;
                await session!.Activity(tick++);
                if (elapsed.Elapsed.TotalSeconds >= nextProgress)
                {
                    Console.WriteLine($"SOAK PROGRESS {name} elapsed={elapsed.Elapsed.TotalSeconds:F1} connections={counts.Connections} moves={counts.Moves} actions={counts.Moves} peer_joins={counts.PeerJoins} peer_removals={counts.PeerRemovals}");
                    nextProgress += 30;
                }
                if (role == "churn" && elapsed.Elapsed.TotalSeconds - sessionStart >= 5)
                {
                    await session.Close();
                    session = null;
                    await Task.Delay(250);
                    if (elapsed.Elapsed.TotalSeconds >= activeSeconds)
                    {
                        break;
                    }
                    session = new Session(counts);
                    await session.Connect(host, port);
                    Program.Check(identities.All(id => id.Item1 != session.Room.PlayerId && id.Item2 != session.Room.EntityId),
                        "Reconnect reused a guest identity.");
                    identities.Add((session.Room.PlayerId, session.Room.EntityId));
                    counts.FreshReconnects++;
                    sessionStart = elapsed.Elapsed.TotalSeconds;
                    tick = 0;
                }
                while (elapsed.Elapsed.TotalMilliseconds - tickStart < 250)
                {
                    session?.Poll();
                    await Task.Delay(5);
                }
            }
            if (role == "resident")
            {
                await session!.Until(() => File.Exists(Path.Combine(control, "done-Churn")) && session.Room.Players.Count == 2);
                Program.Check(counts.Connections == 1 && counts.PeerJoins >= 3 && counts.PeerRemovals >= 2 &&
                    counts.PeerMoves > 0 && counts.PeerActions > 0, "Resident did not observe sustained peer activity and churn.");
                File.WriteAllText(Path.Combine(control, "settled-" + name), "settled\n");
                await session.Until(() => File.Exists(Path.Combine(control, "settled-Alice")) && File.Exists(Path.Combine(control, "settled-Bob")));
            }
            Program.Check(counts.MaxPlayers == 3 && counts.Moves > 0 && counts.CappedRest > 0, "No complete three-client workload observed.");
        }
        catch (Exception error)
        {
            counts.Failures++;
            Console.Error.WriteLine(error);
        }
        finally
        {
            if (session is not null)
            {
                try
                {
                    await session.Close();
                }
                catch (Exception error) { counts.Failures++; Console.Error.WriteLine(error); }
            }
            counts.ElapsedSeconds = elapsed.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(control, "result-" + name + ".json"), JsonSerializer.Serialize(counts, new JsonSerializerOptions { WriteIndented = true }));
            if (role == "churn" && counts.Failures == 0)
            {
                File.WriteAllText(Path.Combine(control, "done-Churn"), "done\n");
            }
            Console.WriteLine($"SOAK {(counts.Failures == 0 ? "PASS" : "FAIL")} {name} " + JsonSerializer.Serialize(counts));
        }
        return counts.Failures == 0 ? 0 : 1;
    }
}
