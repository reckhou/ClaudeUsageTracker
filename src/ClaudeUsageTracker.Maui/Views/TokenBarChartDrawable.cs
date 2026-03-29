using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class TokenBarChartDrawable(
    IReadOnlyList<TokenUsage> data,
    string timeRangeLabel,
    string? unavailableMessage = null) : IDrawable
{
    private const float LeftPad    = 60f;
    private const float BottomPad  = 28f;
    private const float TopPad     = 20f;
    private const float RightPad   = 8f;
    private const int   YDivisions = 4;

    public void Draw(ICanvas canvas, RectF rect)
    {
        float w      = rect.Width;
        float h      = rect.Height;
        float chartW = w - LeftPad - RightPad;
        float chartH = h - TopPad - BottomPad;

        // Faint baseline always visible
        canvas.StrokeColor = Color.FromArgb("#C8C8C8");
        canvas.StrokeSize  = 1;
        canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);

        // Unavailable / empty state
        if (unavailableMessage != null || data.Count == 0)
        {
            string msg = unavailableMessage ?? "No token data available";
            canvas.FontColor = Color.FromArgb("#6E6E6E");
            canvas.FontSize  = 13;
            canvas.DrawString(msg, rect.Left, rect.Top, w, h - BottomPad,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        long maxTokens = data.Max(d => d.Tokens) == 0 ? 1 : data.Max(d => d.Tokens);

        // Y-axis gridlines + labels
        for (int j = 0; j <= YDivisions; j++)
        {
            float frac  = j / (float)YDivisions;
            float y     = TopPad + chartH * (1f - frac);
            long  value = (long)(maxTokens * frac);

            canvas.StrokeColor = Color.FromArgb("#E1E1E1");
            canvas.StrokeSize  = 1;
            canvas.DrawLine(LeftPad, y, w - RightPad, y);

            string label = FormatTokens(value);
            canvas.FontColor = Color.FromArgb("#6E6E6E");
            canvas.FontSize  = 9;
            canvas.DrawString(label, 0, y - 6, LeftPad - 4, 14,
                HorizontalAlignment.Right, VerticalAlignment.Top);
        }

        // Bars
        float barSpacing = chartW / data.Count;
        float barWidth   = Math.Max(1, barSpacing - 2f);

        for (int i = 0; i < data.Count; i++)
        {
            float x    = LeftPad + i * barSpacing + (barSpacing - barWidth) / 2f;
            float frac = (float)data[i].Tokens / maxTokens;
            float barH = chartH * frac;
            float y    = TopPad + chartH - barH;

            canvas.FillColor = Color.FromArgb("#34C759");
            if (barH > 0) canvas.FillRectangle(x, y, barWidth, barH);

            // Value label — only on bars wide enough to show text legibly
            if (data[i].Tokens > 0 && barWidth >= 22)
            {
                string valLabel  = FormatTokens(data[i].Tokens);
                bool insideBar   = barH >= 14;
                canvas.FontColor = insideBar ? Colors.White : Color.FromArgb("#2A9E47");
                canvas.FontSize  = 10;
                float labelY = insideBar ? y + 2 : y - 13;
                canvas.DrawString(valLabel, x, labelY, barWidth, 12,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            // X-axis labels:
            //   24 bars  → every 3rd hour
            //   168 bars → every 24th slot (= once per day, label with day name)
            //   30 bars  → every 5th day
            bool showLabel;
            string xLabel;
            if (data.Count <= 24)
            {
                showLabel = i % 3 == 0 || i == data.Count - 1;
                xLabel    = data[i].Date.ToLocalTime().ToString("htt").ToLower();
            }
            else if (data.Count <= 168)
            {
                showLabel = i % 24 == 0 || i == data.Count - 1;
                xLabel    = data[i].Date.ToLocalTime().ToString("ddd");
            }
            else
            {
                showLabel = i % 5 == 0 || i == data.Count - 1;
                xLabel    = data[i].Date.ToLocalTime().ToString("M/d");
            }

            if (showLabel)
            {
                canvas.FontColor = Color.FromArgb("#6E6E6E");
                canvas.FontSize  = 9;
                canvas.DrawString(xLabel, x, h - BottomPad + 4, barWidth, BottomPad - 4,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        // Axis lines
        canvas.StrokeColor = Color.FromArgb("#ACACAC");
        canvas.StrokeSize  = 1;
        canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);
        canvas.DrawLine(LeftPad, TopPad, LeftPad, h - BottomPad);
    }

    private static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:F1}M",
        >= 1_000     => $"{tokens / 1_000.0:F0}K",
        _            => tokens.ToString()
    };
}
