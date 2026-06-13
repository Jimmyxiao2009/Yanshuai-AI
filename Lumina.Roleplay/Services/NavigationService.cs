using System;
using Windows.UI.Xaml.Controls;

namespace yanshuai.Services
{
    /// <summary>
    /// 轻量导航服务：封装内容 Frame，取代散落各处的
    /// <c>ShellPage.Current.ContentFrame.Navigate(...)</c> 静态调用。
    ///
    /// 用法：Shell 在初始化时调用 <see cref="Register"/> 把内容 Frame 注册进来；
    /// 页面 / ViewModel 通过 <see cref="Instance"/> 导航。迁移期间 ShellPage.Current
    /// 仍保留作兼容垫片，二者指向同一个 Frame。
    /// </summary>
    public class NavigationService
    {
        public static NavigationService Instance { get; } = new NavigationService();

        private Frame _frame;

        /// <summary>当前内容 Frame（未注册时为 null）。</summary>
        public Frame Frame { get { return _frame; } }

        public void Register(Frame frame)
        {
            _frame = frame;
        }

        public bool CanGoBack { get { return _frame != null && _frame.CanGoBack; } }

        public bool Navigate(Type pageType)
        {
            return Navigate(pageType, null);
        }

        public bool Navigate(Type pageType, object parameter)
        {
            if (_frame == null || pageType == null) return false;
            return _frame.Navigate(pageType, parameter);
        }

        public void GoBack()
        {
            if (CanGoBack) _frame.GoBack();
        }
    }
}
