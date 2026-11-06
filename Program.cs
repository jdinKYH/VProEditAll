using System;
using System.Threading;
using System.Windows.Forms;

namespace VProEditAll
{
    internal static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 261105@Claude [보완] 예기치 않은 오류로 프로그램이 바로 종료되지 않도록 전역 처리
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// UI 스레드 예외: 메시지만 보여 주고 프로그램은 계속 실행
        /// </summary>
        private static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            MessageBox.Show($"예기치 않은 오류가 발생했습니다:\n{e.Exception.Message}\n\n작업 중인 파일이 있으면 다른 이름으로 저장해 두세요.",
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 그 외 스레드 예외: 복구할 수 없으므로 내용만 알림 (이후 종료됨)
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            MessageBox.Show($"복구할 수 없는 오류가 발생해 프로그램을 종료합니다:\n{(ex != null ? ex.Message : e.ExceptionObject?.ToString())}",
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
