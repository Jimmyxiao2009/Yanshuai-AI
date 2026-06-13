using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace yanshuai
{
    public sealed partial class MainPage : Page
    {
        private void UpdateTopBar()
        {
            var shell = ShellPage.Current;
            if (shell == null) return;

            if (_topBarContainer == null)
            {
                _topBarContainer = new Grid { Height = 51 };
                _topBarContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _topBarContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _topBarContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _topBarContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                _topBarAvatar = new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = DefaultAvatarGlyph(),
                };
                _topBarTitle = new TextBlock
                {
                    Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                    FontSize = 15, FontWeight = Windows.UI.Text.FontWeights.Light,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160,
                };
                leftStack.Children.Add(_topBarAvatar);
                leftStack.Children.Add(_topBarTitle);
                Grid.SetColumn(leftStack, 0);
                _topBarContainer.Children.Add(leftStack);

                var newBtn = MakeTopBarButton("", 14, AppSettings.S("新对话", "New Chat"));
                newBtn.Click += NewConv_Click;
                Grid.SetColumn(newBtn, 1);
                _topBarContainer.Children.Add(newBtn);

                var appearBtn = MakeTopBarButton("", 14, AppSettings.S("外观设置", "Appearance"), 0.75);
                appearBtn.Click += (s, e) => Frame.Navigate(typeof(ConvAppearancePage));
                Grid.SetColumn(appearBtn, 2);
                _topBarContainer.Children.Add(appearBtn);

                var settBtn = MakeTopBarButton("", 15, AppSettings.S("对话设置", "Settings"), 0.75);
                settBtn.Click += (s, e) => Frame.Navigate(typeof(ConvSettingsPage));
                Grid.SetColumn(settBtn, 3);
                _topBarContainer.Children.Add(settBtn);

                shell.SetTopContent(_topBarContainer);
            }

            _topBarTitle.Text = _conv?.Title ?? AppSettings.S("新对话", "New Chat");
            RefreshTopAvatar();
        }

        private static Button MakeTopBarButton(string glyph, double fontSize, string tooltip, double opacity = 1.0)
        {
            var btn = new Button
            {
                Content = glyph,
                FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = fontSize,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Width = 40, Height = 40, Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = opacity,
            };
            ToolTipService.SetToolTip(btn, tooltip);
            return btn;
        }

        private void RefreshTopAvatar()
        {
            if (_topBarAvatar == null) return;
            var cardId = _conv?.CharacterCardId;
            var card = DataManager.Data.CharacterCards.FirstOrDefault(c => c.Id == cardId);
            if (card != null && card.HasAvatar)
            {
                _ = LoadBubbleImageAsync(card.AvatarBase64, bmp =>
                    _topBarAvatar.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill });
            }
            else
            {
                _topBarAvatar.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                _topBarAvatar.Child = DefaultAvatarGlyph();
            }
        }


        private TextBlock DefaultAvatarGlyph()
        {
            return new TextBlock
            {
                Text = "\uE716",
                FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 12, Opacity = 0.5,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private async void NewConv_Click(object sender, RoutedEventArgs e)
        {
            // 切换到新对话前，保存当前对话记忆
            if (_conv != null && _conv.MemoryEnabled && _conv.ExchangesSinceLastSummary > 0 && _conv.Messages.Count >= 3)
            {
                _conv.ExchangesSinceLastSummary = 0;
                await MemoryPipeline.SummarizeAndStoreAsync(_conv);
                await RunDeepMemoryExtractionAsync();
            }

            var conv = DataManager.CreateNewConversation();
            AppState.ActiveConversation = conv;
            _conv = conv;
            _isPendingConv = true;
            _ = DataManager.SaveAsync();
            // Rebuild UI for new empty conversation
            _bubbles.Clear();
            ChatItems.ItemsSource = null;
            ChatItems.ItemsSource = _bubbles;
            WelcomePanel.Visibility = Visibility.Visible;
            InputTextBox.Text = "";
            UpdateTopBar();
        }

    }
}
