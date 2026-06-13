using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class ProjectsListPage : Page
    {
        public ProjectsListPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            AppSettings.ApplyTheme(RootGrid, this);
            LoadProjects();
        }

        private void LoadProjects()
        {
            if (DataManager.Data.Projects == null)
                DataManager.Data.Projects = new List<Project>();

            var projects = DataManager.Data.Projects
                .OrderByDescending(p => p.UpdatedAt)
                .Select(p => new ProjectViewModel(p))
                .ToList();

            ProjectList.ItemsSource = projects;
            EmptyState.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MainPage));
        }

        private void NewProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            var project = new Project { Name = "新项目" };
            DataManager.Data.Projects.Add(project);
            _ = DataManager.SaveAsync();
            Frame.Navigate(typeof(ProjectSettingsPage), project.Id);
        }

        private void Project_Click(object sender, ItemClickEventArgs e)
        {
            var vm = e.ClickedItem as ProjectViewModel;
            if (vm != null)
                Frame.Navigate(typeof(ProjectSettingsPage), vm.Id);
        }
    }

    internal class ProjectViewModel
    {
        private readonly Project _p;
        public ProjectViewModel(Project p) { _p = p; }

        public string Id => _p.Id;
        public string Name => _p.Name;
        public string IconGlyph => string.IsNullOrEmpty(_p.IconGlyph) ? "\uE8B7" : _p.IconGlyph;
        public string Description => string.IsNullOrEmpty(_p.Description) ? "无描述" : _p.Description;
        public string UpdatedAtDisplay
        {
            get
            {
                var now = DateTime.Now;
                if (_p.UpdatedAt.Date == now.Date) return _p.UpdatedAt.ToString("HH:mm");
                if ((now - _p.UpdatedAt).TotalDays < 7) return _p.UpdatedAt.ToString("ddd HH:mm");
                return _p.UpdatedAt.ToString("MM-dd");
            }
        }
        public string ConvCountLabel
        {
            get
            {
                int count = _p.ConversationIds?.Count ?? 0;
                int memCount = _p.ProjectMemories?.Count ?? 0;
                int kbCount = _p.KnowledgeFiles?.Count ?? 0;
                return $"{count} 对话 · {kbCount} 知识文件 · {memCount} 记忆";
            }
        }
    }
}
