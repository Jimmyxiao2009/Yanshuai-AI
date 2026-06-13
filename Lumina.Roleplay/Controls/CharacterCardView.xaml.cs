using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using yanshuai.ViewModels;

namespace yanshuai.Controls
{
    /// <summary>可复用角色卡视图。DataContext = CardItemViewModel。</summary>
    public sealed partial class CharacterCardView : UserControl
    {
        public CharacterCardView()
        {
            InitializeComponent();
            Loaded += (s, e) => (DataContext as CardItemViewModel)?.EnsureAvatar();
            // 虚拟化回收时 DataContext 变化，重新触发对应卡片的头像加载
            DataContextChanged += (s, e) => (DataContext as CardItemViewModel)?.EnsureAvatar();
        }
    }
}
