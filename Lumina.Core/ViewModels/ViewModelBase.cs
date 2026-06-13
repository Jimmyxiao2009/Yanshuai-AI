using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace yanshuai.ViewModels
{
    /// <summary>
    /// 轻量 ViewModel 基类：INotifyPropertyChanged + Set&lt;T&gt; 辅助。
    /// 不依赖任何 MVVM 框架，配合 yanshuai.Common.RelayCommand 使用。
    /// 设计目标是把页面巨型 code-behind 里的状态/命令迁出来，
    /// 而 AppState / AppSettings / DataManager 仍作为静态单例由 VM 包装访问。
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>设置字段并在值变化时触发 PropertyChanged。返回是否发生变化。</summary>
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
