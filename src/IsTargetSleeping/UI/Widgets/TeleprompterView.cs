using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IsTargetSleeping.UI;

public sealed class TeleprompterView : ScrollViewer
{
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium };
    private TimeSpan? lastFrame;
    private bool rendering;

    public static readonly DependencyProperty ScriptProperty = DependencyProperty.Register(nameof(Script), typeof(string), typeof(TeleprompterView), new PropertyMetadata("", UpdateText));
    public static readonly DependencyProperty ScrollSpeedProperty = DependencyProperty.Register(nameof(ScrollSpeed), typeof(double), typeof(TeleprompterView), new PropertyMetadata(25.0));
    public static readonly DependencyProperty RunningProperty = DependencyProperty.Register(nameof(Running), typeof(bool), typeof(TeleprompterView), new PropertyMetadata(false, UpdateRunning));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(TeleprompterView), new PropertyMetadata(false, UpdateMotion));
    public static readonly DependencyProperty ResetRequestProperty = DependencyProperty.Register(nameof(ResetRequest), typeof(int), typeof(TeleprompterView), new PropertyMetadata(0, Reset));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(TeleprompterView), new PropertyMetadata(0.0));

    public string Script { get => (string)GetValue(ScriptProperty); set => SetValue(ScriptProperty, value); }
    public double ScrollSpeed { get => (double)GetValue(ScrollSpeedProperty); set => SetValue(ScrollSpeedProperty, value); }
    public bool Running { get => (bool)GetValue(RunningProperty); set => SetValue(RunningProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }
    public int ResetRequest { get => (int)GetValue(ResetRequestProperty); set => SetValue(ResetRequestProperty, value); }
    public double Progress => (double)GetValue(ProgressProperty);

    public TeleprompterView()
    {
        Content = text;
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;
        ScrollChanged += (_, _) => { UpdateProgress(); UpdateMotion(); };
        SizeChanged += (_, _) => { UpdateProgress(); UpdateMotion(); };
        IsVisibleChanged += (_, _) => UpdateMotion();
        Loaded += (_, _) => { UpdateProgress(); UpdateMotion(); };
        Unloaded += (_, _) => StopRendering();
    }

    private static void UpdateText(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TeleprompterView)d;
        view.text.Text = (string)e.NewValue;
        view.ResetPosition();
        view.UpdateProgress();
        view.UpdateMotion();
    }
    private static void Reset(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TeleprompterView)d).ResetPosition();
    private static void UpdateMotion(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TeleprompterView)d).UpdateMotion();
    private static void UpdateRunning(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TeleprompterView)d;
        view.UpdateMotion();
    }

    private void ResetPosition()
    {
        ScrollToTop();
        lastFrame = null;
        UpdateProgress();
    }

    private void UpdateMotion()
    {
        bool shouldRender = IsLoaded && IsVisible && Running && !ReducedMotion && ExtentHeight > ViewportHeight;
        if (shouldRender == rendering) return;
        rendering = shouldRender;
        lastFrame = null;
        if (rendering) CompositionTarget.Rendering += OnRendering;
        else CompositionTarget.Rendering -= OnRendering;
    }

    private void StopRendering()
    {
        if (!rendering) return;
        CompositionTarget.Rendering -= OnRendering;
        rendering = false;
        lastFrame = null;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame) return;
        var now = frame.RenderingTime;
        if (lastFrame is not { } previous) { lastFrame = now; return; }
        var dt = Math.Clamp((now - previous).TotalSeconds, 0, 1.0 / 30);
        lastFrame = now;
        double end = ExtentHeight - ViewportHeight;
        if (end <= 0) { UpdateMotion(); return; }
        if (VerticalOffset + ScrollSpeed * dt >= end) ScrollToTop();
        else ScrollToVerticalOffset(VerticalOffset + ScrollSpeed * dt);
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        double end = ExtentHeight - ViewportHeight;
        SetCurrentValue(ProgressProperty, end > 0 ? Math.Clamp(VerticalOffset / end, 0, 1) : 0);
    }
}
