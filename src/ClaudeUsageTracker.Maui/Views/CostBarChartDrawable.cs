using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class CostBarChartDrawable(
    IReadOnlyList<DailyUsage> data,
    string? unavailableMessage = null) : IDrawable
{
    private const float LeftPad    = 52f;
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
            string msg = unavailableMessage ?? "No data available";
            canvas.FontColor = Color.FromArgb("#6E6E6E");
            canvas.FontSize  = 13;
            canvas.DrawString(msg, rect.Left, rect.Top, w, h - BottomPad,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        float maxCost = (float)(data.Max(d => d.CostUsd) == 0 ? 1m : data.Max(d => d.CostUsd));

        // Y-axis gridlines + labels
        for (int j = 0; j <= YDivisions; j++)
        {
            float frac  = j / (float)YDivisions;
            float y     = TopPad + chartH * (1f - frac);
            float value = maxCost * frac;

            canvas.StrokeColor = Color.FromArgb("#E1E1E1");
            canvas.StrokeSize  = 1;
            canvas.DrawLine(LeftPad, y, w - RightPad, y);

            string label = value == 0 ? "$0" : value >= 1 ? $"${value:F2}" : $"${value:F3}";
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
            float frac = (float)(data[i].CostUsd / (decimal)maxCost);
            float barH = chartH * frac;
            float y    = TopPad + chartH - barH;

            canvas.FillColor = Color.FromArgb("#512BD4");
            if (barH > 0) canvas.FillRectangle(x, y, barWidth, barH);

            // Value label — only on bars wide enough to show text legibly
            if (data[i].CostUsd > 0 && barWidth >= 22)
            {
                string valLabel = data[i].CostUsd < 0.001m ? "<$0.001"
                    : data[i].CostUsd < 0.01m ? $"${data[i].CostUsd:F3}"
                    : $"${data[i].CostUsd:F2}";
                bool insideBar  = barH >= 14;
                canvas.FontColor = insideBar ? Colors.White : Color.FromArgb("#512BD4");
                canvas.FontSize  = 10;
                float labelY = insideBar ? y + 2 : y - 13;
                canvas.DrawString(valLabel, x, labelY, barWidth, 12,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            // X-axis date label (every 5th, plus last)
            bool showDate = data.Count <= 7
                || (data.Count <= 31 && i % 5 == 0)
                || i == data.Count - 1;
            if (showDate)
            {
                canvas.FontColor = Color.FromArgb("#6E6E6E");
                canvas.FontSize  = 9;
                string dateLabel = data.Count <= 7
                    ? data[i].Date.ToLocalTime().ToString("ddd")
                    : data[i].Date.ToLocalTime().ToString("M/d");
                canvas.DrawString(dateLabel, x, h - BottomPad + 4, barWidth, BottomPad - 4,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        // Axis lines
        canvas.StrokeColor = Color.FromArgb("#ACACAC");
        canvas.StrokeSize  = 1;
        canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);
        canvas.DrawLine(LeftPad, TopPad, LeftPad, h - BottomPad);
    }
}
