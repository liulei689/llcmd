using CheckInProject.App.Models;
using CheckInProject.App.Utils;
using CheckInProject.CheckInCore.Interfaces;
using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.WindowsAPICodePack.Dialogs;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CheckInProject.App.Pages
{
    /// <summary>
    /// FaceDataManagementPage.xaml 的交互逻辑
    /// </summary>
    public partial class FaceDataManagementPage : Page, INotifyPropertyChanged
    {
        private readonly IServiceProvider ServiceProvider;

        public event PropertyChangedEventHandler? PropertyChanged;

        #region 属性

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

        public string TotalCountText
        {
            get => _totalCountText;
            set
            {
                _totalCountText = value;
                NotifyPropertyChanged();
            }
        }
        private string _totalCountText = "共 0 人";

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

        public bool TrimImageWhenEncoding
        {
            get => _trimImageWhenEncoding;
            set
            {
                _trimImageWhenEncoding = value;
                NotifyPropertyChanged();
            }
        }
        private bool _trimImageWhenEncoding = false;

        public ObservableCollection<StringPersonDataBase> FaceDataList
        {
            get => _faceDataList;
            set
            {
                _faceDataList = value;
                NotifyPropertyChanged();
                UpdateFilteredList();
            }
        }
        private ObservableCollection<StringPersonDataBase> _faceDataList = new ObservableCollection<StringPersonDataBase>();

        public ObservableCollection<StringPersonDataBase> FilteredFaceDataList
        {
            get => _filteredFaceDataList;
            set
            {
                _filteredFaceDataList = value;
                NotifyPropertyChanged();
            }
        }
        private ObservableCollection<StringPersonDataBase> _filteredFaceDataList = new ObservableCollection<StringPersonDataBase>();

        public List<CameraDevice> CameraDevices
        {
            get => _cameraDevices;
            set
            {
                _cameraDevices = value;
                NotifyPropertyChanged();
            }
        }
        private List<CameraDevice> _cameraDevices = new List<CameraDevice>();

        public BitmapSource CameraPreviewSource
        {
            get => _cameraPreviewSource;
            set
            {
                _cameraPreviewSource = value;
                NotifyPropertyChanged();
            }
        }
        private BitmapSource _cameraPreviewSource = new BitmapImage();

        public bool IsCameraPreviewing
        {
            get => _isCameraPreviewing;
            set
            {
                _isCameraPreviewing = value;
                NotifyPropertyChanged();
            }
        }
        private bool _isCameraPreviewing = false;

        public int SelectedCameraIndex
        {
            get => _selectedCameraIndex;
            set
            {
                _selectedCameraIndex = value;
                NotifyPropertyChanged();
            }
        }
        private int _selectedCameraIndex = -1;

        #endregion

        private IFaceDataManager FaceRecognitionAPI => ServiceProvider.GetRequiredService<IFaceDataManager>();
        private IPersonDatabaseManager DatabaseAPI => ServiceProvider.GetRequiredService<IPersonDatabaseManager>();
        private ICheckInManager CheckInManager => ServiceProvider.GetRequiredService<ICheckInManager>();

        private CancellationTokenSource? _cameraCts;
        private Bitmap? _lastCapturedBitmap = null;
        private StringPersonDataBase? _editingItem = null;

        public FaceDataManagementPage(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            InitializeComponent();
            CameraDevices = CameraDeviceEnumerator.EnumerateCameras();
            Loaded += FaceDataManagementPage_Loaded;
        }

        private void FaceDataManagementPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshDataList();
        }

        private void RefreshDataList()
        {
            FaceDataList.Clear();
            var data = DatabaseAPI.GetFaceData();
            foreach (var item in data)
            {
                FaceDataList.Add(item);
            }
            UpdateFilteredList();
            UpdateStats();
        }

        private void UpdateFilteredList()
        {
            var searchText = SearchTextBox?.Text?.ToLower() ?? "";
            var filtered = string.IsNullOrWhiteSpace(searchText)
                ? FaceDataList.ToList()
                : FaceDataList.Where(x => 
                    (x.Name?.ToLower().Contains(searchText) ?? false) || 
                    (x.ClassID?.ToString().Contains(searchText) ?? false)).ToList();

            FilteredFaceDataList = new ObservableCollection<StringPersonDataBase>(filtered);
            IsEmpty = FaceDataList.Count == 0;
        }

        private void UpdateStats()
        {
            TotalCountText = $"共 {FaceDataList.Count} 人";
            IsEmpty = FaceDataList.Count == 0;
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        #region 导航

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<WelcomePage>());
        }

        #endregion

        #region 批量导入

        private async void ImportFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new CommonOpenFileDialog())
            {
                dialog.IsFolderPicker = true;
                dialog.Title = "选择包含人脸图片的文件夹";

                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    await ImportFromFolderAsync(dialog.FileName);
                }
            }
        }

        private async Task ImportFromFolderAsync(string folderPath)
        {
            try
            {
                var imageFiles = Directory.GetFiles(folderPath)
                    .Where(f => PictureConverters.SupportedPictureType
                        .Contains(Path.GetExtension(f).ToUpper())).ToList();

                if (imageFiles.Count == 0)
                {
                    MessageBox.Show("所选文件夹中没有支持的图片文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show($"找到 {imageFiles.Count} 张图片，确定要导入吗？\n\n文件名将作为姓名导入", 
                    "确认导入", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                var successCount = 0;
                var failCount = 0;
                var faceDataList = new List<RawPersonDataBase>();
                uint index = 0;

                foreach (var imageFile in imageFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(imageFile);
                    StatusMessage = $"正在处理: {fileName} ({successCount + failCount + 1}/{imageFiles.Count})";

                    try
                    {
                        using (var bitmap = new Bitmap(imageFile))
                        {
                            Bitmap processBitmap = bitmap;
                            if (TrimImageWhenEncoding)
                            {
                                var faceModels = FaceRecognitionAPI.GetFaceImage(bitmap);
                                if (faceModels.FaceImages.Count == 0)
                                {
                                    failCount++;
                                    continue;
                                }
                                processBitmap = faceModels.FaceImages.First();
                            }
                            else
                            {
                                // 复制一份以避免 using 结束后被释放
                                processBitmap = new Bitmap(bitmap);
                            }

                            var faceData = FaceRecognitionAPI.CreateFaceData(processBitmap, fileName, ++index);
                            processBitmap.Dispose();
                            faceDataList.Add(faceData);
                            successCount++;
                        }
                    }
                    catch
                    {
                        failCount++;
                    }
                }

                if (faceDataList.Count > 0)
                {
                    await CheckInManager.ClearCheckInRecords();
                    var stringData = faceDataList.Select(t => t.ConvertToStringPersonDataBase()).ToList();
                    await DatabaseAPI.ImportFaceData(stringData);
                }

                StatusMessage = $"导入完成: 成功 {successCount}, 失败 {failCount}";
                RefreshDataList();

                MessageBox.Show($"导入完成！\n成功: {successCount}\n失败: {failCount}", 
                    "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (FaceDataList.Count == 0)
            {
                MessageBox.Show("数据库已经是空的", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确定要清空所有 {FaceDataList.Count} 条人脸数据吗？\n此操作不可恢复！", 
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                await DatabaseAPI.ClearFaceData();
                await CheckInManager.ClearCheckInRecords();
                RefreshDataList();
                StatusMessage = "数据库已清空";
            }
        }

        #endregion

        #region 从图片添加

        private Bitmap? _previewBitmap = null;
        private string _previewFileName = "";

        private void AddFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = $"图片文件|{PictureConverters.SupportedPictureExtensions}",
                Title = "选择人脸图片"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                // 加载图片并显示预览
                using (var bitmap = new Bitmap(dialog.FileName))
                {
                    _previewBitmap = new Bitmap(bitmap);
                }

                _previewFileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                
                // 检测人脸
                bool hasFace = false;
                try
                {
                    var faceModels = FaceRecognitionAPI.GetFaceImage(_previewBitmap);
                    hasFace = faceModels.FaceImages.Count > 0;
                }
                catch { }

                // 显示预览面板
                PreviewImage.Source = PictureConverters.ToBitmapImage(_previewBitmap);
                PreviewNameTextBox.Text = _previewFileName;
                PreviewClassIdTextBox.Text = "";

                // 更新检测状态
                if (hasFace)
                {
                    FaceDetectStatusBorder.Background = (System.Windows.Media.Brush)Application.Current.FindResource("SystemFillColorSuccessBrush");
                    FaceDetectStatusText.Text = "✓ 已检测到人脸，可以添加";
                }
                else
                {
                    FaceDetectStatusBorder.Background = (System.Windows.Media.Brush)Application.Current.FindResource("SystemFillColorCautionBrush");
                    FaceDetectStatusText.Text = "⚠ 未检测到人脸，可能无法识别";
                }

                DataListPanel.Visibility = Visibility.Collapsed;
                ImagePreviewPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                _previewBitmap?.Dispose();
                _previewBitmap = null;
            }
        }

        private void CloseImagePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAddImageButton_Click(sender, e);
        }

        private void CancelAddImageButton_Click(object sender, RoutedEventArgs e)
        {
            ImagePreviewPanel.Visibility = Visibility.Collapsed;
            DataListPanel.Visibility = Visibility.Visible;
            _previewBitmap?.Dispose();
            _previewBitmap = null;
            _previewFileName = "";
        }

        private async void ConfirmAddImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_previewBitmap == null) return;

            var name = PreviewNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("请输入姓名", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                PreviewNameTextBox.Focus();
                return;
            }

            try
            {
                Bitmap processBitmap;
                if (TrimImageWhenEncoding)
                {
                    var faceModels = FaceRecognitionAPI.GetFaceImage(_previewBitmap);
                    if (faceModels.FaceImages.Count == 0)
                    {
                        MessageBox.Show("未检测到人脸，请更换图片", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    processBitmap = faceModels.FaceImages.First();
                }
                else
                {
                    processBitmap = new Bitmap(_previewBitmap);
                }

                uint? classId = null;
                if (uint.TryParse(PreviewClassIdTextBox.Text.Trim(), out var parsedId))
                {
                    classId = parsedId;
                }

                var newId = (uint)(FaceDataList.Count + 1);
                var faceData = FaceRecognitionAPI.CreateFaceData(processBitmap, name, newId);
                faceData.ClassID = classId;
                var stringData = faceData.ConvertToStringPersonDataBase();
                await DatabaseAPI.AddFaceData(stringData);

                StatusMessage = $"已添加: {name}";
                RefreshDataList();
                
                // 关闭预览面板
                ImagePreviewPanel.Visibility = Visibility.Collapsed;
                DataListPanel.Visibility = Visibility.Visible;
                
                MessageBox.Show($"已成功添加: {name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _previewBitmap?.Dispose();
                _previewBitmap = null;
                _previewFileName = "";
            }
        }

        #endregion

        #region 摄像头录入

        private void AddFromCameraButton_Click(object sender, RoutedEventArgs e)
        {
            CameraPanel.Visibility = Visibility.Visible;
            DataListPanel.Visibility = Visibility.Collapsed;
            NewPersonNameTextBox.Text = "";
            NewPersonClassIdTextBox.Text = "";
            _lastCapturedBitmap = null;

            if (CameraDevices.Count > 0 && SelectedCameraIndex < 0)
            {
                SelectedCameraIndex = 0;
            }
        }

        private void CloseCameraPanelButton_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            CameraPanel.Visibility = Visibility.Collapsed;
            DataListPanel.Visibility = Visibility.Visible;
        }

        private void StartPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCameraIndex < 0)
            {
                MessageBox.Show("请先选择摄像头", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            StartCameraPreview(SelectedCameraIndex);
        }

        private void StopPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }

        private void StartCameraPreview(int cameraIndex)
        {
            StopCamera();
            _cameraCts = new CancellationTokenSource();
            IsCameraPreviewing = true;

            Task.Run(() =>
            {
                try
                {
                    using (var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW))
                    {
                        if (!capture.IsOpened())
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("无法打开摄像头", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                IsCameraPreviewing = false;
                            });
                            return;
                        }

                        capture.Set(VideoCaptureProperties.FrameWidth, 640);
                        capture.Set(VideoCaptureProperties.FrameHeight, 480);

                        using (var image = new Mat())
                        {
                            while (!_cameraCts.Token.IsCancellationRequested)
                            {
                                capture.Read(image);
                                if (image.Empty()) continue;

                                var faceCount = FaceRecognitionAPI.GetFaceCount(image);
                                using (faceCount.RetangleImage)
                                {
                                    var bitmapImage = PictureConverters.ToBitmapImage(faceCount.RetangleImage);
                                    Dispatcher.Invoke(() => CameraPreviewSource = bitmapImage);

                                    if (faceCount.Count > 0)
                                    {
                                        _lastCapturedBitmap?.Dispose();
                                        _lastCapturedBitmap = image.ToBitmap();
                                    }
                                }
                                Thread.Sleep(50);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"摄像头错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        IsCameraPreviewing = false;
                    });
                }
            });
        }

        private async void CaptureAndSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastCapturedBitmap == null)
            {
                MessageBox.Show("未检测到人脸，请确保人脸在画面中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = NewPersonNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("请输入姓名", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                NewPersonNameTextBox.Focus();
                return;
            }

            Bitmap? processBitmap = null;
            try
            {
                if (TrimImageWhenEncoding)
                {
                    var faceModels = FaceRecognitionAPI.GetFaceImage(_lastCapturedBitmap);
                    if (faceModels.FaceImages.Count == 0)
                    {
                        MessageBox.Show("人脸检测失败，请重试", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    processBitmap = faceModels.FaceImages.First();
                }
                else
                {
                    // 复制一份，因为 _lastCapturedBitmap 后面会被释放
                    processBitmap = new Bitmap(_lastCapturedBitmap);
                }

                uint? classId = null;
                if (uint.TryParse(NewPersonClassIdTextBox.Text.Trim(), out var parsedId))
                {
                    classId = parsedId;
                }

                var newId = (uint)(FaceDataList.Count + 1);
                var faceData = FaceRecognitionAPI.CreateFaceData(processBitmap, name, newId);
                faceData.ClassID = classId;
                var stringData = faceData.ConvertToStringPersonDataBase();
                await DatabaseAPI.AddFaceData(stringData);

                StatusMessage = $"已添加: {name}";
                NewPersonNameTextBox.Text = "";
                NewPersonClassIdTextBox.Text = "";
                
                RefreshDataList();
                MessageBox.Show($"已成功添加: {name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                processBitmap?.Dispose();
                _lastCapturedBitmap?.Dispose();
                _lastCapturedBitmap = null;
            }
        }

        private void StopCamera()
        {
            _cameraCts?.Cancel();
            IsCameraPreviewing = false;
        }

        #endregion

        #region 单条编辑删除

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is StringPersonDataBase data)
            {
                _editingItem = data;
                EditNameTextBox.Text = data.Name ?? "";
                EditClassIdTextBox.Text = data.ClassID?.ToString() ?? "";
                EditPanel.Visibility = Visibility.Visible;
            }
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            EditPanel.Visibility = Visibility.Collapsed;
            _editingItem = null;
        }

        private async void SaveEditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_editingItem == null) return;

            var name = EditNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("姓名不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _editingItem.Name = name;
                if (uint.TryParse(EditClassIdTextBox.Text.Trim(), out var classId))
                {
                    _editingItem.ClassID = classId;
                }
                else
                {
                    _editingItem.ClassID = null;
                }

                await DatabaseAPI.UpdateFaceData(_editingItem);
                StatusMessage = $"已更新: {name}";
                EditPanel.Visibility = Visibility.Collapsed;
                _editingItem = null;
                RefreshDataList();
                MessageBox.Show($"已成功更新: {name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is StringPersonDataBase data)
            {
                var result = MessageBox.Show($"确定要删除 {data.Name} 的人脸数据吗？", 
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await DatabaseAPI.DeleteFaceData(data.StudentID);
                    StatusMessage = $"已删除: {data.Name}";
                    RefreshDataList();
                }
            }
        }

        #endregion

        #region 搜索

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilteredList();
        }

        #endregion
    }
}
