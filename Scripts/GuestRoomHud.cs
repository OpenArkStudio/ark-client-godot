using Ark.Client.Core;
using Godot;

namespace Ark.Client;

public partial class GuestRoomHud : Control
{
    public LineEdit Host { get; private set; } = null!;
    public LineEdit Port { get; private set; } = null!;
    public LineEdit GuestName { get; private set; } = null!;
    public Label Status { get; private set; } = null!;
    public bool SmoothingEnabled => _smoothing.ButtonPressed;
    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    private Label _summary = null!, _details = null!, _roster = null!;
    private Button _connect = null!, _disconnect = null!;
    private CheckButton _smoothing = null!;
    private IReadOnlyList<PlayerState>? _lastRoster;

    public void Initialize(string host, string port, string name)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1384, 800);
        PanelContainer connection = Card(new Vector2(24, 16), new Vector2(1336, 136));
        var top = new VBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        AddMargin(connection, top);
        var title = new HBoxContainer();
        top.AddChild(title);
        title.AddChild(Text("ARK / GUEST ROOM", 25, expand: true));
        title.AddChild(Text("SERVER-AUTHORITATIVE  ·  ARK1 / TCP", 14));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        top.AddChild(row);
        Host = Field(host, "Host", 200);
        Port = Field(port, "Port", 90);
        GuestName = Field(name, "Guest name", 240);
        row.AddChild(Host);
        row.AddChild(Port);
        row.AddChild(GuestName);
        _connect = new Button { Text = "Connect / Rejoin", CustomMinimumSize = new Vector2(160, 36) };
        _connect.Pressed += () => ConnectRequested?.Invoke();
        row.AddChild(_connect);
        _disconnect = new Button { Text = "Disconnect", CustomMinimumSize = new Vector2(130, 36) };
        _disconnect.Pressed += () => DisconnectRequested?.Invoke();
        row.AddChild(_disconnect);
        Status = Text("Enter a name and connect.", 14, expand: true);
        top.AddChild(Status);

        PanelContainer sidebar = Card(new Vector2(1008, 168), new Vector2(352, 608));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddMargin(sidebar, scroll);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(content);
        content.AddChild(Text("ROOM / LIVE SYNC", 21));
        _summary = Text("Disconnected", 15, wrap: true);
        content.AddChild(_summary);
        _smoothing = new CheckButton { Text = "Visual smoothing", ButtonPressed = true };
        content.AddChild(_smoothing);
        content.AddChild(Text("Filled avatar: display position\nOutlined ring: raw server position\nSmoothing is visual easing, not prediction.", 13, wrap: true));
        content.AddChild(new HSeparator());
        var toggle = new Button { Text = "Show network details ▸", Alignment = HorizontalAlignment.Left };
        content.AddChild(toggle);
        _details = Text("", 14, wrap: true);
        _details.Visible = false;
        content.AddChild(_details);
        toggle.Pressed += () =>
        {
            _details.Visible = !_details.Visible;
            toggle.Text = _details.Visible ? "Hide network details ▾" : "Show network details ▸";
        };
        content.AddChild(Text("CLIENT proposes target + seq\nSERVER checks bounds + seq\nKERNEL stores accepted state\nSNAPSHOTS update both views", 13, wrap: true));
        content.AddChild(new HSeparator());
        content.AddChild(Text("ROOM ROSTER / SERVER VALUES", 15));
        _roster = Text("No guests yet.", 14, wrap: true);
        content.AddChild(_roster);
        Label controls = Text("WASD / arrows to move  ·  cyan = you  ·  amber = peers\nThe view follows server snapshots. Rejoin creates a fresh entity ID.", 14);
        controls.Position = new Vector2(24, 724);
        AddChild(controls);
    }

    public void Observe(RoomSession room, TransportMetrics transport, SessionMetrics session, bool connecting, bool connected)
    {
        string state = room.HasSnapshot ? "JOINED" : connected ? "JOINING" : connecting ? "CONNECTING" : "DISCONNECTED";
        _connect.Text = room.HasSnapshot ? "Rejoin as new guest" : "Connect / Rejoin";
        _disconnect.Disabled = !connecting;
        _summary.Text = $"{state}  ·  {room.Players.Count}/16 guests\n" +
            $"Entity ID: {room.PlayerId ?? "—"}\n" +
            $"App ping RTT: {Milliseconds(session.ApplicationRttMilliseconds)}\n" +
            $"Last room update age: {Milliseconds(session.SnapshotAgeMilliseconds)}";
        MoveProposal? proposal = room.LastProposedMove;
        PlayerState? confirmed = room.Players.FirstOrDefault(player => player.Id == room.PlayerId);
        string outgoing = proposal is null ? "—" : FormattableString.Invariant($"seq {proposal.Sequence} → ({proposal.X:F1}, {proposal.Y:F1})");
        string authoritative = confirmed is null ? "—" : FormattableString.Invariant($"seq {confirmed.Sequence} @ ({confirmed.X:F1}, {confirmed.Y:F1})");
        _details.Text = $"TCP peer: {transport.PeerEndpoint ?? "—"}\nProtocol: ARK1 / UTF-8 JSON\n\n" +
            $"Move proposal:\n{outgoing}\nServer-confirmed:\n{authoritative}\n\n" +
            $"Ping pending: {session.PendingPingNonce?.ToString() ?? "—"}\nMatched pong: {session.MatchedPongNonce?.ToString() ?? "—"}\n" +
            $"Tx completed frames: {transport.TxFrames}\nTx frame bytes: {transport.TxBytes}\n" +
            $"Rx decoded frames: {transport.RxFrames}\nRx stream bytes: {transport.RxBytes}\n\n" +
            "Tx means local write completion, not peer ACK. RTT includes client/server queues and owner-thread polling.";
        if (!ReferenceEquals(_lastRoster, room.Players))
        {
            _lastRoster = room.Players;
            _roster.Text = room.Players.Count == 0 ? "No guests yet." : string.Join("\n\n", room.Players.Select(player =>
            {
                string name = PlayerLabels.FitText(player.Name, 180,
                    value => ThemeDB.FallbackFont.GetStringSize(value, HorizontalAlignment.Left, -1, 14).X);
                string tag = player.Id == room.PlayerId ? "YOU" : "PEER";
                return FormattableString.Invariant($"{tag}  {name}  #{player.Id}\nseq {player.Sequence}  ({player.X:F1}, {player.Y:F1})");
            }));
        }
    }

    private static string Milliseconds(double? value) => value is double milliseconds
        ? FormattableString.Invariant($"{milliseconds:F1} ms") : "—";

    private PanelContainer Card(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer { Position = position, Size = size };
        var style = new StyleBoxFlat
        {
            BgColor = new Color("142237"),
            BorderColor = new Color("29435e"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);
        return panel;
    }

    private static void AddMargin(Control parent, Control child)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 16);
        }
        parent.AddChild(margin);
        margin.AddChild(child);
    }

    private static LineEdit Field(string value, string placeholder, float width) => new()
    {
        Text = value,
        PlaceholderText = placeholder,
        CustomMinimumSize = new Vector2(width, 36)
    };

    private static Label Text(string value, int fontSize, bool expand = false, bool wrap = false)
    {
        var label = new Label
        {
            Text = value,
            SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.Fill,
            AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off
        };
        if (wrap)
        {
            label.CustomMinimumSize = new Vector2(300, 0);
        }
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }
}
