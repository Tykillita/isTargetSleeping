using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace IsTargetSleeping.UI;

public sealed class TeleprompterView : ScrollViewer
{
    private readonly TextBlock text = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 1.35 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime last;

    public static readonly DependencyProperty ScriptProperty = DependencyProperty.Register(nameof(Script), typeof(string), typeof(TeleprompterView), new PropertyMetadata("", UpdateText));
    public static readonly DependencyProperty ScrollSpeedProperty = DependencyProperty.Register(nameof(ScrollSpeed), typeof(double), typeof(TeleprompterView), new PropertyMetadata(25.0));
    public static readonly DependencyProperty RunningProperty = DependencyProperty.Register(nameof(Running), typeof(bool), typeof(TeleprompterView), new PropertyMetadata(false, UpdateRunning));
    public static readonly DependencyProperty ReducedMotionProperty = DependencyProperty.Register(nameof(ReducedMotion), typeof(bool), typeof(TeleprompterView), new PropertyMetadata(false));

    public string Script { get => (string)GetValue(ScriptProperty); set => SetValue(ScriptProperty, value); }
    public double ScrollSpeed { get => (double)GetValue(ScrollSpeedProperty); set => SetValue(ScrollSpeedProperty, value); }
    public bool Running { get => (bool)GetValue(RunningProperty); set => SetValue(RunningProperty, value); }
    public bool ReducedMotion { get => (bool)GetValue(ReducedMotionProperty); set => SetValue(ReducedMotionProperty, value); }

    public TeleprompterView()
    {
        Content = text;
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Focusable = false;
        timer.Tick += (_, _) =>
        {
            var now = DateTime.UtcNow;
            var dt = Math.Clamp((now - last).TotalSeconds, 0, .1);
            last = now;
            if (!ReducedMotion && ExtentHeight > ViewportHeight) ScrollToVerticalOffset(VerticalOffset + ScrollSpeed * dt);
            if (VerticalOffset >= ExtentHeight - ViewportHeight) ScrollToVerticalOffset(0);
        };
    }

    private static void UpdateText(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TeleprompterView)d;
        view.text.Text = (string)e.NewValue;
        view.ScrollToTop();
    }
    private static void UpdateRunning(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TeleprompterView)d;
        view.last = DateTime.UtcNow;
        if ((bool)e.NewValue) view.timer.Start(); else view.timer.Stop();
    }
}
