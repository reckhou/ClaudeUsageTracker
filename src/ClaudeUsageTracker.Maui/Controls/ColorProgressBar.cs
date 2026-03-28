using Microsoft.Maui.Controls;

namespace ClaudeUsageTracker.Maui.Controls;

public class ColorProgressBar : Microsoft.Maui.Controls.ProgressBar
{
    public static readonly BindableProperty ProgressPercentProperty =
        BindableProperty.Create(nameof(ProgressPercent), typeof(double), typeof(ColorProgressBar),
            0.0, propertyChanged: OnProgressPercentChanged);

    public static readonly BindableProperty HighThresholdProperty =
        BindableProperty.Create(nameof(HighThreshold), typeof(double), typeof(ColorProgressBar), 80.0);

    public static readonly BindableProperty MediumThresholdProperty =
        BindableProperty.Create(nameof(MediumThreshold), typeof(double), typeof(ColorProgressBar), 50.0);

    public double ProgressPercent
    {
        get => (double)GetValue(ProgressPercentProperty);
        set => SetValue(ProgressPercentProperty, value);
    }

    public double HighThreshold
    {
        get => (double)GetValue(HighThresholdProperty);
        set => SetValue(HighThresholdProperty, value);
    }

    public double MediumThreshold
    {
        get => (double)GetValue(MediumThresholdProperty);
        set => SetValue(MediumThresholdProperty, value);
    }

    public ColorProgressBar()
    {
        HeightRequest = 8;
        UpdateColor(ProgressPercent);
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

    private void UpdateColor(double percent)
    {
        if (percent >= HighThreshold)
            ProgressColor = Color.FromArgb("#E53935"); // Red
        else if (percent >= MediumThreshold)
            ProgressColor = Color.FromArgb("#FB8C00"); // Orange/Yellow
        else
            ProgressColor = Color.FromArgb("#43A047"); // Green
    }
}
