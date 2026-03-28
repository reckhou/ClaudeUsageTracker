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
        }

        // Draw zero line
        canvas.StrokeColor = Colors.Gray;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, h, w, h);
    }
}
