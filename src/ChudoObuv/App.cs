using System;
using System.Configuration;
using System.Threading;
using System.Windows;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
namespace ChudoObuv
{
    public sealed class App : Application
    {
        [STAThread]
        public static void Main()
        {
            Thread.CurrentThread.CurrentCulture = Ui.Russian;
            Thread.CurrentThread.CurrentUICulture = Ui.Russian;
            var app = new App();
            app.DispatcherUnhandledException += delegate (object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
            {
                Ui.ShowError(app.MainWindow, args.Exception);
                args.Handled = true;
            };
            try
            {
                Ui.InitializeResources();
                string connection = Environment.GetEnvironmentVariable("CHUDOOBUV_CONNECTION_STRING");
                if (string.IsNullOrWhiteSpace(connection))
                {
                    var setting = ConfigurationManager.ConnectionStrings["ChudoObuv"];
                    if (setting == null)
                    {
                        throw new InvalidOperationException("В ChudoObuv.exe.config отсутствует строка подключения ChudoObuv. Восстановите конфигурацию из src/ChudoObuv/App.config и укажите сервер по инструкции START.md.");
                    }

                    connection = setting.ConnectionString;
                }

                var window = new MainWindow(new Store(connection));
                app.Run(window);
            }
            catch (Exception error)
            {
                Ui.ShowError(null, error);
                Environment.ExitCode = 1;
            }
        }
    }
}
