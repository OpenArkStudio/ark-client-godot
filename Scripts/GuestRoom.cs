using Ark.Client.Core;
using Godot;

namespace Ark.Client;

public partial class GuestRoom : Node2D
{
    private readonly RoomSession _room = new();
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
    private const float Speed = 180;

    public override void _Ready()
    {
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
        GD.Print("CLIENT READY scene=GuestRoom language=C# protocol=ARK1");
        if (_smoke || _options.ContainsKey("connect"))
        {
            Connect();
        }
    }

    private string Option(string key, string fallback) => _options.TryGetValue(key, out string? value) ? value : fallback;
    private void BuildControls()
    {
        var title = new Label { Text = "ARK / GUEST ROOM", Position = new Vector2(24, 12) };
        title.AddThemeFontSizeOverride("font_size", 28);
        AddChild(title);
        var row = new HBoxContainer { Position = new Vector2(24, 52), Size = new Vector2(960, 36) };
        AddChild(row);
        _host = new LineEdit { Text = Option("host", "127.0.0.1"), PlaceholderText = "Host", CustomMinimumSize = new Vector2(180, 36) };
        _port = new LineEdit { Text = Option("port", "19100"), PlaceholderText = "Port", CustomMinimumSize = new Vector2(90, 36) };
        _name = new LineEdit { Text = Option("name", "Guest"), PlaceholderText = "Guest name", CustomMinimumSize = new Vector2(180, 36) };
        row.AddChild(_host);
        row.AddChild(_port);
        row.AddChild(_name);
        var connect = new Button { Text = "Connect / Rejoin" };
        connect.Pressed += () =>
        {
            _retries = 0;
            Connect();
        };
        row.AddChild(connect);
        var disconnect = new Button { Text = "Disconnect" };
        disconnect.Pressed += Disconnect;
        row.AddChild(disconnect);
        _status = new Label { Position = new Vector2(24, 664), Size = new Vector2(960, 44), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_status);
        AddChild(new Label { Text = "WASD / arrows to move · server snapshots determine positions · reconnect creates a fresh guest", Position = new Vector2(24, 94) });
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
                        string kind = _room.Apply(item.Frame);
                        if (kind == "accepted")
                        {
                            SetStatus("Joined lobby as " + _room.Name + " · ID " + _room.PlayerId);
                        }

                        if (kind == "snapshot")
                        {
                            _joinDeadline = double.PositiveInfinity;
                            _retries = 0;
                            _message = "Lobby · " + _room.Players.Count + "/16 guests · your ID " + _room.PlayerId;
                        }
                        if (kind == "pong" && _room.LastPong == _nonce)
                        {
                            _pongDeadline = double.PositiveInfinity;
                        }

                        if (kind == "error")
                        {
                            Fail(_room.LastError ?? "Room request failed.", retry: false);
                            break;
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
                _nextPing = _clock + 5;
                _pongDeadline = _clock + 4;
                Send(RoomSession.PingFrame(++_nonce));
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

        _status.Text = _message;
        QueueRedraw();
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

        _nextMove = _clock + 0.1;
        PlayerState? self = _room.Players.FirstOrDefault(player => player.Id == _room.PlayerId);
        if (self is null)
        {
            return;
        }

        Vector2 target = new Vector2(self.X, self.Y) + direction.Normalized() * Speed * 0.1f;
        Send(_room.MoveFrame(Math.Clamp(target.X, 0, RoomSession.Width), Math.Clamp(target.Y, 0, RoomSession.Height)));
    }

    private void Smoke()
    {
        string peerName = Option("peer-name", _name.Text == "Alice" ? "Bob" : "Alice");
        PlayerState? peer = _room.Players.FirstOrDefault(player => player.Name == peerName);
        PlayerState? self = _room.Players.FirstOrDefault(player => player.Id == _room.PlayerId);
        float targetX = _name.Text == "Alice" ? 120 : 240;
        float targetY = _name.Text == "Alice" ? 100 : 180;
        if (peer is null || self is null)
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
        bool peerMove = peer.Sequence >= 1 && Math.Abs(peer.X - peerX) < 0.01 && Math.Abs(peer.Y - peerY) < 0.01;
        if (!ownMove || !peerMove)
        {
            return;
        }

        if (!_smokeReconnected && double.IsPositiveInfinity(_reconnectAt))
        {
            _oldId = self.Id;
            _oldPeerId = peer.Id;
            _reconnectAt = _clock + (_name.Text == "Alice" ? 0.3 : 1.0);
        }
        if (!_smokeReconnected && _clock >= _reconnectAt)
        {
            _smokeReconnected = true;
            GD.Print("SMOKE RECONNECT " + _name.Text + " old_id=" + _oldId);
            Connect();
            return;
        }
        if (_smokeReconnected && self.Id != _oldId && peer.Id != _oldPeerId)
        {
            if (double.IsPositiveInfinity(_smokeSuccessAt))
            {
                _smokeSuccessAt = _clock + 1;
                GD.Print("SMOKE PASS " + _name.Text + " old_id=" + _oldId + " new_id=" + self.Id + " peer_id=" + peer.Id + " seq=" + self.Sequence);
            }
        }
    }

    public override void _Draw()
    {
        var origin = new Vector2(24, 118);
        DrawRect(new Rect2(origin, new Vector2(RoomSession.Width, RoomSession.Height)), new Color("14263d"));
        for (int x = 0; x <= RoomSession.Width; x += 60)
        {
            DrawLine(origin + new Vector2(x, 0), origin + new Vector2(x, RoomSession.Height), new Color("1c344d"));
        }

        for (int y = 0; y <= RoomSession.Height; y += 60)
        {
            DrawLine(origin + new Vector2(0, y), origin + new Vector2(RoomSession.Width, y), new Color("1c344d"));
        }

        foreach (PlayerState player in _room.Players)
        {
            bool self = player.Id == _room.PlayerId;
            Vector2 position = origin + new Vector2(player.X, player.Y);
            Color color = self ? new Color("56e0c3") : new Color("ffbd69");
            DrawCircle(position, 12, color);
        }

        Font font = ThemeDB.FallbackFont;
        float lineHeight = font.GetHeight(16);
        IReadOnlyList<PlayerLabel> labels = PlayerLabels.Arrange(_room.Players, lineHeight,
            text => font.GetStringSize(text, HorizontalAlignment.Left, -1, 16).X);
        foreach (PlayerLabel label in labels)
        {
            PlayerState player = _room.Players.First(player => player.Id == label.PlayerId);
            Color color = player.Id == _room.PlayerId ? new Color("56e0c3") : new Color("ffbd69");
            LabelRectangle bounds = label.Bounds;
            Vector2 anchor = origin + new Vector2(bounds.X, bounds.Y + bounds.Height / 2);
            DrawLine(origin + new Vector2(player.X, player.Y), anchor, new Color(color, 0.45f));
        }
        foreach (PlayerLabel label in labels)
        {
            Color color = label.PlayerId == _room.PlayerId ? new Color("56e0c3") : new Color("ffbd69");
            LabelRectangle bounds = label.Bounds;
            DrawRect(new Rect2(origin + new Vector2(bounds.X, bounds.Y), new Vector2(bounds.Width, bounds.Height)),
                new Color("14263d"));
            DrawString(font, origin + new Vector2(bounds.X, bounds.Y + font.GetAscent(16)), label.Text,
                HorizontalAlignment.Left, -1, 16, color);
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
