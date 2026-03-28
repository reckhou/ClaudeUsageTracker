using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace ClaudeUsageTracker.Maui.Controls;

public class ColorProgressBar : Microsoft.Maui.Controls.ProgressBar
{
    public static readonly BindableProperty ProgressPercentProperty =
        BindableProperty.Create(nameof(ProgressPercent), typeof(double), typeof(ColorProgressBar),
            0.0, propertyChanged: OnProgressPercentChanged);

    public static readonly BindableProperty DisplayColorProperty =
        BindableProperty.Create(nameof(DisplayColor), typeof(Color), typeof(ColorProgressBar),
            Colors.Green, propertyChanged: OnDisplayColorChanged);

    public double ProgressPercent
    {
        get => (double)GetValue(ProgressPercentProperty);
        set => SetValue(ProgressPercentProperty, value);
    }

    /// <summary>Bind this to TextColor on labels that should match the progress bar color.</summary>
    public Color DisplayColor
    {
        get => (Color)GetValue(DisplayColorProperty);
        set => SetValue(DisplayColorProperty, value);
    }

    public ColorProgressBar()
    {
        HeightRequest = 8;
        UpdateColor(0);
    }

    private static void OnProgressPercentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ColorProgressBar bar)
        {
            var percent = (double)newValue;
            bar.Progress = percent / 100.0;
            bar.UpdateColor(percent);
        }
    }

    private static void OnDisplayColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ColorProgressBar bar)
            bar.ProgressColor = (Color)newValue;
    }

    private void UpdateColor(double percent)
    {
        // Smooth gradient: green (0%) → yellow (50%) → red (100%)
        Color color;
        if (percent <= 50)
        {
            // Green to yellow: interpolate R and G from 67,160,71 to 251,140,0
            var t = percent / 50.0;
            color = Interpolate(
                Color.FromArgb("#43A047"),  // Green
                Color.FromArgb("#FBBC05"),  // Yellow
                t);
        }
        else
        {
            // Yellow to red: interpolate R and G from 251,140,0 to 229,57,53
            var t = (percent - 50) / 50.0;
            color = Interpolate(
                Color.FromArgb("#FBBC05"),  // Yellow
                Color.FromArgb("#E53935"),  // Red
                t);
        }

        ProgressColor = color;
        SetValue(DisplayColorProperty, color);
    }

    private static Color Interpolate(Color from, Color to, double t)
    {
        var r = from.Red + (to.Red - from.Red) * t;
        var g = from.Green + (to.Green - from.Green) * t;
        var b = from.Blue + (to.Blue - from.Blue) * t;
        return new Color((float)r, (float)g, (float)b);
    }
}
