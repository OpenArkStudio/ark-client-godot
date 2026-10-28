using Ark.Client.Core;
using Godot;

namespace Ark.Client;

public partial class GuestRoomWorld : Control
{
    private IReadOnlyList<DisplayPlayer> _players = Array.Empty<DisplayPlayer>();
    private string? _localId;
    private bool _rawMarkers;
    private double _now;
    private readonly Dictionary<string, (AttributeChange Change, double At)> _attributeFeedback = new(StringComparer.Ordinal);
    private Texture2D _floor = null!, _village = null!, _ninja = null!, _samurai = null!, _shadow = null!, _pot = null!, _crate = null!;
    private readonly StyleBoxFlat _nameplate = new()
    {
        BgColor = new Color(0.06f, 0.12f, 0.15f, 0.92f),
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4
    };

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        const string art = "res://Assets/NinjaAdventure/";
        _floor = GD.Load<Texture2D>(art + "map/tileset_floor.png");
        _village = GD.Load<Texture2D>(art + "map/tileset_village_abandoned.png");
        _ninja = GD.Load<Texture2D>(art + "character/ninja_blue/sprite.png");
        _samurai = GD.Load<Texture2D>(art + "character/samurai_blue/sprite.png");
        _shadow = GD.Load<Texture2D>(art + "character/Shadow.png");
        _pot = GD.Load<Texture2D>(art + "destroyable/pot.png");
        _crate = GD.Load<Texture2D>(art + "destroyable/crate.png");
    }

    public void Present(IReadOnlyList<DisplayPlayer> players, string? localId, bool rawMarkers, double now)
    {
        _players = players;
        _localId = localId;
        _rawMarkers = rawMarkers;
        _now = now;
        foreach (string expired in _attributeFeedback.Keys.Where(id => now - _attributeFeedback[id].At > 1.2 ||
            !players.Any(player => player.ServerState.Id == id)).ToArray())
        {
            _attributeFeedback.Remove(expired);
        }
        QueueRedraw();
    }

    public void ShowAttributeChanges(IReadOnlyList<AttributeChange> changes, double now)
    {
        foreach (AttributeChange change in changes)
        {
            _attributeFeedback[change.PlayerId] = (change, now);
        }
    }

    public void ClearAttributeFeedback() => _attributeFeedback.Clear();

    private static bool IsPath(int x, int y) => x >= 0 && x < 20 && y >= 0 && y < 12 &&
        ((x >= 1 && x <= 7 && y >= 1 && y <= 4) || (x >= 7 && x <= 13 && y >= 4 && y <= 9) ||
         (x >= 2 && x <= 16 && y >= 4 && y <= 5) || (x >= 14 && x <= 15 && y >= 1 && y <= 5));

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(960, 540)), new Color("74894b"));
        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Rect2 region = (x * 7 + y * 3) % 5 == 0 ? new Rect2(32, 192, 16, 16) : new Rect2(48, 176, 16, 16);
                if (IsPath(x, y))
                {
                    int column = !IsPath(x - 1, y) ? 0 : !IsPath(x + 1, y) ? 2 : 1;
                    int row = !IsPath(x, y - 1) ? 0 : !IsPath(x, y + 1) ? 2 : 1;
                    region = new Rect2(column * 16, 112 + row * 16, 16, 16);
                }
                DrawTextureRectRegion(_floor, new Rect2(x * 48, y * 48, 48, 48), region);
            }
        }
        // All scenery is decorative: the server still validates only the unchanged arena bounds.
        Scenery(new Rect2(0, 0, 64, 48), new Vector2(454, 8), 2);
        Scenery(new Rect2(176, 96, 80, 80), new Vector2(700, 32), 2);
        Scenery(new Rect2(0, 96, 64, 64), new Vector2(-18, 348), 3);
        Scenery(new Rect2(64, 96, 32, 32), new Vector2(836, 396), 3);
        Scenery(new Rect2(0, 96, 64, 64), new Vector2(-42, -66), 2);
        Scenery(new Rect2(112, 80, 32, 32), new Vector2(468, 310), 3);
        Scenery(new Rect2(0, 48, 16, 32), new Vector2(386, 24), 2);
        Scenery(new Rect2(0, 48, 16, 32), new Vector2(852, 264), 2);
        DrawTextureRect(_pot, new Rect2(310, 130, 42, 48), false);
        DrawTextureRect(_pot, new Rect2(682, 198, 42, 48), false);
        DrawTextureRect(_crate, new Rect2(846, 206, 42, 45), false);
        DrawTextureRect(_crate, new Rect2(198, 446, 42, 45), false);
        foreach (DisplayPlayer display in _players.OrderBy(player => player.Y))
        {
            DrawPlayer(display);
        }
        DrawNameplates();
        DrawAttributeFeedback();
        DrawRect(new Rect2(Vector2.Zero, new Vector2(960, 540)), new Color("3e5942"), false, 3);
    }

    private void Scenery(Rect2 region, Vector2 position, int scale) =>
        DrawTextureRectRegion(_village, new Rect2(position, region.Size * scale), region);

    private void DrawPlayer(DisplayPlayer display)
    {
        bool local = PlayerPresentation.RoleFor(display.ServerState.Id, _localId) == PlayerRole.Local;
        Vector2 position = new(display.X, display.Y);
        Color accent = local ? new Color("8ffff0") : new Color("ffe1a3");
        if (_rawMarkers)
        {
            Vector2 raw = new((float)display.ServerState.X, (float)display.ServerState.Y);
            DrawArc(raw, 23, 0, Mathf.Tau, 32, new Color("ffffff"), 1.5f, true);
            DrawLine(raw, position, new Color("ffffff"));
        }
        DrawTextureRect(_shadow, new Rect2(position + new Vector2(-24, -6), new Vector2(48, 24)), false);
        DrawSetTransform(position + new Vector2(0, 8), 0, new Vector2(1, 0.45f));
        DrawArc(Vector2.Zero, local ? 23 : 20, 0, Mathf.Tau, 32, accent, local ? 3 : 1, true);
        if (local)
        {
            DrawArc(Vector2.Zero, 29, 0, Mathf.Tau, 32, new Color("ffffff"), 1.5f, true);
        }
        DrawSetTransform(Vector2.Zero);
        int direction = Math.Abs(display.FacingX) > Math.Abs(display.FacingY)
            ? display.FacingX < 0 ? 2 : 3 : display.FacingY < 0 ? 1 : 0;
        int frame = display.IsWalking ? (int)display.WalkPhase : 0;
        DrawTextureRectRegion(local ? _ninja : _samurai, new Rect2(position + new Vector2(-24, -42), new Vector2(48, 48)),
            new Rect2(direction * 16, frame * 16, 16, 16));
        Vector2 marker = position + new Vector2(0, -51);
        if (local)
        {
            DrawColoredPolygon(new[] { marker + new Vector2(0, -6), marker + new Vector2(6, 0),
                marker + new Vector2(0, 6), marker + new Vector2(-6, 0) }, new Color("ffffff"));
            DrawCircle(marker, 2, accent.Darkened(0.55f));
        }
        else
        {
            DrawArc(marker, 5, 0, Mathf.Tau, 16, new Color("ffffff"), 2, true);
        }
    }

    private void DrawNameplates()
    {
        Font font = ThemeDB.FallbackFont;
        PlayerState[] visible = _players.Select(display => display.ServerState with
        {
            X = display.X,
            Y = display.Y - 44,
            Name = (PlayerPresentation.RoleFor(display.ServerState.Id, _localId) == PlayerRole.Local ? "YOU · " : "PLAYER · ") + display.ServerState.Name
        }).ToArray();
        float textHeight = font.GetHeight(13);
        IReadOnlyList<PlayerLabel> labels = PlayerLabels.Arrange(visible, textHeight,
            text => font.GetStringSize(text, HorizontalAlignment.Left, -1, 13).X, extraHeight: 28);
        foreach (PlayerLabel label in labels)
        {
            LabelRectangle bounds = label.Bounds;
            var rectangle = new Rect2(new Vector2(bounds.X - 4, bounds.Y - 2), new Vector2(bounds.Width + 8, bounds.Height + 4));
            DrawStyleBox(_nameplate, rectangle);
            DrawString(font, new Vector2(bounds.X, bounds.Y + font.GetAscent(13)), label.Text,
                HorizontalAlignment.Left, -1, 13, new Color("ffffff"));
            PlayerState player = _players.First(display => display.ServerState.Id == label.PlayerId).ServerState;
            DrawAttributeBar(new Rect2(bounds.X, bounds.Y + textHeight + 2, bounds.Width, 12), "HP", player.Hp, player.HpMax, new Color("c95c66"));
            DrawAttributeBar(new Rect2(bounds.X, bounds.Y + textHeight + 16, bounds.Width, 12), "MP", player.Mp, player.MpMax, new Color("4b89cc"));
        }
    }

    private void DrawAttributeBar(Rect2 bounds, string caption, uint value, uint maximum, Color color)
    {
        DrawRect(bounds, new Color("26313b"));
        DrawRect(new Rect2(bounds.Position, new Vector2(bounds.Size.X * (value / (float)maximum), bounds.Size.Y)), color);
        Font font = ThemeDB.FallbackFont;
        string text = $"{caption} {value}/{maximum}";
        float width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 10).X;
        DrawString(font, bounds.Position + new Vector2((bounds.Size.X - width) / 2, 10), text,
            HorizontalAlignment.Left, -1, 10, new Color("ffffff"));
    }

    private void DrawAttributeFeedback()
    {
        Font font = ThemeDB.FallbackFont;
        foreach (DisplayPlayer player in _players)
        {
            if (!_attributeFeedback.TryGetValue(player.ServerState.Id, out var feedback))
            {
                continue;
            }
            float rise = (float)(_now - feedback.At) * 24;
            string hp = feedback.Change.HpDelta == 0 ? "" : $"HP {feedback.Change.HpDelta:+0;-0}";
            string mp = feedback.Change.MpDelta == 0 ? "" : $"MP {feedback.Change.MpDelta:+0;-0}";
            DrawString(font, new Vector2(player.X + 28, player.Y - 20 - rise), string.Join("  ", new[] { hp, mp }.Where(value => value.Length > 0)),
                HorizontalAlignment.Left, -1, 14, new Color("ffffff"));
        }
    }
}
