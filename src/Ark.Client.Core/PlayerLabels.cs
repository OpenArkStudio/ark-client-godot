using System.Globalization;

namespace Ark.Client.Core;

public readonly record struct LabelRectangle(float X, float Y, float Width, float Height)
{
    public bool Intersects(LabelRectangle other) =>
        X < other.X + other.Width + 2 && X + Width + 2 > other.X &&
        Y < other.Y + other.Height + 2 && Y + Height + 2 > other.Y;
}

public sealed record PlayerLabel(string PlayerId, string Text, LabelRectangle Bounds);

public static class PlayerLabels
{
    public const float MaximumWidth = 160;
    private const float Padding = 4;

    public static string FitText(string text, float maximumWidth, Func<string, float> measure)
    {
        if (measure(text) <= maximumWidth)
        {
            return text;
        }

        const string ellipsis = "…";
        int[] elements = StringInfo.ParseCombiningCharacters(text);
        for (int length = elements.Length - 1; length >= 0; length--)
        {
            string shortened = text[..elements[length]] + ellipsis;
            if (measure(shortened) <= maximumWidth)
            {
                return shortened;
            }
        }
        return "";
    }

    public static IReadOnlyList<PlayerLabel> Arrange(IReadOnlyList<PlayerState> players,
        float lineHeight, Func<string, float> measure, float extraHeight = 0)
    {
        float height = lineHeight + extraHeight;
        float pitch = height + Padding;
        int rows = (int)((RoomSession.Height - 2 * Padding) / pitch);
        if (players.Count > 16 || !float.IsFinite(height) || lineHeight <= 0 || extraHeight < 0 || height > 48 || rows < 7)
        {
            throw new ArgumentException("Label layout requires at most 16 players and a panel height of at most 48 pixels.");
        }

        var labels = new List<PlayerLabel>(players.Count);
        foreach (PlayerState player in players)
        {
            string text = FitText(player.Name + " #" + player.Id, MaximumWidth, measure);
            float width = extraHeight > 0 ? MaximumWidth : measure(text);
            float preferredX = Math.Clamp(player.X + 16, Padding, RoomSession.Width - width - Padding);
            int preferredRow = Math.Clamp((int)((player.Y - height - 16 - Padding) / pitch), 0, rows - 1);
            LabelRectangle? chosen = null;
            // Try nearby rows first; five bounded fallback columns fit dense stat panels.
            for (int column = -1; column < 5 && chosen is null; column++)
            {
                float x = column < 0 ? preferredX : Padding + column * (MaximumWidth + 8);
                for (int distance = 0; distance < rows && chosen is null; distance++)
                {
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        int row = preferredRow + direction * distance;
                        if (row < 0 || row >= rows)
                        {
                            continue;
                        }
                        var candidate = new LabelRectangle(x, Padding + row * pitch, width, height);
                        if (!labels.Any(label => candidate.Intersects(label.Bounds)))
                        {
                            chosen = candidate;
                            break;
                        }
                    }
                }
            }
            if (chosen is null)
            {
                throw new InvalidOperationException("No bounded label slot is available.");
            }
            labels.Add(new PlayerLabel(player.Id, text, chosen.Value));
        }
        return labels;
    }
}
