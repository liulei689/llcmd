using CheckInProject.CheckInCore.Interfaces;
using CheckInProject.CheckInCore.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CheckInProject.App.Pages
{
    /// <summary>
    /// CheckInRecordsPage.xaml 的交互逻辑
    /// </summary>
    public partial class CheckInRecordsPage : Page, INotifyPropertyChanged
    {
        private readonly IServiceProvider ServiceProvider;
        private ICheckInManager CheckInManager => ServiceProvider.GetRequiredService<ICheckInManager>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<CheckInRecordViewModel> RecordsList
        {
            get => _recordsList;
            set
            {
                _recordsList = value;
                NotifyPropertyChanged();
                IsEmpty = value.Count == 0;
            }
        }
        private ObservableCollection<CheckInRecordViewModel> _recordsList = new ObservableCollection<CheckInRecordViewModel>();

        public string TodayCountText
        {
            get => _todayCountText;
            set
            {
                _todayCountText = value;
                NotifyPropertyChanged();
            }
        }
        private string _todayCountText = "今日 0 人";

        public string MorningCountText
        {
            get => _morningCountText;
            set
            {
                _morningCountText = value;
                NotifyPropertyChanged();
            }
        }
        private string _morningCountText = "上午 0 人";

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                NotifyPropertyChanged();
            }
        }
        private string _statusMessage = "就绪";

        public bool IsEmpty
        {
            get => _isEmpty;
            set
            {
                _isEmpty = value;
                NotifyPropertyChanged();
            }
        }
        private bool _isEmpty = true;

        public CheckInRecordsPage(IServiceProvider provider)
        {
            ServiceProvider = provider;
            InitializeComponent();
            Loaded += CheckInRecordsPage_Loaded;
        }

        private void CheckInRecordsPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadRecords();
        }

        private void LoadRecords()
        {
            try
            {
                var records = CheckInManager.QueryTodayRecords();
                var viewModels = records.Select(r => new CheckInRecordViewModel
                {
                    Name = r.Name ?? "未知",
                    ClassID = r.ClassID?.ToString() ?? "-",
                    MorningTimeText = FormatCheckInTime(r.MorningCheckedIn, r.MorningCheckInTime),
                    AfternoonTimeText = FormatCheckInTime(r.AfternoonCheckedIn, r.AfternoonCheckInTime),
                    EveningTimeText = FormatCheckInTime(r.EveningCheckedIn, r.EveningCheckInTime),
                    CheckInDate = r.CheckInDate.ToString("yyyy-MM-dd")
                }).ToList();

                RecordsList = new ObservableCollection<CheckInRecordViewModel>(viewModels);

                var morningCount = records.Count(r => r.MorningCheckedIn);
                var totalCount = records.Count;
                TodayCountText = $"今日 {totalCount} 人";
                MorningCountText = $"上午 {morningCount} 人";
                StatusMessage = $"共 {totalCount} 条记录";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
        }

        private string FormatCheckInTime(bool checkedIn, TimeOnly? time)
        {
            if (!checkedIn) return "-";
            return time?.ToString("HH:mm") ?? "✓";
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<WelcomePage>());
        }

        private void GoToScanButton_Click(object sender, RoutedEventArgs e)
        {
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<ScanDynamicPicturePage>());
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (RecordsList.Count == 0)
            {
                MessageBox.Show("没有记录可导出", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel文件|*.xlsx",
                FileName = $"签到记录_{DateTime.Now:yyyyMMdd}.xlsx"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                StatusMessage = "正在导出...";
                await CheckInManager.ExportRecordsToExcelFile(ExportTypeEnum.CheckedIn, dialog.FileName);
                StatusMessage = "导出成功";
                MessageBox.Show("导出成功！", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"导出失败: {ex.Message}";
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class CheckInRecordViewModel
    {
        public string Name { get; set; } = "";
        public string ClassID { get; set; } = "";
        public string MorningTimeText { get; set; } = "";
        public string AfternoonTimeText { get; set; } = "";
        public string EveningTimeText { get; set; } = "";
        public string CheckInDate { get; set; } = "";
    }
}
