using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ChudoObuv.Tests
{
    public static class IntegrationTests
    {
        private static string connectionString;
        private static Store store;
        private static int passed;
        private static User admin;
        private static User manager;
        private static User customer;
        private static object Sql(string query)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(query, connection))
                {
                    return command.ExecuteScalar();
                }
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception(message);
            }
        }

        private static void Reject(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            throw new Exception("Expected business-rule rejection.");
        }

        private static void Test(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }

        private static CartLine Line(int stockId, int quantity, DateTime date)
        {
            Product product = store.GetProducts(date).Single(p => store.GetStock(p.Id).Any(s => s.Id == stockId));
            var size = store.GetStock(product.Id).Single(s => s.Id == stockId);
            return new CartLine
            {
                StockId = stockId,
                ProductId = product.Id,
                Name = product.Name,
                Manufacturer = product.Manufacturer,
                Size = size.Size,
                UnitPrice = product.Price,
                Quantity = quantity
            };
        }

        private static void Pump()
        {
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate
            {
            }));
        }

        private static void Render(FrameworkElement element, string name, int width, int height)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
            Pump();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string directory = Environment.GetEnvironmentVariable("CHUDOOBUV_PREVIEW_DIR");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            using (var stream = File.Create(Path.Combine(directory, name + ".png")))
            {
                encoder.Save(stream);
            }
        }

        private static void Modal(Window owner, Action open, Action<Window> interact)
        {
            Exception failure = null;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
            {
                Window dialog = Application.Current.Windows.Cast<Window>().LastOrDefault(w => w != owner && w.IsVisible);
                try
                {
                    Assert(dialog != null, "dialog opened");
                    interact(dialog);
                }
                catch (Exception error)
                {
                    failure = error;
                }
                finally
                {
                    if (dialog != null && dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                }
            }));
            open();
            if (failure != null)
            {
                throw failure;
            }
        }

        private static void Click(FrameworkElement root, string name)
        {
            Ui.Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        [STAThread]
        public static int Main()
        {
            try
            {
                connectionString = Environment.GetEnvironmentVariable("CHUDOOBUV_TEST_CONNECTION_STRING");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new Exception("Set CHUDOOBUV_TEST_CONNECTION_STRING to a disposable Verification/Test database.");
                }

                var builder = new SqlConnectionStringBuilder(connectionString);
                if (builder.InitialCatalog.IndexOf("Verification", StringComparison.OrdinalIgnoreCase) < 0 && builder.InitialCatalog.IndexOf("Test", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new Exception("Refusing to modify a database without Test/Verification in its name.");
                }

                store = new Store(connectionString);
                admin = store.Login("isivanov");
                manager = store.Login("papetrov");
                customer = store.Login("asidorova");
                DateTime date = new DateTime(2026, 10, 16);
                Test("seed counts and financial reconciliation", delegate
                {
                    Assert(store.GetProducts(date).Count == 31, "31 products");
                    Assert((int)Sql("SELECT COUNT(*) FROM dbo.users") == 20, "20 users");
                    Assert((int)Sql("SELECT COUNT(*) FROM dbo.stock_items") == 92, "92 variants");
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == 1274, "1274 pairs");
                    Assert(store.GetOrders(admin).Count == 10, "10 orders");
                    Assert(store.GetOrders(admin).Sum(x => x.Total) == 341770m, "historical total");
                });
                Test("login, role mapping and unknown login", delegate
                {
                    Assert(admin.IsAdmin && manager.IsStaff && !customer.IsStaff, "roles");
                    Reject(delegate
                    {
                        store.Login("unknown' OR 1=1 --");
                    });
                    Reject(delegate
                    {
                        store.GetOrders(customer);
                    });
                    Reject(delegate
                    {
                        store.GetUsers(customer);
                    });
                    Reject(delegate
                    {
                        store.GetOrderLines(null, 1);
                    });
                });
                Test("calendar boundaries January, leap year and half-kopeck rounding", delegate
                {
                    Assert(Pricing.PreviousMonthStart(new DateTime(2026, 1, 31)) == new DateTime(2025, 12, 1), "January");
                    Assert(Pricing.PreviousMonthStart(new DateTime(2024, 3, 1)) == new DateTime(2024, 2, 1), "leap year");
                    Assert(Pricing.DiscountedKopecks(101, true) == 76, "rounding");
                    Assert(Pricing.DiscountedKopecks(102, true) == 77, "half rounding");
                    Assert(store.GetProducts(date).All(p => p.HasDiscount), "no September orders");
                    Assert(store.GetProducts(new DateTime(2026, 5, 1)).Any(p => !p.HasDiscount), "April sales disable discount in May");
                    Assert(store.GetProducts(new DateTime(2026, 4, 30)).All(p => p.HasDiscount), "current-month orders do not remove discount");
                });
                Test("quantity validation", delegate
                {
                    foreach (string value in new[]
                    {
                        "0",
                        "-1",
                        "1.5",
                        "",
                        "10001",
                        "x",
                        " 1",
                        "2147483648"
                    }

                    )
                    {
                        Reject(delegate
                        {
                            Pricing.ParseQuantity(value);
                        });
                    }

                    Assert(Pricing.ParseQuantity("12") == 12, "valid integer");
                });
                Test("create multi-line order, historical price and return stock", delegate
                {
                    int before = (int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items");
                    int id = store.CreateOrder(customer, customer.Id, new[] { Line(1, 2, date), Line(2, 1, date) }, date);
                    var lines = store.GetOrderLines(admin, id);
                    Assert(lines.Count == 2 && lines.Sum(x => x.Quantity) == 3, "two lines");
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == before - 3, "stock reduced");
                    decimal total = lines.Sum(x => x.Total);
                    store.ChangeOrderDate(admin, id, new DateTime(2026, 9, 15));
                    Assert(!store.GetProducts(date).Single(p => p.Id == lines.Select(x => Line(1, 1, date).ProductId).First()).HasDiscount, "date affects discount");
                    Assert(store.GetOrderLines(admin, id).Sum(x => x.Total) == total, "historical price stable");
                    store.DeleteOrder(admin, id);
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == before, "stock restored");
                });
                Test("permission checks on all mutations", delegate
                {
                    Reject(delegate
                    {
                        store.CreateOrder(null, customer.Id, new[] { Line(1, 1, date) }, date);
                    });
                    Reject(delegate
                    {
                        store.CreateOrder(customer, admin.Id, new[] { Line(1, 1, date) }, date);
                    });
                    Reject(delegate
                    {
                        store.ChangeOrderDate(manager, 1, date);
                    });
                    Reject(delegate
                    {
                        store.DeleteOrderLine(manager, 1, 1);
                    });
                    Reject(delegate
                    {
                        store.DeleteOrder(customer, 1);
                    });
                    // Forging Role on the UI object does not change the stored role.
                    Reject(delegate
                    {
                        store.DeleteOrder(new User { Id = customer.Id, Role = "admin" }, 1);
                    });
                });
                Test("manager creates for another user; admin deletes a line", delegate
                {
                    int before = (int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items");
                    int id = store.CreateOrder(manager, customer.Id, new[] { Line(1, 2, date), Line(2, 1, date) }, date);
                    var line = store.GetOrderLines(admin, id).First();
                    store.DeleteOrderLine(admin, id, line.Id);
                    Assert(store.GetOrderLines(admin, id).Count == 1, "one line left");
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == before - 1, "line stock returned");
                    Reject(delegate
                    {
                        store.DeleteOrderLine(admin, id, store.GetOrderLines(admin, id).Single().Id);
                    });
                    store.DeleteOrder(manager, id);
                    Reject(delegate
                    {
                        store.DeleteOrder(manager, id);
                    });
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == before, "no double stock return");
                });
                Test("transaction rolls back after insufficient stock in second line", delegate
                {
                    int before = (int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items");
                    int orders = store.GetOrders(admin).Count;
                    Reject(delegate
                    {
                        store.CreateOrder(customer, customer.Id, new[] { Line(1, 1, date), Line(2, 10000, date) }, date);
                    });
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == before, "first line rolled back");
                    Assert(store.GetOrders(admin).Count == orders, "header rolled back");
                });
                Test("empty order, negative quantity and stale price rejected", delegate
                {
                    Reject(delegate
                    {
                        store.CreateOrder(customer, customer.Id, new CartLine[0], date);
                    });
                    Reject(delegate
                    {
                        store.CreateOrder(customer, customer.Id, new[] { Line(1, -1, date) }, date);
                    });
                    var line = Line(1, 1, date);
                    line.UnitPrice += 1;
                    Reject(delegate
                    {
                        store.CreateOrder(customer, customer.Id, new[] { line }, date);
                    });
                });
                Test("MSSQL foreign keys, unique and CHECK constraints", delegate
                {
                    int rejected = 0;
                    foreach (string query in new[]
                    {
                        "INSERT dbo.orders(order_date,customer_id) VALUES('20261016',999999)",
                        "INSERT dbo.sizes(value) SELECT TOP(1) value FROM dbo.sizes",
                        "UPDATE dbo.stock_items SET available_quantity=-1 WHERE stock_item_id=1"
                    }

                    )
                    {
                        try
                        {
                            Sql(query);
                        }
                        catch (SqlException)
                        {
                            rejected++;
                        }
                    }

                    Assert(rejected == 3, "FK/UQ/CHECK enforced");
                });
                Test("concurrent checkout of last pair", delegate
                {
                    int original = (int)Sql("SELECT available_quantity FROM dbo.stock_items WHERE stock_item_id=1");
                    Sql("UPDATE dbo.stock_items SET available_quantity=1 WHERE stock_item_id=1");
                    var line = Line(1, 1, date);
                    var gate = new ManualResetEventSlim(false);
                    var ids = new List<int>();
                    int rejected = 0;
                    Action checkout = delegate
                    {
                        gate.Wait();
                        try
                        {
                            int id = store.CreateOrder(customer, customer.Id, new[] { line }, date);
                            lock (ids)
                            {
                                ids.Add(id);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            Interlocked.Increment(ref rejected);
                        }
                        catch (SqlException error)
                        {
                            if (error.Number != 1205)
                            {
                                throw;
                            }

                            Interlocked.Increment(ref rejected);
                        }
                    };
                    var first = Task.Run(checkout);
                    var second = Task.Run(checkout);
                    gate.Set();
                    Task.WaitAll(first, second);
                    Assert(ids.Count == 1 && rejected == 1, "exactly one checkout succeeds");
                    Assert((int)Sql("SELECT available_quantity FROM dbo.stock_items WHERE stock_item_id=1") == 0, "no overselling");
                    foreach (int id in ids)
                    {
                        store.DeleteOrder(admin, id);
                    }

                    Sql("UPDATE dbo.stock_items SET available_quantity=" + original + " WHERE stock_item_id=1");
                });
                var application = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                Ui.InitializeResources();
                Test("all embedded WPF XAML forms load and render", delegate
                {
                    foreach (string name in new[]
                    {
                        "LoginView",
                        "ProductView",
                        "CartView",
                        "OrdersView"
                    }

                    )
                    {
                        var content = (FrameworkElement)Ui.Load(name + ".xaml");
                        if (name == "ProductView")
                        {
                            content.DataContext = store.GetProducts(date).First();
                        }

                        Render(content, name, 1060, 760);
                    }
                });
                Test("catalog, live combined filters and role visibility", delegate
                {
                    var window = new MainWindow(store);
                    window.Show();
                    Pump();
                    var root = window.View;
                    Assert(Ui.Find<ListBox>(root, "ProductsList").Items.Count == 31, "guest catalog");
                    Assert(!Ui.Find<StackPanel>(root, "FiltersPanel").IsEnabled, "guest filters disabled");
                    Render(root, "catalog-guest", 1200, 800);
                    window.SetUser(customer);
                    Assert(Ui.Find<Button>(root, "OrdersButton").Visibility == Visibility.Collapsed, "customer cannot access orders");
                    Ui.Find<ComboBox>(root, "SortBox").SelectedIndex = 2;
                    Ui.Find<ComboBox>(root, "CategoryBox").SelectedIndex = 1;
                    Ui.Find<TextBox>(root, "SearchBox").Text = "кроссовки";
                    var filtered = Ui.Find<ListBox>(root, "ProductsList").Items.Cast<Product>().ToList();
                    Assert(filtered.Count > 0 && filtered.All(p => p.CategoryId == store.GetCategories()[1].Id), "combined category and search");
                    Assert(filtered.SequenceEqual(filtered.OrderByDescending(p => p.Price).ThenBy(p => p.Id)), "descending sort preserved");
                    Render(root, "catalog-filtered", 1200, 800);
                    Ui.Find<TextBox>(root, "SearchBox").Text = "not present 12345";
                    Assert(Ui.Find<TextBlock>(root, "EmptyText").Visibility == Visibility.Visible, "empty result state");
                    window.SetUser(admin);
                    Assert(Ui.Find<Button>(root, "OrdersButton").Visibility == Visibility.Visible, "admin orders visible");
                    Assert(Ui.Find<TextBlock>(root, "UserName").Text == admin.FullName, "full name top right");
                    Render(root, "catalog-admin", 1200, 800);
                    window.Close();
                });
                Test("interactive WPF login, product, cart and order editing", delegate
                {
                    var window = new MainWindow(store);
                    window.Show();
                    Pump();
                    var root = window.View;
                    Modal(window, delegate
                    {
                        Click(root, "LoginButton");
                    }, delegate (Window dialog)
                    {
                        var content = (FrameworkElement)dialog.Content;
                        Ui.Find<TextBox>(content, "LoginBox").Text = "asidorova";
                        Render(content, "login-live", 700, 480);
                        Click(content, "SignIn");
                    });
                    Assert(Ui.Find<TextBlock>(root, "UserName").Text == customer.FullName, "login event wired");
                    var list = Ui.Find<ListBox>(root, "ProductsList");
                    list.SelectedIndex = 0;
                    Modal(window, delegate
                    {
                        list.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                    }, delegate (Window dialog)
                    {
                        var content = (FrameworkElement)dialog.Content;
                        Assert(Ui.Find<ComboBox>(content, "SizeBox").Items.Count > 0, "available sizes loaded");
                        Assert(!string.IsNullOrWhiteSpace(Ui.Find<TextBlock>(content, "SizeRange").Text), "size range loaded");
                        Render(content, "product-live", 940, 750);
                        Click(content, "Add");
                    });
                    Assert(Ui.Find<Button>(root, "CartButton").Content.ToString() == "Корзина (1)", "product added");
                    Modal(window, delegate
                    {
                        Click(root, "CartButton");
                    }, delegate (Window dialog)
                    {
                        var content = (FrameworkElement)dialog.Content;
                        var grid = Ui.Find<DataGrid>(content, "CartGrid");
                        Assert(grid.Items.Count == 1, "cart line shown");
                        grid.SelectedIndex = 0;
                        Ui.Find<TextBox>(content, "QuantityBox").Text = "2";
                        Click(content, "ChangeQuantity");
                        Assert(((CartLine)grid.Items[0]).Quantity == 2, "cart quantity event wired");
                        Render(content, "cart-live", 1040, 690);
                        grid.SelectedIndex = 0;
                        Click(content, "RemoveLine");
                        Assert(grid.Items.Count == 0 && !Ui.Find<Button>(content, "ConfirmOrder").IsEnabled, "empty cart cannot confirm");
                        Click(content, "Back");
                    });
                    Click(root, "LoginButton");
                    Assert(Ui.Find<TextBlock>(root, "UserName").Text == "Гость", "logout event wired");
                    window.SetUser(admin);
                    Modal(window, delegate
                    {
                        Click(root, "OrdersButton");
                    }, delegate (Window dialog)
                    {
                        var content = (FrameworkElement)dialog.Content;
                        var grid = Ui.Find<DataGrid>(content, "OrdersGrid");
                        Assert(grid.Items.Count == 10, "orders loaded");
                        Assert(Ui.Find<DataGrid>(content, "LinesGrid").Items.Count > 0, "lines loaded");
                        var order = (Order)grid.SelectedItem;
                        var picker = Ui.Find<DatePicker>(content, "OrderDate");
                        picker.SelectedDate = order.Date.AddDays(1);
                        Click(content, "SaveDate");
                        Assert(((Order)grid.SelectedItem).Date == order.Date.AddDays(1), "date edit event wired");
                        picker.SelectedDate = order.Date;
                        Click(content, "SaveDate");
                        Render(content, "orders-live", 1110, 750);
                        Click(content, "Back");
                    });
                    window.SetUser(manager);
                    Modal(window, delegate
                    {
                        Click(root, "OrdersButton");
                    }, delegate (Window dialog)
                    {
                        var content = (FrameworkElement)dialog.Content;
                        Assert(Ui.Find<StackPanel>(content, "AdminPanel").Visibility == Visibility.Collapsed, "manager cannot see administrator controls");
                        Assert(Ui.Find<DataGrid>(content, "OrdersGrid").Items.Count == 10, "manager can see orders");
                        Render(content, "orders-manager", 1110, 750);
                        Click(content, "Back");
                    });
                    window.Close();
                });
                Test("final reconciliation after all mutations", delegate
                {
                    Assert((int)Sql("SELECT SUM(available_quantity) FROM dbo.stock_items") == 1274, "stock restored");
                    Assert(store.GetOrders(admin).Count == 10, "seed orders unchanged");
                    Assert(store.GetOrders(admin).Sum(x => x.Total) == 341770m, "seed totals unchanged");
                });
                Console.WriteLine("All " + passed + " checks passed against " + builder.InitialCatalog);
                application.Shutdown();
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
