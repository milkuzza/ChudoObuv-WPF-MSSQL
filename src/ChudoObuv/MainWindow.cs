using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace ChudoObuv
{
    public sealed class MainWindow : Window
    {
        private readonly Store store;
        private readonly FrameworkElement view;
        private readonly List<CartLine> cart = new List<CartLine>();
        private User user;
        private List<Product> products = new List<Product>();
        private ICollectionView productView;
        private bool ready;
        public FrameworkElement View { get { return view; } }

        public MainWindow(Store store)
        {
            this.store = store;
            Title = "Чудо Обувь — каталог обуви";
            Width = 1220;
            Height = 840;
            MinWidth = 980;
            MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Icon = Ui.Image("app.ico");
            view = (FrameworkElement)Ui.Load("MainView.xaml");
            Content = view;
            Ui.Find<Image>(view, "Logo").Source = Ui.Image("logo.png");
            Ui.Find<Button>(view, "LoginButton").Click += delegate { Ui.Guard(this, LoginOrLogout); };
            Ui.Find<Button>(view, "CartButton").Click += delegate { Ui.Guard(this, ShowCart); };
            Ui.Find<Button>(view, "OrdersButton").Click += delegate { Ui.Guard(this, ShowOrders); };
            Ui.Find<Button>(view, "RefreshButton").Click += delegate { Ui.Guard(this, Reload); };
            Ui.Find<TextBox>(view, "SearchBox").TextChanged += delegate { ApplyFilters(); };
            Ui.Find<ComboBox>(view, "CategoryBox").SelectionChanged += delegate { ApplyFilters(); };
            Ui.Find<ComboBox>(view, "SortBox").SelectionChanged += delegate { ApplyFilters(); };
            Ui.Find<ListBox>(view, "ProductsList").PreviewMouseLeftButtonUp += OpenSelectedProduct;
            Ui.Find<ListBox>(view, "ProductsList").KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Enter)
                {
                    Ui.Guard(this, ShowProduct);
                }
            };
            Loaded += delegate { Ui.Guard(this, Reload); };
            Closing += delegate(object sender, CancelEventArgs args)
            {
                if (cart.Count > 0 && !Ui.Confirm(this, "В корзине есть неоформленные позиции. Закрыть приложение и очистить корзину?"))
                {
                    args.Cancel = true;
                }
            };
            UpdateSession();
        }

        private void OpenSelectedProduct(object sender, MouseButtonEventArgs args)
        {
            DependencyObject source = args.OriginalSource as DependencyObject;
            var item = ItemsControl.ContainerFromElement(Ui.Find<ListBox>(view, "ProductsList"), source) as ListBoxItem;
            if (item != null)
            {
                Ui.Find<ListBox>(view, "ProductsList").SelectedItem = item.Content;
                Ui.Guard(this, ShowProduct);
            }
        }

        public void Reload()
        {
            int selectedCategory = Ui.Find<ComboBox>(view, "CategoryBox").SelectedValue is int ? (int)Ui.Find<ComboBox>(view, "CategoryBox").SelectedValue : 0;
            ready = false;
            products = store.GetProducts(DateTime.Today);
            Ui.Find<ComboBox>(view, "CategoryBox").ItemsSource = store.GetCategories();
            Ui.Find<ComboBox>(view, "CategoryBox").SelectedValue = selectedCategory;
            if (Ui.Find<ComboBox>(view, "CategoryBox").SelectedIndex < 0)
            {
                Ui.Find<ComboBox>(view, "CategoryBox").SelectedIndex = 0;
            }
            productView = CollectionViewSource.GetDefaultView(products);
            Ui.Find<ListBox>(view, "ProductsList").ItemsSource = productView;
            ready = true;
            ApplyFilters();
            Ui.Find<TextBlock>(view, "StatusText").Text = "";
            UpdateCartLabel();
        }

        private void ApplyFilters()
        {
            if (!ready || productView == null)
            {
                return;
            }
            string text = user == null ? "" : Ui.Find<TextBox>(view, "SearchBox").Text.Trim();
            object selected = Ui.Find<ComboBox>(view, "CategoryBox").SelectedValue;
            int categoryId = user == null || !(selected is int) ? 0 : (int)selected;
            productView.Filter = delegate(object value)
            {
                var product = (Product)value;
                bool matches = Ui.Russian.CompareInfo.IndexOf(product.Name, text, System.Globalization.CompareOptions.IgnoreCase) >= 0 ||
                    Ui.Russian.CompareInfo.IndexOf(product.Description, text, System.Globalization.CompareOptions.IgnoreCase) >= 0;
                return (categoryId == 0 || product.CategoryId == categoryId) && matches;
            };
            productView.SortDescriptions.Clear();
            int sort = user == null ? 0 : Ui.Find<ComboBox>(view, "SortBox").SelectedIndex;
            if (sort > 0)
            {
                productView.SortDescriptions.Add(new SortDescription("Price", sort == 1 ? ListSortDirection.Ascending : ListSortDirection.Descending));
            }
            productView.SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Ascending));
            Ui.Find<TextBlock>(view, "CountText").Text = "Моделей: " + productView.Cast<Product>().Count() + " из " + products.Count;
            Ui.Find<TextBlock>(view, "EmptyText").Visibility = productView.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetUser(User value)
        {
            user = value;
            cart.Clear();
            Ui.Find<TextBox>(view, "SearchBox").Text = "";
            Ui.Find<ComboBox>(view, "CategoryBox").SelectedValue = 0;
            Ui.Find<ComboBox>(view, "SortBox").SelectedIndex = 0;
            UpdateSession();
            ApplyFilters();
        }

        private void UpdateSession()
        {
            Ui.Find<TextBlock>(view, "UserName").Text = user == null ? "Гость" : user.FullName;
            Ui.Find<TextBlock>(view, "UserRole").Text = user == null ? "Просмотр каталога" : user.RoleName;
            Ui.Find<Button>(view, "LoginButton").Content = user == null ? "Войти по логину" : "Выйти";
            Ui.Find<StackPanel>(view, "FiltersPanel").IsEnabled = user != null;
            Ui.Find<Button>(view, "CartButton").IsEnabled = user != null;
            Ui.Find<Button>(view, "OrdersButton").Visibility = user != null && user.IsStaff ? Visibility.Visible : Visibility.Collapsed;
            UpdateCartLabel();
        }

        private void UpdateCartLabel()
        {
            Ui.Find<Button>(view, "CartButton").Content = "Корзина (" + cart.Sum(x => x.Quantity) + ")";
        }

        private void LoginOrLogout()
        {
            if (user != null)
            {
                if (cart.Count == 0 || Ui.Confirm(this, "Выйти из учётной записи? Неоформленная корзина будет очищена."))
                {
                    SetUser(null);
                }
                return;
            }
            var content = (FrameworkElement)Ui.Load("LoginView.xaml");
            var dialog = Ui.Dialog(this, "вход по логину", content, 720, 520);
            var input = Ui.Find<TextBox>(content, "LoginBox");
            Action login = delegate
            {
                Ui.Guard(dialog, delegate
                {
                    SetUser(store.Login(input.Text));
                    dialog.DialogResult = true;
                });
            };
            Ui.Find<Button>(content, "SignIn").Click += delegate { login(); };
            Ui.Find<Button>(content, "Back").Click += delegate { dialog.Close(); };
            input.KeyDown += delegate(object sender, KeyEventArgs args) { if (args.Key == Key.Enter) { login(); } };
            dialog.Loaded += delegate { input.Focus(); };
            dialog.ShowDialog();
        }

        private void ShowProduct()
        {
            var selected = Ui.Find<ListBox>(view, "ProductsList").SelectedItem as Product;
            if (selected == null)
            {
                return;
            }
            Product product = store.GetProducts(DateTime.Today).Single(x => x.Id == selected.Id);
            var sizes = store.GetStock(product.Id);
            var content = (FrameworkElement)Ui.Load("ProductView.xaml");
            content.DataContext = product;
            var dialog = Ui.Dialog(this, "модель обуви", content, 960, 790);
            Ui.Find<TextBlock>(content, "SizeRange").Text = string.Join(", ", sizes.Select(x => x.Size.ToString("0.#", Ui.Russian)));
            var selector = Ui.Find<ComboBox>(content, "SizeBox");
            selector.ItemsSource = sizes.Where(x => x.Available > 0).ToList();
            selector.SelectedIndex = 0;
            var quantity = Ui.Find<TextBox>(content, "QuantityBox");
            Ui.NumericInput(quantity);
            Ui.Find<Button>(content, "Add").IsEnabled = user != null && selector.Items.Count > 0;
            Ui.Find<TextBlock>(content, "AddHint").Text = user == null ? "Войдите по логину, чтобы добавить товар в заказ." : "Выберите доступный размер и количество пар.";
            Ui.Find<Button>(content, "Back").Click += delegate { dialog.Close(); };
            Ui.Find<Button>(content, "Add").Click += delegate
            {
                Ui.Guard(dialog, delegate
                {
                    var size = selector.SelectedItem as StockOption;
                    if (size == null)
                    {
                        throw new InvalidOperationException("Выберите доступный размер.");
                    }
                    int count = Pricing.ParseQuantity(quantity.Text);
                    var current = store.GetStock(product.Id).Single(x => x.Id == size.Id);
                    var existing = cart.FirstOrDefault(x => x.StockId == size.Id);
                    if (count + (existing == null ? 0 : existing.Quantity) > current.Available)
                    {
                        throw new InvalidOperationException("Недостаточно пар этого размера с учётом корзины. Доступно: " + current.Available + ". Уменьшите количество.");
                    }
                    if (existing == null)
                    {
                        cart.Add(new CartLine { StockId = size.Id, ProductId = product.Id, Name = product.Name, Manufacturer = product.Manufacturer, Size = size.Size, Quantity = count, UnitPrice = product.Price });
                    }
                    else
                    {
                        existing.Quantity += count;
                    }
                    UpdateCartLabel();
                    dialog.DialogResult = true;
                });
            };
            dialog.ShowDialog();
        }

        private void ShowCart()
        {
            if (user == null)
            {
                throw new InvalidOperationException("Войдите по логину для оформления заказа.");
            }
            var currentProducts = store.GetProducts(DateTime.Today).ToDictionary(x => x.Id);
            foreach (var line in cart)
            {
                line.UnitPrice = currentProducts[line.ProductId].Price;
            }
            var content = (FrameworkElement)Ui.Load("CartView.xaml");
            var dialog = Ui.Dialog(this, "корзина и оформление заказа", content, 1060, 730);
            var grid = Ui.Find<DataGrid>(content, "CartGrid");
            var quantity = Ui.Find<TextBox>(content, "QuantityBox");
            Ui.NumericInput(quantity);
            var customers = Ui.Find<ComboBox>(content, "CustomerBox");
            customers.ItemsSource = user.IsStaff ? store.GetUsers(user) : new List<User> { user };
            customers.SelectedValue = user.Id;
            customers.IsEnabled = user.IsStaff;
            Action refresh = delegate
            {
                grid.ItemsSource = cart.ToList();
                Ui.Find<TextBlock>(content, "TotalText").Text = cart.Sum(x => x.Total).ToString("N2", Ui.Russian) + " ₽";
                Ui.Find<Button>(content, "ConfirmOrder").IsEnabled = cart.Count > 0;
                UpdateCartLabel();
            };
            grid.SelectionChanged += delegate
            {
                var line = grid.SelectedItem as CartLine;
                quantity.Text = line == null ? "1" : line.Quantity.ToString();
            };
            Ui.Find<Button>(content, "ChangeQuantity").Click += delegate
            {
                Ui.Guard(dialog, delegate
                {
                    var line = grid.SelectedItem as CartLine;
                    if (line == null) { throw new InvalidOperationException("Выберите строку корзины."); }
                    int count = Pricing.ParseQuantity(quantity.Text);
                    var size = store.GetStock(line.ProductId).Single(x => x.Id == line.StockId);
                    if (count > size.Available) { throw new InvalidOperationException("Доступно только " + size.Available + " пар. Уменьшите количество."); }
                    line.Quantity = count;
                    refresh();
                });
            };
            Ui.Find<Button>(content, "RemoveLine").Click += delegate
            {
                var line = grid.SelectedItem as CartLine;
                if (line != null) { cart.Remove(line); refresh(); }
            };
            Ui.Find<Button>(content, "Back").Click += delegate { dialog.Close(); };
            Ui.Find<Button>(content, "CancelOrder").Click += delegate
            {
                if (cart.Count == 0 || Ui.Confirm(dialog, "Отменить формирование заказа и очистить корзину?"))
                {
                    cart.Clear();
                    refresh();
                    dialog.Close();
                }
            };
            Ui.Find<Button>(content, "ConfirmOrder").Click += delegate
            {
                Ui.Guard(dialog, delegate
                {
                    var customer = customers.SelectedItem as User;
                    if (customer == null) { throw new InvalidOperationException("Выберите клиента."); }
                    if (!Ui.Confirm(dialog, "Оформить заказ для «" + customer.FullName + "» на сумму " + cart.Sum(x => x.Total).ToString("N2", Ui.Russian) + " ₽?")) { return; }
                    int id = store.CreateOrder(user, customer.Id, cart, DateTime.Today);
                    cart.Clear();
                    refresh();
                    Reload();
                    MessageBox.Show(dialog, "Заказ № " + id + " оформлен. Остатки обновлены.", "Чудо Обувь — заказ оформлен", MessageBoxButton.OK, MessageBoxImage.Information);
                    dialog.DialogResult = true;
                });
            };
            refresh();
            dialog.ShowDialog();
        }

        private void ShowOrders()
        {
            if (user == null || !user.IsStaff) { throw new InvalidOperationException("Просмотр заказов доступен менеджеру и администратору."); }
            var content = (FrameworkElement)Ui.Load("OrdersView.xaml");
            var dialog = Ui.Dialog(this, "управление заказами", content, 1130, 790);
            var ordersGrid = Ui.Find<DataGrid>(content, "OrdersGrid");
            var linesGrid = Ui.Find<DataGrid>(content, "LinesGrid");
            var date = Ui.Find<DatePicker>(content, "OrderDate");
            Ui.Find<StackPanel>(content, "AdminPanel").Visibility = user.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
            Action loadLines = delegate
            {
                var order = ordersGrid.SelectedItem as Order;
                linesGrid.ItemsSource = order == null ? new List<OrderLine>() : store.GetOrderLines(user, order.Id);
                date.SelectedDate = order == null ? (DateTime?)null : order.Date;
                Ui.Find<TextBlock>(content, "OrderTotal").Text = order == null ? "Выберите заказ" : "Заказ № " + order.Id + " · Итого: " + order.Total.ToString("N2", Ui.Russian) + " ₽";
            };
            Action refresh = delegate
            {
                var previous = ordersGrid.SelectedItem as Order;
                var orders = store.GetOrders(user);
                ordersGrid.ItemsSource = orders;
                ordersGrid.SelectedItem = previous == null ? orders.FirstOrDefault() : orders.FirstOrDefault(x => x.Id == previous.Id) ?? orders.FirstOrDefault();
                loadLines();
                Reload();
            };
            ordersGrid.SelectionChanged += delegate { Ui.Guard(dialog, loadLines); };
            Ui.Find<Button>(content, "Back").Click += delegate { dialog.Close(); };
            Ui.Find<Button>(content, "Refresh").Click += delegate { Ui.Guard(dialog, refresh); };
            Ui.Find<Button>(content, "NewOrder").Click += delegate
            {
                dialog.Close();
                Ui.Find<TextBox>(view, "SearchBox").Focus();
                Ui.Find<TextBlock>(view, "StatusText").Text = "Новый заказ: найдите модель, выберите размер, добавьте позиции и откройте корзину. Клиент выбирается в корзине.";
            };
            Ui.Find<Button>(content, "DeleteOrder").Click += delegate
            {
                Ui.Guard(dialog, delegate
                {
                    var order = ordersGrid.SelectedItem as Order;
                    if (order == null) { throw new InvalidOperationException("Выберите заказ."); }
                    if (Ui.Confirm(dialog, "Удалить заказ № " + order.Id + "? Все его позиции будут удалены, количество вернётся в остатки."))
                    {
                        store.DeleteOrder(user, order.Id);
                        refresh();
                    }
                });
            };
            Ui.Find<Button>(content, "SaveDate").Click += delegate
            {
                Ui.Guard(dialog, delegate
                {
                    var order = ordersGrid.SelectedItem as Order;
                    if (order == null || !date.SelectedDate.HasValue) { throw new InvalidOperationException("Выберите заказ и корректную дату."); }
                    store.ChangeOrderDate(user, order.Id, date.SelectedDate.Value);
                    refresh();
                });
            };
            Ui.Find<Button>(content, "DeleteLine").Click += delegate
            {
                Ui.Guard(dialog, delegate
                {
                    var order = ordersGrid.SelectedItem as Order;
                    var line = linesGrid.SelectedItem as OrderLine;
                    if (order == null || line == null) { throw new InvalidOperationException("Выберите позицию в составе заказа."); }
                    if (Ui.Confirm(dialog, "Удалить выбранную позицию? Количество вернётся в остатки, сумма заказа уменьшится."))
                    {
                        store.DeleteOrderLine(user, order.Id, line.Id);
                        refresh();
                    }
                });
            };
            refresh();
            dialog.ShowDialog();
        }
    }
}
