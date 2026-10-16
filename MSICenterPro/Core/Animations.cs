using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MSICenterPro.Core;

/// <summary>
/// Non-linear entrance animations: staggered fade + rise for tagged elements,
/// and page-level slide transitions.
/// </summary>
public static class EntranceAnim
{
    public const string TagName = "enter";

    public static void Play(DependencyObject root, double step = 0.05, double baseDelay = 0.03)
    {
        var targets = new List<FrameworkElement>();
        Collect(root, targets);
        for (int i = 0; i < targets.Count; i++)
        {
            Animate(targets[i], baseDelay + i * step);
        }
    }

    private static void Collect(DependencyObject node, List<FrameworkElement> targets)
    {
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement fe && (fe.Tag as string) == TagName)
            {
                targets.Add(fe);
            }
            Collect(child, targets);
        }
    }

    private static void Animate(FrameworkElement el, double delay)
    {
        el.Opacity = 0;
        var slide = new TranslateTransform(0, 20);
        var existing = el.RenderTransform;
        if (existing != null && existing != Transform.Identity)
        {
            var group = new TransformGroup();
            group.Children.Add(existing);
            group.Children.Add(slide);
            el.RenderTransform = group;
        }
        else
        {
            el.RenderTransform = slide;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(460))
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            EasingFunction = ease
        };
        var move = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(560))
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
        };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty, move);
    }

    /// <summary>Slide-in from the right + fade for page switches.</summary>
    public static void PlayPageIn(FrameworkElement page)
    {
        page.Opacity = 0;
        var slide = new TranslateTransform(30, 0);
        page.RenderTransform = slide;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var move = new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
        };
        page.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.XProperty, move);
    }
}
