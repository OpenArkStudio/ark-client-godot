using Ark.Client.Core;
using Godot;

namespace Ark.Client;

public partial class GuestRoomWorld : Control
{
    private IReadOnlyList<DisplayPlayer> _players = Array.Empty<DisplayPlayer>();
    private string? _localId;

    public void Present(IReadOnlyList<DisplayPlayer> players, string? localId)
    {
        _players = players;
        _localId = localId;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(RoomSession.Width, RoomSession.Height)), new Color("101d2c"));
        for (int x = 0; x <= RoomSession.Width; x += 60)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, RoomSession.Height), new Color("1b3045"));
        }
        for (int y = 0; y <= RoomSession.Height; y += 60)
        {
            DrawLine(new Vector2(0, y), new Vector2(RoomSession.Width, y), new Color("1b3045"));
        }
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(18, 28), "LOBBY  /  960 × 540", HorizontalAlignment.Left, -1, 17, new Color("91a7bd"));
        foreach (DisplayPlayer display in _players)
        {
            Color color = display.ServerState.Id == _localId ? new Color("56e0c3") : new Color("ffbd69");
            Vector2 server = new(display.ServerState.X, display.ServerState.Y);
            Vector2 position = new(display.X, display.Y);
            DrawArc(server, 20, 0, Mathf.Tau, 32, new Color(color, 0.5f), 1.5f, true);
            DrawLine(server, position, new Color(color, 0.35f));
            DrawEllipseShadow(position);
            DrawRect(new Rect2(position + new Vector2(-9, -1), new Vector2(18, 16)), color);
            DrawCircle(position + new Vector2(0, -8), 10, color.Lightened(0.12f));
            DrawCircle(position + new Vector2(-3, -9), 1.5f, new Color("142237"));
            DrawCircle(position + new Vector2(3, -9), 1.5f, new Color("142237"));
            if (display.ServerState.Id == _localId)
            {
                DrawColoredPolygon(new[] { position + new Vector2(-5, -30), position + new Vector2(5, -30),
                    position + new Vector2(0, -24) }, color);
            }
        }
        PlayerState[] visible = _players.Select(display => display.ServerState with { X = display.X, Y = display.Y }).ToArray();
        IReadOnlyList<PlayerLabel> labels = PlayerLabels.Arrange(visible, font.GetHeight(16),
            text => font.GetStringSize(text, HorizontalAlignment.Left, -1, 16).X);
        foreach (PlayerLabel label in labels)
        {
            PlayerState player = visible.First(player => player.Id == label.PlayerId);
            LabelRectangle bounds = label.Bounds;
            DrawLine(new Vector2(player.X, player.Y), new Vector2(bounds.X, bounds.Y + bounds.Height / 2), new Color("66849c"));
        }
        foreach (PlayerLabel label in labels)
        {
            LabelRectangle bounds = label.Bounds;
            Color color = label.PlayerId == _localId ? new Color("56e0c3") : new Color("ffbd69");
            DrawRect(new Rect2(new Vector2(bounds.X, bounds.Y), new Vector2(bounds.Width, bounds.Height)), new Color("101d2c"));
            DrawString(font, new Vector2(bounds.X, bounds.Y + font.GetAscent(16)), label.Text,
                HorizontalAlignment.Left, -1, 16, color);
        }
        DrawRect(new Rect2(Vector2.Zero, new Vector2(RoomSession.Width, RoomSession.Height)), new Color("37506b"), false, 2);
    }

    private void DrawEllipseShadow(Vector2 position)
    {
        DrawSetTransform(position + new Vector2(0, 14), 0, new Vector2(1, 0.35f));
        DrawCircle(Vector2.Zero, 16, new Color(0, 0, 0, 0.35f));
        DrawSetTransform(Vector2.Zero);
    }
}
