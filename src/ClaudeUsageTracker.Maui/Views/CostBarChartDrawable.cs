using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class CostBarChartDrawable(IReadOnlyList<DailyUsage> data) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (data.Count == 0) return;

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;
        float barMargin = 2f;
        float barWidth = (w / data.Count) - barMargin;
        float maxCost = (float)(data.Max(d => d.CostUsd) == 0 ? 1 : (double)data.Max(d => d.CostUsd));

        canvas.FillColor = Color.FromArgb("#512BD4");

        for (int i = 0; i < data.Count; i++)
        {
            float barH = h * (float)(data[i].CostUsd / (decimal)maxCost);
            float x = i * (barWidth + barMargin);
            float y = h - barH;
            canvas.FillRectangle(x, y, barWidth, barH);

            // Date labels
            if (data.Count <= 7)
            {
                // Weekly — show day name
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                canvas.DrawString(data[i].Date.ToLocalTime().ToString("ddd"),
                    x + barWidth / 2, h + 14, HorizontalAlignment.Center);
            }
            else if (i % 5 == 0)
            {
                // 30-day — show month/day every 5 bars
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                canvas.DrawString(data[i].Date.ToLocalTime().ToString("M/d"),
                    x + barWidth / 2, h + 14, HorizontalAlignment.Center);
            }
        }

        canvas.StrokeColor = Colors.Gray;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, h, w, h);
    }
}
