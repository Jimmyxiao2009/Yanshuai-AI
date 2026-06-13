using System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace yanshuai
{
    /// <summary>
    /// 流式文本渐显（仿 ChatGPT）。绑定到「完整文本」，但每次变化只把<b>新追加的增量</b>
    /// 作为一个独立 Run 追加到 TextBlock，并对它做透明度 0→1 的淡入动画。
    /// 配合后台按 ~4 次/秒 推送，得到「一段段渐显」的观感——低频更新也不显得跳，
    /// 同时每次只触发一次布局（O(n)），而不是每 token 一次（O(n²)），消除长回复卡顿。
    /// 注意：仅用于流式期间的临时 TextBlock；流式结束后气泡会切到 MarkdownBlock 富文本视图。
    /// </summary>
    public static class StreamFade
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text", typeof(string), typeof(StreamFade),
                new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);
        public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);

        // 已渲染到 UI 的字符数，用于算增量（append-only 流式下即上次文本长度）
        private static readonly DependencyProperty RenderedLenProperty =
            DependencyProperty.RegisterAttached(
                "RenderedLen", typeof(int), typeof(StreamFade), new PropertyMetadata(0));

        private static readonly Duration FadeDuration = new Duration(TimeSpan.FromMilliseconds(300));

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is TextBlock tb)) return;
            string full = (e.NewValue as string) ?? "";
            int rendered = (int)tb.GetValue(RenderedLenProperty);

            // 取前景色（克隆出独立可动画 brush，避免影响已渲染文本的颜色）
            var scb = tb.Foreground as SolidColorBrush;

            // 不可见（已完成/被回收的气泡）：直接全量重建、不做动画，省开销也避免错位
            if (tb.Visibility != Visibility.Visible)
            {
                tb.Inlines.Clear();
                if (full.Length > 0)
                    tb.Inlines.Add(scb != null
                        ? new Run { Text = full, Foreground = new SolidColorBrush(scb.Color) }
                        : new Run { Text = full });
                tb.SetValue(RenderedLenProperty, full.Length);
                return;
            }

            // 可见：增量追加 + 淡入
            // 文本变短或换了消息（不再是末尾追加）→ 全量重建
            if (full.Length < rendered)
            {
                tb.Inlines.Clear();
                rendered = 0;
            }
            if (full.Length == rendered) return;

            string delta = full.Substring(rendered);
            tb.SetValue(RenderedLenProperty, full.Length);

            // 拿不到 SolidColorBrush 前景：退化为无动画的继承色 Run（极少见）
            if (scb == null)
            {
                tb.Inlines.Add(new Run { Text = delta });
                return;
            }

            var brush = new SolidColorBrush(scb.Color) { Opacity = 0 };
            tb.Inlines.Add(new Run { Text = delta, Foreground = brush });

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = FadeDuration,
                EnableDependentAnimation = true, // brush 子属性动画属 dependent
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(anim, brush);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
    }
}
