using CheckInProject.App.Models;
using CheckInProject.App.Utils;
using CheckInProject.CheckInCore.Interfaces;
using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Threading;

namespace CheckInProject.App.Pages
{
    /// <summary>
    /// ScanDynamicPicturePage.xaml 的交互逻辑
    /// </summary>
    public partial class ScanDynamicPicturePage : Page, INotifyPropertyChanged
    {
        private readonly IServiceProvider ServiceProvider;
        private IFaceDataManager FaceRecognitionAPI => ServiceProvider.GetRequiredService<IFaceDataManager>();
        private IPersonDatabaseManager PersonDatabaseAPI => ServiceProvider.GetRequiredService<IPersonDatabaseManager>();
        private ICheckInManager CheckInManager => ServiceProvider.GetRequiredService<ICheckInManager>();
        private List<RawPersonDataBase> ResultItems => ServiceProvider.GetRequiredService<List<RawPersonDataBase>>();

        #region 属性绑定

        public BitmapSource SourceImage
        {
            get => _sourceImage;
            set
            {
                _sourceImage = value;
                NotifyPropertyChanged();
            }
        }
        private BitmapSource _sourceImage = new BitmapImage();

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

        public int RecognitionMode
        {
            get => _recognitionMode;
            set
            {
                _recognitionMode = value;
                NotifyPropertyChanged();
            }
        }
        private int _recognitionMode = 0;

        public bool IsCameraRunning
        {
            get => _isCameraRunning;
            set
            {
                _isCameraRunning = value;
                NotifyPropertyChanged();
            }
        }
        private bool _isCameraRunning = false;

        public bool IsInitializing
        {
            get => _isInitializing;
            set
            {
                _isInitializing = value;
                NotifyPropertyChanged();
            }
        }
        private bool _isInitializing = false;

        public bool KeepRecognizing
        {
            get => _keepRecognizing;
            set
            {
                _keepRecognizing = value;
                NotifyPropertyChanged();
            }
        }
        private bool _keepRecognizing = false;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                NotifyPropertyChanged();
            }
        }
        private string _statusMessage = "准备就绪";

        public bool ShowSuccessPopup
        {
            get => _showSuccessPopup;
            set
            {
                _showSuccessPopup = value;
                NotifyPropertyChanged();
            }
        }
        private bool _showSuccessPopup = false;

        public string RecognizedName
        {
            get => _recognizedName;
            set
            {
                _recognizedName = value;
                NotifyPropertyChanged();
            }
        }
        private string _recognizedName = string.Empty;

        public string SuccessTime
        {
            get => _successTime;
            set
            {
                _successTime = value;
                NotifyPropertyChanged();
            }
        }
        private string _successTime = string.Empty;

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        private CancellationTokenSource? _cameraCts;
        private int _faceDetectedCount = 0;

        public ScanDynamicPicturePage(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            CameraDevices = CameraDeviceEnumerator.EnumerateCameras();
            InitializeComponent();
            
            // 如果有可用摄像头，默认选择第一个
            if (CameraDevices.Count > 0)
            {
                SelectedCameraIndex = 0;
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        #region 控制按钮

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCameraIndex < 0)
            {
                MessageBox.Show("请先选择摄像头", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var faceDataCount = PersonDatabaseAPI.GetFaceData().Count;
            if (faceDataCount == 0)
            {
                var result = MessageBox.Show("人脸数据库为空，是否先录入人脸数据？", "提示", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<FaceDataManagementPage>());
                }
                return;
            }

            await StartRecognitionAsync(SelectedCameraIndex, RecognitionMode == 1);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            StopCamera();
            App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<WelcomePage>());
        }

        #endregion

        #region 摄像头识别逻辑

        private async Task StartRecognitionAsync(int cameraIndex, bool isMultiplePersonMode)
        {
            IsInitializing = true;
            StatusMessage = "正在启动摄像头...";

            try
            {
                _cameraCts = new CancellationTokenSource();
                IsCameraRunning = true;
                IsInitializing = false;
                StatusMessage = isMultiplePersonMode ? "多人识别模式 - 正在识别..." : "单人识别模式 - 正在识别...";
                _faceDetectedCount = 0;

                await Task.Run(() => CaptureAndRecognize(cameraIndex, isMultiplePersonMode, _cameraCts.Token));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    StopCamera();
                });
            }
        }

        private void CaptureAndRecognize(int cameraIndex, bool isMultiplePersonMode, CancellationToken token)
        {
            try
            {
                using (var capture = new VideoCapture(cameraIndex, VideoCaptureAPIs.DSHOW))
                {
                    if (!capture.IsOpened())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "摄像头打开失败";
                            StopCamera();
                        });
                        return;
                    }

                    // 设置分辨率提高性能
                    capture.Set(VideoCaptureProperties.FrameWidth, 640);
                    capture.Set(VideoCaptureProperties.FrameHeight, 480);

                    using (var image = new Mat())
                    {
                        while (!token.IsCancellationRequested)
                        {
                            capture.Read(image);
                            if (image.Empty()) continue;

                            // 检测人脸
                            var faceCount = FaceRecognitionAPI.GetFaceCount(image);
                            
                            using (faceCount.RetangleImage)
                            {
                                // 更新预览图像
                                var bitmapImage = PictureConverters.ToBitmapImage(faceCount.RetangleImage);
                                Dispatcher.Invoke(() => SourceImage = bitmapImage);

                                if (faceCount.Count > 0)
                                {
                                    _faceDetectedCount++;
                                    
                                    // 连续检测到人脸20帧后执行识别
                                    if (_faceDetectedCount >= 20)
                                    {
                                        using (var targetBitmap = image.ToBitmap())
                                        {
                                            bool recognized = ProcessRecognition(targetBitmap, isMultiplePersonMode);
                                            if (recognized && !KeepRecognizing)
                                            {
                                                Dispatcher.Invoke(StopCamera);
                                                return;
                                            }
                                        }
                                        _faceDetectedCount = 0;
                                    }
                                }
                                else
                                {
                                    _faceDetectedCount = 0;
                                    Dispatcher.Invoke(() => StatusMessage = isMultiplePersonMode 
                                        ? "多人模式 - 请将人脸对准摄像头" 
                                        : "单人模式 - 请将人脸对准摄像头");
                                }
                            }

                            Thread.Sleep(50); // 20fps
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"错误: {ex.Message}";
                    MessageBox.Show($"摄像头异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    IsCameraRunning = false;
                    StatusMessage = "已停止";
                });
            }
        }

        private bool ProcessRecognition(Bitmap targetBitmap, bool isMultiplePersonMode)
        {
            try
            {
                if (isMultiplePersonMode)
                {
                    return ProcessMultiplePersonRecognition(targetBitmap);
                }
                else
                {
                    return ProcessSinglePersonRecognition(targetBitmap);
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => StatusMessage = $"识别错误: {ex.Message}");
                return false;
            }
        }

        private bool ProcessSinglePersonRecognition(Bitmap targetBitmap)
        {
            var targetFaceBitmapModels = FaceRecognitionAPI.GetFaceImage(targetBitmap);
            try
            {
                if (targetFaceBitmapModels.FaceImages.Count == 0)
                {
                    Dispatcher.Invoke(() => StatusMessage = "未检测到有效人脸");
                    return false;
                }

                using (var targetFaceBitmap = targetFaceBitmapModels.FaceImages.First())
                {
                    var targetFaceEncoding = FaceRecognitionAPI.CreateFaceData(targetFaceBitmap, null, null);
                    var knownFaces = PersonDatabaseAPI.GetFaceData().Select(t => t.ConvertToRawPersonDataBase()).ToList();
                    var result = FaceRecognitionAPI.CompareFace(knownFaces, targetFaceEncoding);

                    if (result.Count > 0)
                    {
                        if (result.Count == 1)
                        {
                            var person = result.First();
                            Dispatcher.Invoke(async () =>
                            {
                                try
                                {
                                    await CheckInManager.CheckIn(DateOnly.FromDateTime(DateTime.Now), TimeOnly.FromDateTime(DateTime.Now), person.StudentID);
                                    ShowSuccessNotification(person.Name ?? "未知");
                                }
                                catch (Exception ex)
                                {
                                    StatusMessage = $"签到失败: {ex.Message}";
                                }
                            });
                            return true;
                        }
                        else
                        {
                            ResultItems.Clear();
                            ResultItems.AddRange(result);
                            Dispatcher.Invoke(() =>
                            {
                                App.RootFrame?.Navigate(ServiceProvider.GetRequiredService<MultipleResultsPage>());
                            });
                            return true;
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() => StatusMessage = "未匹配到已知人脸");
                    }
                }
            }
            finally
            {
                targetFaceBitmapModels.RetangleImage?.Dispose();
            }
            return false;
        }

        private bool ProcessMultiplePersonRecognition(Bitmap targetBitmap)
        {
            var targetFaceBitmapModels = FaceRecognitionAPI.GetFacesImage(targetBitmap);
            try
            {
                if (targetFaceBitmapModels.FaceImages.Count == 0)
                {
                    Dispatcher.Invoke(() => StatusMessage = "未检测到有效人脸");
                    return false;
                }

                var targetFaceBitmapList = targetFaceBitmapModels.FaceImages;
                try
                {
                    var targetFaceEncoding = FaceRecognitionAPI.CreateFacesData(targetFaceBitmapList);
                    var knownFaces = PersonDatabaseAPI.GetFaceData().Select(t => t.ConvertToRawPersonDataBase()).ToList();
                    var result = FaceRecognitionAPI.CompareFaces(knownFaces, targetFaceEncoding);

                    if (result.Count > 0)
                    {
                        var names = result.Select(r => r.Name).Where(n => !string.IsNullOrEmpty(n));
                        Dispatcher.Invoke(async () =>
                        {
                            try
                            {
                                StatusMessage = $"识别成功: {string.Join(", ", names)}";
                                foreach (var person in result)
                                {
                                    await CheckInManager.CheckIn(DateOnly.FromDateTime(DateTime.Now), TimeOnly.FromDateTime(DateTime.Now), person.StudentID);
                                }
                                ShowSuccessNotification(string.Join(", ", names));
                            }
                            catch (Exception ex)
                            {
                                StatusMessage = $"签到失败: {ex.Message}";
                            }
                        });
                        return true;
                    }
                    else
                    {
                        Dispatcher.Invoke(() => StatusMessage = "未匹配到已知人脸");
                    }
                }
                finally
                {
                    foreach (var item in targetFaceBitmapList)
                    {
                        item?.Dispose();
                    }
                }
            }
            finally
            {
                targetFaceBitmapModels.RetangleImage?.Dispose();
            }
            return false;
        }

        private void ShowSuccessNotification(string name)
        {
            RecognizedName = name;
            SuccessTime = DateTime.Now.ToString("HH:mm:ss");
            ShowSuccessPopup = true;
            
            // 2秒后自动关闭弹窗
            Task.Delay(2000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => ShowSuccessPopup = false);
            });
        }

        private void StopCamera()
        {
            _cameraCts?.Cancel();
            IsCameraRunning = false;
            StatusMessage = "已停止";
            _faceDetectedCount = 0;
        }

        #endregion
    }
}
