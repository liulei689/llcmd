using CheckInProject.App.Pages;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace CheckInProject.App
{
    /// <summary>
    /// 权限验证全屏窗口
    /// </summary>
    public partial class AuthWindow : Window
    {
        private readonly IServiceProvider ServiceProvider;
        private AuthVerifyPage? AuthPage;

        /// <summary>
        /// 验证结果
        /// </summary>
        public AuthResult? AuthResult { get; private set; }

        /// <summary>
        /// 目标验证用户ID
        /// </summary>
        public uint? TargetUserId { get; set; } = null;

        public AuthWindow(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            InitializeComponent();
            
            // 注册键盘事件
            KeyDown += AuthWindow_KeyDown;
        }

        private void AuthWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                // ESC 退出，返回失败
                AuthResult = new AuthResult
                {
                    Success = false,
                    ErrorMessage = "用户取消验证",
                    AuthTime = DateTime.Now
                };
                DialogResult = false;
                Close();
            }
        }

        public void StartAuth()
        {
            AuthPage = ServiceProvider.GetRequiredService<AuthVerifyPage>();
            AuthPage.OnAuthSuccess = OnAuthSuccess;
            AuthPage.OnAuthFailed = OnAuthFailed;
            
            AuthFrame.Navigate(AuthPage);
            AuthPage.StartAuth(TargetUserId);
        }

        private void OnAuthSuccess(AuthResult result)
        {
            AuthResult = result;
            DialogResult = true;
            Close();
        }

        private void OnAuthFailed(AuthResult result)
        {
            AuthResult = result;
            DialogResult = false;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            AuthPage?.StopCamera();
            base.OnClosing(e);
        }
    }
}
