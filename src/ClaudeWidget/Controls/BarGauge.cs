using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClaudeWidget.Controls;

/// <summary>
/// A small pill-shaped bar gauge: rounded track with a rounded fill.
///
/// Drawn in <see cref="OnRender"/> for the same reason the widget's other
/// visuals are — at this size a nested visual tree costs more than it buys, and
/// drawing by hand keeps the corner radius exactly at half the bar height so the
/// ends stay properly round at every scale.
/// </summary>
public sealed class BarGauge : FrameworkElement
{
    /// <summary>Target percentage, 0-100. Rendering follows via an animated shadow property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(BarGauge),
        new PropertyMetadata(0d, OnValueChanged));

    private static readonly DependencyProperty DisplayValueProperty = DependencyProperty.Register(
        nameof(DisplayValue), typeof(double), typeof(BarGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bar thickness. The pill radius is half this.</summary>
    public static readonly DependencyProperty BarHeightProperty = DependencyProperty.Register(
        nameof(BarHeight), typeof(double), typeof(BarGauge),
        new FrameworkPropertyMetadata(4.5d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(BarGauge),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(BarGauge),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private double DisplayValue
    {
        get => (double)GetValue(DisplayValueProperty);
        set => SetValue(DisplayValueProperty, value);
    }

    public double BarHeight
    {
        get => (double)GetValue(BarHeightProperty);
        set => SetValue(BarHeightProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush ValueBrush
    {
        get => (Brush)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var gauge = (BarGauge)d;
        var target = Math.Clamp((double)e.NewValue, 0, 100);

        gauge.BeginAnimation(DisplayValueProperty, new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = RenderSize.Width;
        var height = Math.Min(BarHeight, RenderSize.Height);
        if (width <= 0 || height <= 0) return;

        var top = (RenderSize.Height - height) / 2;
        var radius = height / 2;

        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, top, width, height), radius, radius);

        var percent = Math.Clamp(DisplayValue, 0, 100);
        if (percent <= 0.05) return;

        // Never draw a fill narrower than the pill is tall — below that the
        // rounded ends collapse into a sliver that reads as nothing at all.
        var fill = Math.Max(height, width * percent / 100d);

        dc.DrawRoundedRectangle(ValueBrush, null, new Rect(0, top, fill, height), radius, radius);
    }
}
