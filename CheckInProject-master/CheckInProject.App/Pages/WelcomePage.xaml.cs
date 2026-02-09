using CheckInProject.CheckInCore.Interfaces;
using CheckInProject.PersonDataCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CheckInProject.App.Pages
{
    /// <summary>
    /// WelcomePage.xaml 的交互逻辑
    /// </summary>
    public partial class WelcomePage : Page
    {
        private readonly IServiceProvider ServiceProvider;
        private IPersonDatabaseManager PersonDatabaseAPI => ServiceProvider.GetRequiredService<IPersonDatabaseManager>();
        private ICheckInManager CheckInManager => ServiceProvider.GetRequiredService<ICheckInManager>();

        public WelcomePage(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            InitializeComponent();
            Loaded += WelcomePage_Loaded;
        }

        private async void WelcomePage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 加载统计数据
                var faceCount = PersonDatabaseAPI.GetFaceData().Count;
                var todayRecords = await CheckInManager.GetTodayCheckInData();
                
                FaceCountText.Text = faceCount.ToString();
                TodayCheckInText.Text = todayRecords.Count.ToString();
            }
            catch
            {
                // 忽略统计加载错误
            }
        }

        private void DynamicScanCard_Click(object sender, MouseButtonEventArgs e)
        {
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<ScanDynamicPicturePage>());
        }

        private void DataManageCard_Click(object sender, MouseButtonEventArgs e)
        {
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<FaceDataManagementPage>());
        }

        private void RecordsCard_Click(object sender, MouseButtonEventArgs e)
        {
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<CheckInRecordsPage>());
        }
    }
}
