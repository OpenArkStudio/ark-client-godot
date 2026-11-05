using Godot;

namespace Ark.Client;

/// <summary>Editable vector item silhouettes; labels carry the authoritative values.</summary>
public partial class GuestItemIcon : Control
{
    public uint ItemId
    {
        get; set;
    }

    public GuestItemIcon()
    {
        CustomMinimumSize = new Vector2(40, 40);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        DrawStyleBox(new StyleBoxFlat
        {
            BgColor = new Color("102129"),
            BorderColor = new Color("48636a"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        }, new Rect2(0, 0, 40, 40));
        if (ItemId == 1)
        {
            DrawRect(new Rect2(16, 7, 8, 8), new Color("d8bc8a"));
            DrawRect(new Rect2(17, 14, 6, 5), new Color("c6e5e8"));
            DrawCircle(new Vector2(20, 25), 10, new Color("bce3e7"));
            DrawCircle(new Vector2(20, 26), 7, new Color("f57478"));
            DrawLine(new Vector2(20, 21), new Vector2(20, 31), Colors.White, 2);
            DrawLine(new Vector2(15, 26), new Vector2(25, 26), Colors.White, 2);
        }
        else if (ItemId == 1001)
        {
            DrawLine(new Vector2(11, 32), new Vector2(29, 10), new Color("d6ecee"), 6);
            DrawLine(new Vector2(11, 32), new Vector2(17, 25), new Color("b78950"), 5);
            DrawLine(new Vector2(10, 20), new Vector2(23, 30), new Color("edc778"), 4);
        }
        else if (ItemId == 1002)
        {
            DrawLine(new Vector2(12, 33), new Vector2(25, 14), new Color("b98a58"), 5);
            DrawCircle(new Vector2(27, 11), 7, new Color("b4a2f8"));
            DrawCircle(new Vector2(27, 11), 3, new Color("e5e6ff"));
        }
    }
}
