using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class TokenBarChartDrawable(IReadOnlyList<TokenUsage> data, string timeRangeLabel) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (data.Count == 0) return;

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;
        float barMargin = 2f;
        float barWidth = (w / data.Count) - barMargin;
        float maxTokens = (float)(data.Max(d => d.Tokens) == 0 ? 1 : (double)data.Max(d => d.Tokens));

        canvas.FillColor = Color.FromArgb("#34C759"); // Green

        for (int i = 0; i < data.Count; i++)
        {
            float barH = h * (float)(data[i].Tokens / (decimal)maxTokens);
            float x = i * (barWidth + barMargin);
            float y = h - barH;
            canvas.FillRectangle(x, y, barWidth, Math.Max(0, barH));

            // Date labels
            if (data.Count <= 24 && data.Count > 0)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                var label = data[i].Date.ToLocalTime().ToString("htt"); // "6AM"
                canvas.DrawString(label, x + barWidth / 2, h + 12, HorizontalAlignment.Center);
            }
            else if (data.Count <= 7)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                var label = data[i].Date.ToLocalTime().ToString("ddd"); // "Mon"
                canvas.DrawString(label, x + barWidth / 2, h + 12, HorizontalAlignment.Center);
            }
            else if (i % 5 == 0)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                var label = data[i].Date.ToLocalTime().ToString("M/d"); // "3/15"
                canvas.DrawString(label, x + barWidth / 2, h + 12, HorizontalAlignment.Center);
            }
        }

        canvas.StrokeColor = Colors.Gray;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, h, w, h);
    }
}
