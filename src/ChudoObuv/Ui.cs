using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace ChudoObuv
{
    public static class Ui
    {
        public static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
        public static string BaseDirectory
        {
            get
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        public static object Load(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChudoObuv.Views." + name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Не найден встроенный интерфейс " + name);
                }

                return XamlReader.Load(stream);
            }
        }

        public static void InitializeResources()
        {
            Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)Load("Theme.xaml"));
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
        }

        public static BitmapImage Image(string relative)
        {
            string assets = Path.GetFullPath(Path.Combine(BaseDirectory, "Assets")) + Path.DirectorySeparatorChar;
            string path = string.IsNullOrEmpty(relative) ? "" : Path.GetFullPath(Path.Combine(assets, relative));
            if (!path.StartsWith(assets, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                path = Path.Combine(assets, "picture.png");
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception)
            {
                if (!path.EndsWith("picture.png", StringComparison.OrdinalIgnoreCase))
                {
                    return Image(null);
                }

                return null;
            }
        }

        public static T Find<T>(FrameworkElement root, string name)
            where T : class
        {
            return (T)root.FindName(name);
        }

        public static Window Dialog(Window owner, string title, FrameworkElement content, double width, double height)
        {
            return new Window
            {
                Title = "Чудо Обувь — " + title,
                Owner = owner,
                Content = content,
                Width = width,
                Height = height,
                MinWidth = Math.Min(width, 720),
                MinHeight = Math.Min(height, 520),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Image("app.ico"),
                FontFamily = new System.Windows.Media.FontFamily("Calibri"),
                FontSize = 15,
                Background = System.Windows.Media.Brushes.White
            };
        }

        public static void Guard(Window owner, Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                ShowError(owner, error);
            }
        }

        public static void ShowError(Window owner, Exception error)
        {
            string message = error.Message;
            if (error is System.Data.SqlClient.SqlException)
            {
                var sql = (System.Data.SqlClient.SqlException)error;
                message = sql.Number == 1205 ? "Другой пользователь изменяет эти данные. Повторите действие. Изменения не сохранены." : "Не удалось выполнить операцию MSSQL. Проверьте доступность сервера и строку подключения в ChudoObuv.exe.config. " + "При первом запуске выполните Initialize-Database.ps1 по инструкции START.md.\n\nПодробности: " + error.Message;
            }

            MessageBox.Show(owner, message, "Чудо Обувь — ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static bool Confirm(Window owner, string message)
        {
            return MessageBox.Show(owner, message, "Чудо Обувь — подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        public static void NumericInput(TextBox box)
        {
            box.PreviewTextInput += delegate (object sender, TextCompositionEventArgs args)
            {
                args.Handled = !Regex.IsMatch(args.Text, "^[0-9]+$");
            };
            DataObject.AddPastingHandler(box, delegate (object sender, DataObjectPastingEventArgs args)
            {
                if (!args.SourceDataObject.GetDataPresent(DataFormats.Text) || !Regex.IsMatch((string)args.SourceDataObject.GetData(DataFormats.Text), "^[0-9]+$"))
                {
                    args.CancelCommand();
                }
            });
            box.MaxLength = 5;
        }
    }
}
