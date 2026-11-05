using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ark.Client.Core;
using Ark.Client.Protocol;
using Ark.Client.Protocol.EntitySync;

namespace Ark.Client.Testing;

internal static class SoakIntegration
{
    public static void Verify(string directory)
    {
        SoakWorkload.RequireLocalOutput(directory);
        var results = new[] { "Alice", "Bob", "Churn" }.Select(name =>
            JsonSerializer.Deserialize<Counters>(File.ReadAllText(Path.Combine(directory, "result-" + name + ".json")))!).ToArray();
        Program.Check(results.Select(result => result.TestAssemblySha256).Distinct().Count() == 1 &&
            results.Select(result => result.ClientCoreSha256).Distinct().Count() == 1, "Clients used different workload/core assemblies.");
        Counters churn = results[2];
        foreach (Counters result in results)
        {
            int cycles = result.CompletedCycles;
            Program.Check(result.SchemaId == RoomSession.SchemaId && result.ProtocolVersion == 2 &&
                result.PlannedCycles == SoakWorkload.Cycles(result.RequestedSeconds, result.Role == "churn") &&
                result.Failures == 0 && cycles == result.PlannedCycles && cycles > 0 && result.Connections == result.Disconnects &&
                result.Baselines == (ulong)result.Connections && result.Moves == cycles * SoakWorkload.CycleLength && result.Moves == result.Pongs &&
                result.Damage == cycles && result.Skill == cycles && result.Rest == cycles && result.CappedRest == cycles &&
                result.PotionUses == 2 * cycles && result.Pickups == 2 * cycles && result.Equips == 2 * cycles && result.Upgrades == 2 * cycles &&
                result.RowRemovals == cycles && result.RowReuses == cycles && result.ContainerReplacements == 2 * cycles && result.ChildPatches == result.ExpectedChildPatches &&
                result.TxFrames == result.Connections + 3L * result.Moves, "Incomplete acknowledged nested workload: " + result.Name);
        }
        Program.Check(churn.Connections > 1 && churn.FreshReconnects == churn.Connections - 1, "Churn did not create fresh connections.");
        foreach (Counters resident in results.Take(2))
        {
            Program.Check(resident.PeerJoins == churn.Connections + 1 && resident.PeerRemovals >= churn.Connections &&
                resident.PeerRemovals <= churn.Connections + 1 && resident.ResidentPeerSteps == resident.Moves,
                "Resident missed a peer phase or churn join/removal: " + resident.Name);
        }
        Console.WriteLine($"SOAK VERIFIED protocol=2 processes=3 max_joined=3 connections={results.Sum(result => result.Connections)} " +
            $"cycles={results.Sum(result => result.CompletedCycles)} moves={results.Sum(result => result.Moves)} actions={results.Sum(result => result.Moves)} " +
            $"pongs={results.Sum(result => result.Pongs)} resident_peer_steps={results.Sum(result => result.ResidentPeerSteps)} failures=0");
    }

    private sealed class Counters
    {
        public string Name { get; init; } = "";
        public string Role { get; init; } = "";
        public string SchemaId { get; init; } = RoomSession.SchemaId;
        public int ProtocolVersion { get; init; } = 2;
        public string TestAssemblySha256 { get; init; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Program).Assembly.Location))).ToLowerInvariant();
        public string ClientCoreSha256 { get; init; } = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(RoomSession).Assembly.Location))).ToLowerInvariant();
        public int ProcessId { get; init; } = Environment.ProcessId;
        public int RequestedSeconds
        {
            get; init;
        }
        public int PlannedCycles
        {
            get; init;
        }
        public int CompletedCycles
        {
            get; set;
        }
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
        public int PotionUses
        {
            get; set;
        }
        public int Pickups
        {
            get; set;
        }
        public int Equips
        {
            get; set;
        }
        public int Upgrades
        {
            get; set;
        }
        public int RowRemovals
        {
            get; set;
        }
        public int RowReuses
        {
            get; set;
        }
        public int ContainerReplacements
        {
            get; set;
        }
        public int ChildPatches
        {
            get; set;
        }
        public int ExpectedChildPatches
        {
            get; set;
        }
        public int Pongs
        {
            get; set;
        }
        public int ResidentPeerSteps
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

    private sealed record Progress(int Step, ulong EntityId);

    private sealed class Session
    {
        public TcpConnection Connection { get; } = new();
        public RoomSession Room { get; } = new();
        private readonly Counters _counts;
        private readonly string _control;
        private readonly Stopwatch _clock;
        private readonly HashSet<long> _retiredEntities;
        public HashSet<long> EntityIds { get; } = new();
        private readonly Dictionary<ulong, SoakState> _observed = new();
        private Dictionary<ulong, PlayerState> _peers = new();
        private Task? _receive;
        private bool _closed;
        private ulong _lastOwnDeltaRevision;

        public Session(Counters counts, string control, Stopwatch clock, HashSet<long> retiredEntities)
        {
            _counts = counts;
            _control = control;
            _clock = clock;
            _retiredEntities = retiredEntities;
        }

        public async Task Connect(string host, int port)
        {
            _receive = Connection.RunAsync(host, port);
            await Until(() => Connection.IsConnected);
            await Connection.SendAsync(RoomSession.JoinFrame(_counts.Name));
            await Until(() => Room.HasSnapshot);
            Program.Check(Room.BaselineCount == 1 && _observed.ContainsKey(Room.EntityId), "Fresh baseline was not observed.");
            _counts.Connections++;
        }

        private PlayerState Own() => Room.Players.Single(player => player.EntityId == Room.EntityId);

        public void Poll()
        {
            Program.Check(!_clock.IsRunning || _clock.Elapsed.TotalSeconds <= SoakWorkload.Cycles(_counts.RequestedSeconds, false) * SoakWorkload.CycleSeconds + 15,
                "The workload exceeded its fixed duration and acknowledgement grace.");
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
                if (frame.MessageId == RoomSession.Delta)
                {
                    ObserveOperations(frame);
                }
                Program.Check(Room.Players.All(player => player.Name is "Alice" or "Bob" or "Churn"), "Soak runner requires a dedicated empty lobby.");
                foreach (PlayerState player in Room.Players)
                {
                    if (player.EntityId == Room.EntityId)
                    {
                        long root = checked((long)player.EntityId), equipment = player.Equipment.Single().EntityId;
                        Program.Check(!_retiredEntities.Contains(root) && !_retiredEntities.Contains(equipment), "Reconnect reused a root or equipment Entity GUID.");
                        EntityIds.Add(root);
                        EntityIds.Add(equipment);
                    }
                    // Residents validate each other's complete phase history. Churn validates its own fresh stream;
                    // its join may legitimately reach residents after its first publication has coalesced.
                    if (player.EntityId != Room.EntityId && (_counts.Role != "resident" || player.Name == "Churn"))
                    {
                        continue;
                    }
                    if (_observed.TryGetValue(player.EntityId, out SoakState? state))
                    {
                        state.Observe(player);
                    }
                    else
                    {
                        _observed.Add(player.EntityId, new SoakState(player));
                    }
                }
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

        private void ObserveOperations(Frame frame)
        {
            room_delta envelope = ProtobufMessages.Parse(frame.Body, room_delta.Parser, room_delta.Descriptor);
            foreach (nested_delta change in envelope.Changed)
            {
                if (change.Root.EntityId != checked((long)Room.EntityId) || change.Revision <= _lastOwnDeltaRevision)
                {
                    continue;
                }
                _lastOwnDeltaRevision = change.Revision;
                foreach (table_delta table in change.Root.Tables.Where(table => table.Index == 200))
                {
                    _counts.RowRemovals += table.Rows.Count(row => row.Number == 1 && row.Incarnation == 0);
                    _counts.RowReuses += table.Rows.Count(row => row.Number == 1 && row.RequiredIncarnation == 0 && row.Incarnation != 0);
                }
                foreach (container_delta container in change.Root.Containers.Where(container => container.Index == 300))
                {
                    if (container.UpdateCase == container_delta.UpdateOneofCase.Replacement)
                    {
                        _counts.ContainerReplacements++;
                    }
                    else if (container.UpdateCase == container_delta.UpdateOneofCase.Patch &&
                        container.Patch.Changed.Any(child => child.Slot == 1 && child.Change.Fields.Any(field => field.Index == 2)))
                    {
                        _counts.ChildPatches++;
                    }
                }
            }
        }

        public async Task Until(Func<bool> predicate)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed <= TimeSpan.FromSeconds(8))
            {
                Poll();
                if (predicate())
                {
                    return;
                }
                await Task.Delay(5);
            }
            throw new TimeoutException(_counts.Name + ": activity or coordination acknowledgement timed out.");
        }

        public async Task Activity(int tick, int residentStep)
        {
            double x = 80 + tick * 17 % 800, y = 60 + tick * 11 % 420;
            await Connection.SendAsync(Room.MoveFrame(x, y));
            uint moveSequence = Room.LastProposedMove!.Sequence;
            int phase = tick % SoakWorkload.CycleLength;
            bool expectedChildPatch = phase is 9 or 11 && Own().Equipment.Single().Power < 100;
            await Connection.SendAsync(Room.ActionFrame(SoakWorkload.Kinds[phase]));
            uint actionSequence = Room.LastProposedAction!.Sequence;
            uint nonce = (uint)tick + 1;
            await Connection.SendAsync(RoomSession.PingFrame(nonce));
            await Until(() => Own() is var player && player.Sequence == moveSequence && player.X == x && player.Y == y &&
                player.ActionSequence == actionSequence && Room.PendingAction is null && Room.LastPong == nonce);
            if (_counts.Role == "resident")
            {
                string path = Path.Combine(_control, "progress-" + _counts.Name + ".json");
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Progress(residentStep, Room.EntityId)));
                File.Move(path + ".tmp", path, true);
                string other = _counts.Name == "Alice" ? "Bob" : "Alice";
                await Until(() =>
                {
                    string peerPath = Path.Combine(_control, "progress-" + other + ".json");
                    if (!File.Exists(peerPath))
                    {
                        return false;
                    }
                    Progress progress = JsonSerializer.Deserialize<Progress>(File.ReadAllText(peerPath))!;
                    return progress.Step >= residentStep && _observed.TryGetValue(progress.EntityId, out SoakState? state) &&
                        state.Current.ActionSequence >= residentStep && state.Current.Sequence >= residentStep;
                });
                _counts.ResidentPeerSteps++;
            }
            _counts.Moves++;
            _counts.Pongs++;
            if (expectedChildPatch)
            {
                _counts.ExpectedChildPatches++;
            }
            switch (phase)
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
                case 4 or 5:
                    _counts.PotionUses++;
                    break;
                case 6 or 7:
                    _counts.Pickups++;
                    break;
                case 8 or 10:
                    _counts.Equips++;
                    break;
                case 9 or 11:
                    _counts.Upgrades++;
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
            Console.Error.WriteLine("Usage: --soak-client host port Alice|Bob|Churn resident|churn seconds absolute-local-control-directory");
            return 2;
        }
        string host = args[1], name = args[3], role = args[4], control = args[6];
        SoakWorkload.RequireLocalOutput(control);
        int cycles = SoakWorkload.Cycles(seconds, role == "churn");
        var counts = new Counters { Name = name, Role = role, RequestedSeconds = seconds, PlannedCycles = cycles };
        var elapsed = new Stopwatch();
        var retiredEntities = new HashSet<long>();
        Session? session = null;
        try
        {
            session = new Session(counts, control, elapsed, retiredEntities);
            await session.Connect(host, port);
            await session.Until(() => session.Room.Players.Count == 3);
            File.WriteAllText(Path.Combine(control, "ready-" + name), "ready\n");
            await session.Until(() => File.Exists(Path.Combine(control, "begin")));
            elapsed.Start();
            int tick = 0, nextProgress = 30;
            double nextTick = 0;
            var identities = new HashSet<(string?, ulong)> { (session.Room.PlayerId, session.Room.EntityId) };
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                for (int phase = 0; phase < SoakWorkload.CycleLength; phase++)
                {
                    await session!.Until(() => elapsed.Elapsed.TotalMilliseconds >= nextTick);
                    await session!.Activity(tick++, cycle * SoakWorkload.CycleLength + phase + 1);
                    // Use absolute slots so Task.Delay rounding does not accumulate over a long run.
                    // A late acknowledgement starts a new slot rather than producing catch-up sends.
                    nextTick = Math.Max(nextTick + SoakWorkload.TickMilliseconds, elapsed.Elapsed.TotalMilliseconds);
                }
                await session!.Until(() => elapsed.Elapsed.TotalMilliseconds >= nextTick);
                counts.CompletedCycles++;
                if (elapsed.Elapsed.TotalSeconds >= nextProgress)
                {
                    Console.WriteLine($"SOAK PROGRESS {name} elapsed={elapsed.Elapsed.TotalSeconds:F1} cycles={counts.CompletedCycles}/{cycles} connections={counts.Connections} moves={counts.Moves} peer_joins={counts.PeerJoins} peer_removals={counts.PeerRemovals}");
                    nextProgress += 30;
                }
                if (role == "churn" && (cycle + 1) % 2 == 0 && cycle + 1 < cycles)
                {
                    await session!.Close();
                    retiredEntities.UnionWith(session.EntityIds);
                    session = null;
                    await Task.Delay(250);
                    session = new Session(counts, control, elapsed, retiredEntities);
                    await session.Connect(host, port);
                    Program.Check(identities.All(id => id.Item1 != session.Room.PlayerId && id.Item2 != session.Room.EntityId), "Reconnect reused a guest identity.");
                    identities.Add((session.Room.PlayerId, session.Room.EntityId));
                    counts.FreshReconnects++;
                    tick = 0;
                    nextTick = elapsed.Elapsed.TotalMilliseconds;
                }
            }
            if (role == "resident")
            {
                await session!.Until(() => File.Exists(Path.Combine(control, "done-Churn")) && session.Room.Players.Count == 2);
                Program.Check(counts.Connections == 1 && counts.PeerMoves > 0 && counts.PeerActions > 0, "Resident did not observe peer activity.");
                File.WriteAllText(Path.Combine(control, "settled-" + name), "settled\n");
                await session.Until(() => File.Exists(Path.Combine(control, "settled-Alice")) && File.Exists(Path.Combine(control, "settled-Bob")));
            }
            Program.Check(counts.MaxPlayers == 3 && counts.CompletedCycles == cycles, "Incomplete three-client nested workload.");
        }
        catch (Exception error) { counts.Failures++; Console.Error.WriteLine(error); }
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
