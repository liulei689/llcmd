using CheckInProject.CheckInCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CheckInProject.App.Pages
{
    /// <summary>
    /// MultipleResultsPage.xaml 的交互逻辑
    /// </summary>
    public partial class MultipleResultsPage : Page
    {
        private readonly IServiceProvider ServiceProvider;
        private ICheckInManager CheckInManager => ServiceProvider.GetRequiredService<ICheckInManager>();
        private List<RawPersonDataBase> ResultItems => ServiceProvider.GetRequiredService<List<RawPersonDataBase>>();

        public MultipleResultsPage(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            InitializeComponent();
            Loaded += MultipleResultsPage_Loaded;
        }

        private void MultipleResultsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCandidates();
        }

        private void LoadCandidates()
        {
            CandidatesPanel.Children.Clear();

            foreach (var person in ResultItems)
            {
                var card = CreateCandidateCard(person);
                CandidatesPanel.Children.Add(card);
            }
        }

        private Border CreateCandidateCard(RawPersonDataBase person)
        {
            var card = new Border
            {
                Width = 200,
                Height = 120,
                Margin = new Thickness(10),
                Background = (Brush)Application.Current.FindResource("ControlFillColorDefaultBrush"),
                CornerRadius = new CornerRadius(12),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = person
            };

            card.MouseLeftButtonUp += Card_Click;

            // 悬停效果
            card.MouseEnter += (s, e) =>
            {
                card.Background = (Brush)Application.Current.FindResource("SystemAccentColorBrush");
                if (card.Child is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock tb) tb.Foreground = Brushes.White;
                    }
                }
            };

            card.MouseLeave += (s, e) =>
            {
                card.Background = (Brush)Application.Current.FindResource("ControlFillColorDefaultBrush");
                if (card.Child is StackPanel sp)
                {
                    foreach (var child in sp.Children)
                    {
                        if (child is TextBlock tb) tb.ClearValue(TextBlock.ForegroundProperty);
                    }
                }
            };

            // 阴影效果
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.2
            };

            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 头像图标
            var icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = Wpf.Ui.Common.SymbolRegular.Person24,
                FontSize = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            // 姓名
            var nameBlock = new TextBlock
            {
                Text = person.Name ?? "未知",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // 编号
            var idBlock = new TextBlock
            {
                Text = person.ClassID?.ToString() ?? "",
                FontSize = 14,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0)
            };

            content.Children.Add(icon);
            content.Children.Add(nameBlock);
            content.Children.Add(idBlock);

            card.Child = content;
            return card;
        }

        private async void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Border card && card.Tag is RawPersonDataBase person)
            {
                try
                {
                    var currentTime = DateTime.Now;
                    await CheckInManager.CheckIn(DateOnly.FromDateTime(currentTime), TimeOnly.FromDateTime(currentTime), person.StudentID);
                    
                    MessageBox.Show($"签到成功！\n\n姓名: {person.Name}\n时间: {currentTime:HH:mm:ss}", 
                        "签到成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<CheckInRecordsPage>());
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"签到失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<ScanDynamicPicturePage>());
        }
    }
}
