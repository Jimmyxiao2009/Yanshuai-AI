using System;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace yanshuai
{
    // 心象（Mindscape）：每个角色一套专属"心象色"，给聊天屏做整屏微染。
    // 当前由角色身份稳定生成（同一角色恒定同色、彼此区分），始终在深/浅底色下都好看。
    // 升级路径：ComputeMindscapeColor 可改为从角色立绘/头像提取主色——调用点与微染层不变。
    public sealed partial class MainPage : Page
    {
        /// <summary>按当前会话的角色给聊天屏设置心象微染层颜色。每次加载/切换会话时调用。</summary>
        private void ApplyMindscapeTint()
        {
            try
            {
                if (MindscapeWash == null) return;

                CharacterCard chara = _conv != null ? DataManager.GetCharacterForConversation(_conv) : null;
                Color baseColor = ComputeMindscapeColor(chara);

                // 微染：低透明度叠加在中性页面底色上（深色底需要略强一点才看得出）。
                byte washAlpha = AppSettings.IsDark ? (byte)0x2A : (byte)0x1C;
                MindscapeWash.Background = new SolidColorBrush(
                    Color.FromArgb(washAlpha, baseColor.R, baseColor.G, baseColor.B));
            }
            catch { /* 微染纯装饰，失败不影响功能 */ }
        }

        /// <summary>角色 → 心象色。无角色时回落到主题默认强调色。</summary>
        private static Color ComputeMindscapeColor(CharacterCard chara)
        {
            if (chara == null)
                return AppSettings.IsDark ? Color.FromArgb(255, 0x9B, 0x9A, 0xE8)
                                          : Color.FromArgb(255, 0x5E, 0x5D, 0xB6);

            string seed = !string.IsNullOrEmpty(chara.Id) ? chara.Id
                        : (!string.IsNullOrEmpty(chara.Name) ? chara.Name : "default");
            double hue = StableHash(seed) % 360;
            // 收敛饱和/明度，保证两套底色下都协调、且不刺眼。
            double sat = 0.42;
            double lum = AppSettings.IsDark ? 0.68 : 0.55;
            return HslToColor(hue, sat, lum);
        }

        private static int StableHash(string s)
        {
            int h = 17;
            if (s != null)
                foreach (char c in s) h = unchecked(h * 31 + c);
            return h & 0x7fffffff;
        }

        private static Color HslToColor(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360;
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255,
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }
    }
}
