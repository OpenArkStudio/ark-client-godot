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
        float lineHeight, Func<string, float> measure)
    {
        float pitch = lineHeight + Padding;
        int rows = (int)((RoomSession.Height - 2 * Padding) / pitch);
        if (players.Count > 16 || !float.IsFinite(lineHeight) || lineHeight <= 0 || rows < 16)
        {
            throw new ArgumentException("Label layout requires at most 16 players and 16 available rows.");
        }

        var labels = new List<PlayerLabel>(players.Count);
        foreach (PlayerState player in players)
        {
            string text = FitText(player.Name + " #" + player.Id, MaximumWidth, measure);
            float width = measure(text);
            float preferredX = Math.Clamp(player.X + 16, Padding, RoomSession.Width - width - Padding);
            int preferredRow = Math.Clamp((int)((player.Y - lineHeight - 16 - Padding) / pitch), 0, rows - 1);
            LabelRectangle? chosen = null;
            // Rows are shared across at most 16 labels, so a free row always exists.
            for (int distance = 0; distance < rows && chosen is null; distance++)
            {
                for (int direction = -1; direction <= 1; direction += 2)
                {
                    int row = preferredRow + direction * distance;
                    if (row < 0 || row >= rows)
                    {
                        continue;
                    }
                    var candidate = new LabelRectangle(preferredX, Padding + row * pitch, width, lineHeight);
                    if (!labels.Any(label => candidate.Intersects(label.Bounds)))
                    {
                        chosen = candidate;
                        break;
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
