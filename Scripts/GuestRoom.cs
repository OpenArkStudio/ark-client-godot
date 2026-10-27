using Ark.Client.Core;
using Ark.Client.Testing;
using Godot;

namespace Ark.Client;

public partial class GuestRoom : Node2D
{
    private readonly RoomSession _room = new();
    private readonly SessionDiagnostics _diagnostics = new();
    private readonly PlayerPresentation _presentation = new();
    private readonly ConfirmedAttributes _confirmedAttributes = new();
    private readonly AttributeSmokeProgress _smokeAttributes = new();
    private int _smokeActionStage;
    private GuestRoomHud _hud = null!;
    private GuestRoomWorld _world = null!;
    private TcpConnection? _connection;
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private Label _status = null!;
    private LineEdit _host = null!, _port = null!, _name = null!;
    private string _message = "Enter a name and connect to the guest room.";
    private double _clock, _joinDeadline, _nextMove, _nextPing, _pongDeadline = double.PositiveInfinity, _smokeSuccessAt = double.PositiveInfinity, _retryAt = double.PositiveInfinity;
    private bool _automaticReconnect, _smoke, _smokeMoved, _smokeReconnected;
    private int _retries;
    private string? _oldId, _oldPeerId;
    private double _reconnectAt = double.PositiveInfinity;
    private uint _nonce;
    private double _nextHudUpdate;
    private const float Speed = 180;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GetWindow().MinSize = new Vector2I(960, 680);
        }
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string[] pair = arg[2..].Split('=', 2);
            _options[pair[0]] = pair.Length == 2 ? pair[1] : "true";
        }
        _smoke = _options.ContainsKey("smoke");
        BuildControls();
        GD.Print("CLIENT READY scene=GuestRoom language=C# protocol=ARK2");
        if (_smoke || _options.ContainsKey("connect"))
        {
            Connect();
        }
    }

    private string Option(string key, string fallback) => _options.TryGetValue(key, out string? value) ? value : fallback;
    private void BuildControls()
    {
        _world = new GuestRoomWorld
        {
            Position = new Vector2(80, 168),
            Size = new Vector2(960, 540),
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        AddChild(_world);
        _hud = new GuestRoomHud();
        AddChild(_hud);
        _hud.Initialize(Option("host", "127.0.0.1"), Option("port", "19100"), Option("name", "Guest"));
        _host = _hud.Host;
        _port = _hud.Port;
        _name = _hud.GuestName;
        _status = _hud.Status;
        _hud.ConnectRequested += () =>
        {
            _retries = 0;
            Connect();
        };
        _hud.DisconnectRequested += Disconnect;
        _hud.ActionRequested += kind =>
        {
            if (_connection?.IsConnected != true)
            {
                return;
            }
            try
            {
                if (TryPrepareSend(() => _room.ActionFrame(kind), out byte[]? frame))
                {
                    Send(frame!);
                }
                else
                {
                    SetStatus("Network busy; action not sent. Try again.");
                }
            }
            catch (InvalidOperationException error)
            {
                SetStatus(error.Message);
            }
        };
    }

    private void ResetObservations()
    {
        _diagnostics.Reset();
        _presentation.Reset();
        _confirmedAttributes.Reset();
        _world.ClearAttributeFeedback();
        _nonce = 0;
    }

    private void Connect()
    {
        try
        {
            RoomSession.ValidateName(_name.Text);
            if (!int.TryParse(_port.Text, out int port) || port is < 1 or > 65535)
            {
                throw new ArgumentException("Use a port from 1 to 65535.");
            }

            _connection?.Dispose();
            _room.Reset();
            ResetObservations();
            _connection = new TcpConnection();
            _automaticReconnect = true;
            _retryAt = double.PositiveInfinity;
            _joinDeadline = _clock + 8;
            _nextPing = _clock + 5;
            _pongDeadline = double.PositiveInfinity;
            _smokeMoved = false;
            SetStatus("Connecting to " + _host.Text + ":" + port + "…");
            _ = _connection.RunAsync(_host.Text, port);
        }
        catch (Exception error)
        {
            Fail(error.Message, retry: false);
        }
    }

    private void Disconnect()
    {
        _automaticReconnect = false;
        _connection?.Dispose();
        _connection = null;
        _room.Reset();
        ResetObservations();
        _retryAt = double.PositiveInfinity;
        SetStatus("Disconnected. Rejoin to create a fresh guest.");
    }

    private void SetStatus(string message)
    {
        _message = message;
        GD.Print(message);
    }

    private void Fail(string message, bool retry = true)
    {
        _connection?.Dispose();
        _connection = null;
        _room.Reset();
        ResetObservations();
        SetStatus(message);
        if (_smoke)
        {
            GD.PrintErr("SMOKE FAIL " + message);
            GetTree().Quit(1);
            return;
        }
        if (retry && _automaticReconnect && _retries < 5)
        {
            _retryAt = _clock + Math.Min(8, 1 << _retries++);
            _message += " Automatic retry " + _retries + "/5 pending.";
        }
        else
        {
            _retryAt = double.PositiveInfinity;
            _message += " Use Connect / Rejoin to try again.";
        }
    }

    private async void Send(byte[] frame)
    {
        TcpConnection? connection = _connection;
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.SendAsync(frame);
        }
        catch (Exception error)
        {
            // Complete on the Godot synchronization context; ignore an obsolete session.
            if (ReferenceEquals(connection, _connection))
            {
                Fail("Send failed: " + error.Message);
            }
        }
    }

    private bool TryPrepareSend(Func<byte[]> createFrame, out byte[]? frame)
    {
        TcpConnection? connection = _connection;
        return SendPreparation.TryCreate(connection?.IsConnected == true, connection?.IsSending == true, createFrame, out frame);
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock >= _retryAt)
        {
            _retryAt = double.PositiveInfinity;
            Connect();
        }
        TcpConnection? current = _connection;
        if (current is not null)
        {
            while (current.TryDequeue(out TransportEvent? item))
            {
                if (!ReferenceEquals(current, _connection))
                {
                    break;
                }

                try
                {
                    if (item!.Kind == "connected")
                    {
                        Send(RoomSession.JoinFrame(_name.Text));
                    }
                    else if (item.Kind == "closed")
                    {
                        Fail(item.Message ?? "Connection closed.");
                        break;
                    }
                    else if (item.Frame is not null)
                    {
                        bool hadSnapshot = _room.HasSnapshot;
                        string kind = _room.Apply(item.Frame);
                        if (kind == "accepted")
                        {
                            SetStatus("Joined lobby as " + _room.Name + " · ID " + _room.PlayerId);
                        }

                        if (kind == "snapshot")
                        {
                            if (_smoke)
                            {
                                _smokeAttributes.Observe(_room.Players);
                            }
                            _world.ShowAttributeChanges(_confirmedAttributes.Observe(_room.Players), _clock);
                            _diagnostics.SnapshotApplied();
                            if (!hadSnapshot)
                            {
                                _nextPing = Math.Min(_nextPing, _clock);
                            }
                            _joinDeadline = double.PositiveInfinity;
                            _retries = 0;
                            _message = "Lobby · " + _room.Players.Count + "/16 guests · your ID " + _room.PlayerId;
                        }
                        if (kind == "pong" && _diagnostics.MatchPong(_room.LastPong))
                        {
                            _pongDeadline = double.PositiveInfinity;
                        }

                        if (kind == "error")
                        {
                            Fail(_room.LastError ?? "Room request failed.", retry: false);
                            break;
                        }
                        if (kind == "action_error")
                        {
                            if (_smoke)
                            {
                                Fail("Unexpected smoke action rejection: " + _room.ActionFeedback, retry: false);
                                break;
                            }
                            SetStatus(_room.ActionFeedback ?? "Action rejected.");
                        }
                    }
                }
                catch (Exception error)
                {
                    Fail("Protocol error: " + error.Message);
                    break;
                }
            }
            if (_connection is not null && _clock > _joinDeadline)
            {
                Fail("Join timed out after 8 seconds.");
            }
        }
        if (_connection?.IsConnected == true && _room.HasSnapshot)
        {
            if (_clock > _pongDeadline)
            {
                Fail("Server heartbeat response timed out.");
                return;
            }
            if (_clock >= _nextPing)
            {
                if (TryPrepareSend(() =>
                {
                    byte[] ping = RoomSession.PingFrame(_nonce + 1);
                    _nextPing = _clock + 5;
                    _pongDeadline = _clock + 4;
                    _diagnostics.BeginPing(++_nonce);
                    return ping;
                }, out byte[]? frame))
                {
                    Send(frame!);
                }
            }
            if (_smoke)
            {
                Smoke();
            }
            else
            {
                MoveInput();
            }
        }
        if (_smoke && _clock >= _smokeSuccessAt)
        {
            GetTree().Quit(0);
        }

        if (_smoke && _clock > 25)
        {
            Fail("Two-peer integration timed out.", retry: false);
        }

        _presentation.Update(_room.Players, delta, _hud.SmoothingEnabled);
        _world.Present(_presentation.Players, _room.PlayerId, _hud.RawMarkersEnabled, _clock);
        if (_clock >= _nextHudUpdate)
        {
            _nextHudUpdate = _clock + 0.2;
            _status.Text = _message;
            _hud.Observe(_room, _connection?.Metrics ?? TransportMetrics.Empty, _diagnostics.Capture(),
                _connection is not null, _connection?.IsConnected == true);
        }
    }

    private void MoveInput()
    {
        if (_host.HasFocus() || _port.HasFocus() || _name.HasFocus())
        {
            return;
        }

        var direction = new Vector2(
            (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) -
            (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) -
            (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0));
        if (direction == Vector2.Zero || _clock < _nextMove)
        {
            return;
        }

        PlayerState? self = _room.Players.FirstOrDefault(player => player.Id == _room.PlayerId);
        if (self is null)
        {
            return;
        }

        Vector2 target = new Vector2(self.X, self.Y) + direction.Normalized() * Speed * 0.1f;
        if (TryPrepareSend(() => _room.MoveFrame(Math.Clamp(target.X, 0, RoomSession.Width), Math.Clamp(target.Y, 0, RoomSession.Height)), out byte[]? frame))
        {
            _nextMove = _clock + 0.1;
            Send(frame!);
        }
    }

    private void Smoke()
    {
        if (_connection?.IsConnected != true || _connection.IsSending)
        {
            return;
        }
        string peerName = Option("peer-name", _name.Text == "Alice" ? "Bob" : "Alice");
        PlayerState? peer = _room.Players.FirstOrDefault(player => player.Name == peerName);
        PlayerState? self = _room.Players.FirstOrDefault(player => player.Id == _room.PlayerId);
        float targetX = _name.Text == "Alice" ? 120 : 240;
        float targetY = _name.Text == "Alice" ? 100 : 180;
        if (self is null)
        {
            return;
        }
        _oldId ??= self.Id;
        if (peer is not null)
        {
            _oldPeerId ??= peer.Id;
        }
        bool actionsComplete = _oldPeerId is not null && _smokeAttributes.Completed(_oldId, _oldPeerId);
        if (peer is null && (!actionsComplete || _smokeReconnected))
        {
            return;
        }

        if (!_smokeMoved)
        {
            _smokeMoved = true;
            Send(_room.MoveFrame(targetX, targetY));
            GD.Print("SMOKE MOVE " + _name.Text + " id=" + self.Id);
        }
        bool ownMove = self.Sequence >= 1 && Math.Abs(self.X - targetX) < 0.01 && Math.Abs(self.Y - targetY) < 0.01;
        float peerX = peerName == "Alice" ? 120 : 240, peerY = peerName == "Alice" ? 100 : 180;
        bool peerMove = peer is not null && peer.Sequence >= 1 && Math.Abs(peer.X - peerX) < 0.01 && Math.Abs(peer.Y - peerY) < 0.01;
        if (!ownMove || (!peerMove && (!actionsComplete || _smokeReconnected)))
        {
            return;
        }

        if (!_smokeReconnected && !actionsComplete)
        {
            if (_oldPeerId is not null && _smokeActionStage < AttributeSmokeProgress.Kinds.Length && _room.PendingAction is null &&
                (_smokeActionStage == 0 || _smokeAttributes.SawBoth(_oldId, _oldPeerId, _smokeActionStage - 1)))
            {
                string action = AttributeSmokeProgress.Kinds[_smokeActionStage++];
                Send(_room.ActionFrame(action));
                GD.Print($"SMOKE ACTION {_name.Text} {action} id={self.Id}");
            }
            return;
        }

        if (!_smokeReconnected && double.IsPositiveInfinity(_reconnectAt))
        {
            _reconnectAt = _clock + (_name.Text == "Alice" ? 0.3 : 1.0);
            GD.Print($"SMOKE PEER ATTRIBUTES {_name.Text} own_id={_oldId} peer_id={_oldPeerId} damage=85/100 skill=85/80 rest=100/100 action_seq=3");
        }
        if (!_smokeReconnected && _clock >= _reconnectAt)
        {
            _smokeReconnected = true;
            GD.Print("SMOKE RECONNECT " + _name.Text + " old_id=" + _oldId);
            Connect();
            return;
        }
        SessionMetrics observed = _diagnostics.Capture();
        if (_smokeReconnected && peer is not null && self.Id != _oldId && peer.Id != _oldPeerId &&
            _smokeAttributes.SawFresh(self.Id) && _smokeAttributes.SawFresh(peer.Id) &&
            self.Hp == 100 && self.Mp == 100 && self.ActionSequence == 0 && peer.Hp == 100 && peer.Mp == 100 && peer.ActionSequence == 0 &&
            _room.PendingAction is null && _room.LastProposedAction is null &&
            observed.ApplicationRttMilliseconds is double rtt && _connection is not null)
        {
            if (double.IsPositiveInfinity(_smokeSuccessAt))
            {
                _smokeSuccessAt = _clock + 1;
                GD.Print("SMOKE PASS " + _name.Text + " old_id=" + _oldId + " new_id=" + self.Id + " peer_id=" + peer.Id + " seq=" + self.Sequence);
                GD.Print($"SMOKE ATTRIBUTES {_name.Text} own_hp=100 own_mp=100 own_action_seq=0 peer_hp=100 peer_mp=100 peer_action_seq=0 old_peer_actions_observed=3");
                TransportMetrics transport = _connection.Metrics;
                GD.Print(FormattableString.Invariant($"SMOKE METRICS {_name.Text} rtt_ms={rtt:F3} pong={observed.MatchedPongNonce} tx_frames={transport.TxFrames} tx_bytes={transport.TxBytes} rx_frames={transport.RxFrames} rx_bytes={transport.RxBytes}"));
            }
        }
    }

    public override void _ExitTree()
    {
        _automaticReconnect = false;
        TcpConnection? previous = _connection;
        _connection = null;
        previous?.Dispose();
    }
}
