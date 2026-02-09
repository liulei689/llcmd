using CheckInProject.App.Models;
using CheckInProject.App.Utils;
using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Threading;

namespace CheckInProject.App.Pages
{
    /// <summary>
    /// 权限验证页面 - 全屏人脸识别
    ///
    /// 【模型切换配置】
    /// 修改 FaceDataManager.UseCnnModel 即可切换识别模型：
    /// - false (默认) = HOG模型 - 速度快，适合实时验证
    /// - true = CNN模型 - 精度高，但慢10倍以上
    ///
    /// 配置位置：CheckInProject.PersonDataCore.Implementation.FaceDataManager.UseCnnModel
    /// </summary>
    public partial class AuthVerifyPage : Page, INotifyPropertyChanged
    {
        private readonly IServiceProvider ServiceProvider;
        private IFaceDataManager FaceRecognitionAPI => ServiceProvider.GetRequiredService<IFaceDataManager>();
        private IPersonDatabaseManager PersonDatabaseAPI => ServiceProvider.GetRequiredService<IPersonDatabaseManager>();

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

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                NotifyPropertyChanged();
            }
        }
        private string _statusMessage = "正在启动摄像头...";

        public string HintMessage
        {
            get => _hintMessage;
            set
            {
                _hintMessage = value;
                NotifyPropertyChanged();
            }
        }
        private string _hintMessage = "请将人脸对准摄像头";

        public string TargetUserText
        {
            get => _targetUserText;
            set
            {
                _targetUserText = value;
                NotifyPropertyChanged();
            }
        }
        private string _targetUserText = "";

        public bool ShowDeniedMessage
        {
            get => _showDeniedMessage;
            set
            {
                _showDeniedMessage = value;
                NotifyPropertyChanged();
            }
        }
        private bool _showDeniedMessage = false;

        public bool ShowGrantedMessage
        {
            get => _showGrantedMessage;
            set
            {
                _showGrantedMessage = value;
                NotifyPropertyChanged();
            }
        }
        private bool _showGrantedMessage = false;

        public string DeniedUserText
        {
            get => _deniedUserText;
            set
            {
                _deniedUserText = value;
                NotifyPropertyChanged();
            }
        }
        private string _deniedUserText = "";

        public string GrantedUserText
        {
            get => _grantedUserText;
            set
            {
                _grantedUserText = value;
                NotifyPropertyChanged();
            }
        }
        private string _grantedUserText = "";

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;

        private CancellationTokenSource? _cameraCts;
        private int _faceDetectedCount = 0;
        private DateTime _lastDeniedTime = DateTime.MinValue;
        private readonly TimeSpan _deniedDisplayDuration = TimeSpan.FromSeconds(2);

        /// <summary>
        /// 目标验证用户ID（ClassID）
        /// </summary>
        public uint? TargetUserId { get; set; } = null;

        /// <summary>
        /// 验证成功回调
        /// </summary>
        public Action<AuthResult>? OnAuthSuccess { get; set; }

        /// <summary>
        /// 验证失败回调
        /// </summary>
        public Action<AuthResult>? OnAuthFailed { get; set; }

        public AuthVerifyPage(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            InitializeComponent();
            
            // 捕获键盘事件
            Loaded += (s, e) =>
            {
                Focus();
                if (Parent is FrameworkElement parent)
                {
                    parent.Focusable = true;
                    parent.Focus();
                }
            };
            
            KeyDown += AuthVerifyPage_KeyDown;
        }

        private void AuthVerifyPage_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // ESC 退出，返回失败结果
                StopCamera();
                OnAuthFailed?.Invoke(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "用户取消验证",
                    AuthTime = DateTime.Now
                });
            }
        }

        public void StartAuth(uint? targetUserId)
        {
            TargetUserId = targetUserId;
            TargetUserText = TargetUserId.HasValue
                ? $"等待验证用户ID: {TargetUserId.Value}"
                : "等待识别任何用户...";
            
            // 自动选择第一个摄像头
            var cameras = CameraDeviceEnumerator.EnumerateCameras();
            if (cameras.Count > 0)
            {
                StartRecognitionAsync(0);
            }
            else
            {
                StatusMessage = "未找到摄像头";
                OnAuthFailed?.Invoke(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "未找到摄像头",
                    AuthTime = DateTime.Now
                });
            }
        }

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }

        private async Task StartRecognitionAsync(int cameraIndex)
        {
            try
            {
                _cameraCts = new CancellationTokenSource();
                StatusMessage = "正在识别...";
                _faceDetectedCount = 0;

                await Task.Run(() => CaptureAndRecognize(cameraIndex, _cameraCts.Token));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"启动失败: {ex.Message}";
                });
            }
        }

        private void CaptureAndRecognize(int cameraIndex, CancellationToken token)
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
                            OnAuthFailed?.Invoke(new AuthResult
                            {
                                Success = false,
                                ErrorMessage = "摄像头打开失败",
                                AuthTime = DateTime.Now
                            });
                        });
                        return;
                    }

                    // 设置分辨率
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

                                // 隐藏过期的权限不足提示
                                if (ShowDeniedMessage && DateTime.Now - _lastDeniedTime > _deniedDisplayDuration)
                                {
                                    Dispatcher.Invoke(() => ShowDeniedMessage = false);
                                }

                                if (faceCount.Count > 0)
                                {
                                    _faceDetectedCount++;

                                    // 连续检测到人脸3帧后执行识别（减少等待时间）
                                    if (_faceDetectedCount >= 3)
                                    {
                                        using (var targetBitmap = image.ToBitmap())
                                        {
                                            bool shouldStop = ProcessAuthRecognition(targetBitmap);
                                            if (shouldStop)
                                            {
                                                return;
                                            }
                                        }
                                        _faceDetectedCount = 0;
                                    }
                                }
                                else
                                {
                                    _faceDetectedCount = 0;
                                    Dispatcher.Invoke(() =>
                                    {
                                        if (!ShowDeniedMessage && !ShowGrantedMessage)
                                        {
                                            StatusMessage = "请将人脸对准摄像头";
                                        }
                                    });
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
                    StatusMessage = $"摄像头异常: {ex.Message}";
                });
            }
        }

        private bool ProcessAuthRecognition(Bitmap targetBitmap)
        {
            try
            {
                var targetFaceBitmapModels = FaceRecognitionAPI.GetFaceImage(targetBitmap);
                try
                {
                    if (targetFaceBitmapModels.FaceImages.Count == 0)
                    {
                        return false;
                    }

                    using (var targetFaceBitmap = targetFaceBitmapModels.FaceImages.First())
                    {
                        // 使用可配置模型：通过 FaceDataManager.UseCnnModel 控制
                        // false = HOG快速模型(默认)  true = CNN高精度模型
                        var targetFaceEncoding = FaceRecognitionAPI.CreateFaceData(targetFaceBitmap, null, null);
                        var knownFaces = PersonDatabaseAPI.GetFaceData().Select(t => t.ConvertToRawPersonDataBase()).ToList();
                        
                        if (knownFaces.Count == 0)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                StatusMessage = "人脸数据库为空";
                            });
                            return false;
                        }

                        var result = FaceRecognitionAPI.CompareFace(knownFaces, targetFaceEncoding);

                        if (result.Count > 0)
                        {
                            var matchedPerson = result.First();
                            var userName = matchedPerson.Name ?? "未知";
                            var userId = matchedPerson.ClassID;
                            var displayText = userId.HasValue ? $"{userName} (ID:{userId.Value})" : userName;
                            
                            // 如果指定了目标用户ID，检查是否匹配
                            if (TargetUserId.HasValue)
                            {
                                if (userId == TargetUserId.Value)
                                {
                                    // 权限通过
                                    Dispatcher.Invoke(() =>
                                    {
                                        ShowGrantedMessage = true;
                                        GrantedUserText = displayText;
                                        StatusMessage = $"验证通过: {displayText}";
                                    });

                                    // 延迟后返回结果并退出
                                    Task.Delay(1500).ContinueWith(_ =>
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            StopCamera();
                                            OnAuthSuccess?.Invoke(new AuthResult
                                            {
                                                Success = true,
                                                UserName = matchedPerson.Name,
                                                UserId = matchedPerson.ClassID,
                                                AuthId = matchedPerson.StudentID,
                                                AuthTime = DateTime.Now
                                            });
                                        });
                                    });
                                    return true;
                                }
                                else
                                {
                                    // 权限不够 - 显示提示但继续识别
                                    Dispatcher.Invoke(() =>
                                    {
                                        ShowDeniedMessage = true;
                                        DeniedUserText = displayText;
                                        _lastDeniedTime = DateTime.Now;
                                        StatusMessage = $"{userName} 权限不够";
                                    });
                                    return false;
                                }
                            }
                            else
                            {
                                // 未指定目标用户，任何识别到的用户都通过
                                Dispatcher.Invoke(() =>
                                {
                                    ShowGrantedMessage = true;
                                    GrantedUserText = displayText;
                                    StatusMessage = $"验证通过: {displayText}";
                                });

                                Task.Delay(1500).ContinueWith(_ =>
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        StopCamera();
                                        OnAuthSuccess?.Invoke(new AuthResult
                                        {
                                            Success = true,
                                            UserName = matchedPerson.Name,
                                            UserId = matchedPerson.ClassID,
                                            AuthId = matchedPerson.StudentID,
                                            AuthTime = DateTime.Now
                                        });
                                    });
                                });
                                return true;
                            }
                        }
                        else
                        {
                            Dispatcher.Invoke(() =>
                            {
                                StatusMessage = "未识别到已知人脸";
                            });
                        }
                    }
                }
                finally
                {
                    targetFaceBitmapModels.RetangleImage?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"识别错误: {ex.Message}";
                });
            }
            return false;
        }

        public void StopCamera()
        {
            _cameraCts?.Cancel();
            _faceDetectedCount = 0;
        }
    }
}
