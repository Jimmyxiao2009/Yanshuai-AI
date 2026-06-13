using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace yanshuai
{
    public sealed partial class ColorPresetPanel : UserControl
    {
        public event EventHandler<string> ColorSelected;

        public ColorPresetPanel()
        {
            InitializeComponent();
        }

        public double Spacing
        {
            get => PresetStack.Margin.Left;
            set => PresetStack.Margin = new Thickness(value, 0, value, 10);
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                ColorSelected?.Invoke(this, hex);
            }
        }
    }
}
