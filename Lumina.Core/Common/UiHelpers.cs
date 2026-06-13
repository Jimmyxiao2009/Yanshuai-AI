using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace yanshuai
{
    // ══════════════════════════════════════════════════════════════════════════
    // UiHelpers — 共享的 UWP UI 小工具（Agent / Roleplay 共用）
    // 原本在两个项目的 MainPage 里各有一份相同实现，现下沉到 Lumina.Core。
    // ══════════════════════════════════════════════════════════════════════════
    internal static class UiHelpers
    {
        // Accent color: UISettings construction is expensive on W10M — cache it
        private static Color _accentColor;
        private static bool  _accentCached = false;

        public static Color GetAccentColor()
        {
            if (!_accentCached)
            {
                _accentColor  = new UISettings().GetColorValue(UIColorType.Accent);
                _accentCached = true;
            }
            return _accentColor;
        }

        // 在可视化树中递归查找第一个 ScrollViewer（ListView 虚拟化后取其内部滚动容器）
        public static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var r = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (r != null) return r;
            }
            return null;
        }
    }
}
