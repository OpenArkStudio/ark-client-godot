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
    public bool RawMarkersEnabled => _rawMarkers.ButtonPressed;
    public event Action? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action<string>? ActionRequested;
    private Label _identity = null!, _summary = null!, _details = null!, _roster = null!, _fullRoster = null!;
    private Button _connect = null!, _disconnect = null!;
    private CheckButton _smoothing = null!, _rawMarkers = null!;
    private PanelContainer _debugPanel = null!;
    private IReadOnlyList<PlayerState>? _lastRoster;
    private Label _attributes = null!, _actionFeedback = null!;
    private readonly List<Button> _actionButtons = new();

    public void Initialize(string host, string port, string name)
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(1120, 824);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 18);
        AddMargin(Card(new Vector2(24, 16), new Vector2(1072, 64)), header, 12);
        var brand = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        brand.AddThemeConstantOverride("separation", 0);
        Label title = Text("ARK COMMONS", 22);
        title.AddThemeColorOverride("font_color", new Color("ffdda4"));
        brand.AddChild(title);
        _identity = Text("SHARED PLAZA / GUEST SESSION", 12);
        brand.AddChild(_identity);
        header.AddChild(brand);
        _summary = Text("OFFLINE", 14, expand: true);
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        _summary.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_summary);
        var network = new Button { Text = "NETWORK ▸", CustomMinimumSize = new Vector2(124, 36) };
        SkinButton(network);
        header.AddChild(network);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        AddMargin(Card(new Vector2(24, 88), new Vector2(1072, 64)), row, 12);
        Host = Field(host, "Host", 180);
        Port = Field(port, "Port", 76);
        GuestName = Field(name, "Guest name", 180);
        row.AddChild(Host);
        row.AddChild(Port);
        row.AddChild(GuestName);
        _connect = new Button { Text = "Connect", CustomMinimumSize = new Vector2(120, 36) };
        _connect.Pressed += () => ConnectRequested?.Invoke();
        SkinButton(_connect, true);
        row.AddChild(_connect);
        _disconnect = new Button { Text = "Leave", CustomMinimumSize = new Vector2(92, 36) };
        _disconnect.Pressed += () => DisconnectRequested?.Invoke();
        SkinButton(_disconnect);
        row.AddChild(_disconnect);
        Status = Text("Choose a name to join the plaza.", 12, expand: true);
        Status.ClipText = true;
        Status.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(Status);

        var business = new VBoxContainer();
        business.AddThemeConstantOverride("separation", 6);
        AddMargin(Card(new Vector2(24, 724), new Vector2(1072, 76)), business, 8);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        business.AddChild(actions);
        _attributes = Text("HP — / MP —", 14);
        _attributes.CustomMinimumSize = new Vector2(216, 32);
        _attributes.VerticalAlignment = VerticalAlignment.Center;
        actions.AddChild(_attributes);
        foreach ((string kind, string caption) in new[] { ("damage", "Take hit"), ("skill", "Use skill"), ("rest", "Rest") })
        {
            var action = new Button { Text = caption, CustomMinimumSize = new Vector2(100, 32), Disabled = true };
            SkinButton(action);
            action.Pressed += () => ActionRequested?.Invoke(kind);
            actions.AddChild(action);
            _actionButtons.Add(action);
        }
        _actionFeedback = Text("Server-owned attributes", 12, expand: true);
        _actionFeedback.ClipText = true;
        _actionFeedback.VerticalAlignment = VerticalAlignment.Center;
        actions.AddChild(_actionFeedback);
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 16);
        business.AddChild(footer);
        _roster = Text("No guests connected", 13, expand: true);
        _roster.ClipText = true;
        footer.AddChild(_roster);
        footer.AddChild(Text("W A S D  /  ARROWS  ·  EXPLORE THE PLAZA", 12));

        _debugPanel = Card(new Vector2(760, 160), new Vector2(336, 552));
        _debugPanel.Visible = false;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddMargin(_debugPanel, scroll, 12);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(288, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);
        content.AddChild(Text("NETWORK / AUTHORITATIVE STATE", 15));
        _smoothing = new CheckButton { Text = "Visual smoothing", ButtonPressed = true };
        _rawMarkers = new CheckButton { Text = "Raw server markers", ButtonPressed = false };
        content.AddChild(_smoothing);
        content.AddChild(_rawMarkers);
        content.AddChild(Text("Display easing only; no prediction.\nTarget + seq → validation → Kernel\nFull snapshots → both client views", 12, wrap: true));
        _details = Text("", 13, wrap: true);
        content.AddChild(_details);
        content.AddChild(new HSeparator());
        content.AddChild(Text("ROSTER / SERVER VALUES", 14));
        _fullRoster = Text("No guests yet.", 13, wrap: true);
        content.AddChild(_fullRoster);
        network.Pressed += () =>
        {
            _debugPanel.Visible = !_debugPanel.Visible;
            network.Text = _debugPanel.Visible ? "NETWORK ▾" : "NETWORK ▸";
        };
    }

    public void Observe(RoomSession room, TransportMetrics transport, SessionMetrics session, bool connecting, bool connected)
    {
        string state = room.HasSnapshot ? "JOINED" : connected ? "JOINING" : connecting ? "CONNECTING" : "OFFLINE";
        _connect.Text = room.HasSnapshot ? "Rejoin" : "Connect";
        _disconnect.Disabled = !connecting;
        _identity.Text = room.PlayerId is null ? "SHARED PLAZA / GUEST SESSION" : $"◆ YOU: {Fit(room.Name ?? "", 120)} · ID {room.PlayerId}";
        _identity.ClipText = true;
        _summary.Text = $"{state}  ·  {room.Players.Count}/16 ONLINE  ·  {Milliseconds(session.ApplicationRttMilliseconds)} APP RTT";
        MoveProposal? proposal = room.LastProposedMove;
        PlayerState? confirmed = room.Players.FirstOrDefault(player => player.Id == room.PlayerId);
        string outgoing = proposal is null ? "—" : FormattableString.Invariant($"seq {proposal.Sequence} → ({proposal.X:F1}, {proposal.Y:F1})");
        string authoritative = confirmed is null ? "—" : FormattableString.Invariant($"seq {confirmed.Sequence} @ ({confirmed.X:F1}, {confirmed.Y:F1})");
        _attributes.Text = confirmed is null ? "HP — / MP —" : $"HP {confirmed.Hp}/{confirmed.HpMax}   MP {confirmed.Mp}/{confirmed.MpMax}";
        _actionFeedback.Text = room.PendingAction is ActionProposal pending ? $"Awaiting action #{pending.Sequence}…" : room.ActionFeedback ?? "Server-owned attributes";
        foreach (Button button in _actionButtons)
        {
            button.Disabled = !room.HasSnapshot || !connected || room.PendingAction is not null;
        }
        _details.Text = $"TCP peer: {transport.PeerEndpoint ?? "—"}\nARK2 / UTF-8 JSON\nLast room update age: {Milliseconds(session.SnapshotAgeMilliseconds)}\n\n" +
            $"Move proposal:\n{outgoing}\nServer-confirmed:\n{authoritative}\n\n" +
            $"HP / MP: {(confirmed is null ? "—" : $"{confirmed.Hp}/{confirmed.HpMax} · {confirmed.Mp}/{confirmed.MpMax}")}\n" +
            $"Confirmed action_seq: {confirmed?.ActionSequence.ToString() ?? "—"}\n" +
            $"Action proposal: {room.LastProposedAction?.Sequence.ToString() ?? "—"} {room.LastProposedAction?.Kind ?? ""}\n" +
            $"Pending action: {room.PendingAction?.Sequence.ToString() ?? "—"}\n\n" +
            $"Ping pending: {session.PendingPingNonce?.ToString() ?? "—"}\nMatched pong: {session.MatchedPongNonce?.ToString() ?? "—"}\n" +
            $"Tx completed frames / bytes:\n{transport.TxFrames} / {transport.TxBytes}\n" +
            $"Rx decoded frames / stream bytes:\n{transport.RxFrames} / {transport.RxBytes}\n\n" +
            "Tx is local write completion, not peer ACK. App RTT includes queues and owner polling. Idle room update age does not indicate failure.";
        if (!ReferenceEquals(_lastRoster, room.Players))
        {
            _lastRoster = room.Players;
            _roster.Text = room.Players.Count == 0 ? "No guests connected" : string.Join("    ·    ", room.Players.Take(4).Select(player =>
                $"{(player.Id == room.PlayerId ? "◆ YOU" : "○ PLAYER")} {Fit(player.Name, 84)} #{player.Id}")) +
                (room.Players.Count > 4 ? $"    +{room.Players.Count - 4}" : "");
            _fullRoster.Text = room.Players.Count == 0 ? "No guests yet." : string.Join("\n\n", room.Players.Select(player =>
                FormattableString.Invariant($"{(player.Id == room.PlayerId ? "YOU" : "PLAYER")} {Fit(player.Name, 160)} #{player.Id}\nmove seq {player.Sequence}  ({player.X:F1}, {player.Y:F1})\nHP {player.Hp}/{player.HpMax} MP {player.Mp}/{player.MpMax}\naction_seq {player.ActionSequence}")));
        }
    }

    private static string Fit(string value, float width) => PlayerLabels.FitText(value, width,
        text => ThemeDB.FallbackFont.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X);
    private static string Milliseconds(double? value) => value is double milliseconds
        ? FormattableString.Invariant($"{milliseconds:F1} ms") : "—";

    private PanelContainer Card(Vector2 position, Vector2 size)
    {
        var panel = new PanelContainer { Position = position, Size = size };
        panel.AddThemeStyleboxOverride("panel", Box("172b35", "34515b", 10));
        AddChild(panel);
        return panel;
    }

    private static StyleBoxFlat Box(string fill, string border, int radius) => new()
    {
        BgColor = new Color(fill),
        BorderColor = new Color(border),
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius
    };

    private static void SkinButton(Button button, bool primary = false)
    {
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeStyleboxOverride("normal", Box(primary ? "317e69" : "223f4b", "50766e", 7));
        button.AddThemeStyleboxOverride("hover", Box(primary ? "419d82" : "305766", "81b5a1", 7));
        button.AddThemeStyleboxOverride("pressed", Box("193c3d", "b4d9bb", 7));
    }

    private static void AddMargin(Control parent, Control child, int padding)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, padding);
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
            label.CustomMinimumSize = new Vector2(288, 0);
        }
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }
}
