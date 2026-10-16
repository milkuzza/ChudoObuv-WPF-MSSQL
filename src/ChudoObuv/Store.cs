using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ChudoObuv
{
    public sealed class Store
    {
        private readonly string connectionString;
        public Store(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private SqlConnection Open()
        {
            var connection = new SqlConnection(connectionString);
            connection.Open();
            return connection;
        }

        private static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql, params object[] values)
        {
            var command = new SqlCommand(sql, connection, transaction);
            command.CommandTimeout = 30;
            for (int i = 0; i < values.Length; i++)
            {
                command.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
            }

            return command;
        }

        private static User ReadUser(SqlDataReader reader)
        {
            return new User
            {
                Id = reader.GetInt32(0),
                Login = reader.GetString(1),
                FullName = reader.GetString(2),
                Role = reader.GetString(3),
                RoleName = reader.GetString(4)
            };
        }

        private const string UserSql = @"SELECT u.user_id, u.login, CONCAT(u.last_name,N' ',u.first_name,N' ',u.middle_name),r.code,r.name
            FROM dbo.users u JOIN dbo.roles r ON r.role_id=u.role_id";
        public User Login(string login)
        {
            using (var connection = Open())
            using (var command = Command(connection, null, UserSql + " WHERE u.login=@p0", (login ?? "").Trim()))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Логин не найден. Проверьте написание по списку пользователей. Пароль не требуется.");
                }

                return ReadUser(reader);
            }
        }

        // Права проверяются повторно в базе, а не только доступностью кнопок интерфейса.
        private static User RequireUser(SqlConnection connection, SqlTransaction transaction, User actor, bool staff, bool admin)
        {
            if (actor == null)
            {
                throw new InvalidOperationException("Для этого действия войдите по логину.");
            }

            using (var command = Command(connection, transaction, UserSql + " WHERE u.user_id=@p0", actor.Id))
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Пользователь отсутствует в базе. Войдите заново.");
                }

                var current = ReadUser(reader);
                if ((staff && !current.IsStaff) || (admin && !current.IsAdmin))
                {
                    throw new InvalidOperationException("Недостаточно прав для выполнения действия.");
                }

                return current;
            }
        }

        public List<User> GetUsers(User actor)
        {
            using (var connection = Open())
            {
                RequireUser(connection, null, actor, true, false);
                using (var command = Command(connection, null, UserSql + " ORDER BY u.last_name,u.first_name"))
                using (var reader = command.ExecuteReader())
                {
                    var result = new List<User>();
                    while (reader.Read())
                    {
                        result.Add(ReadUser(reader));
                    }

                    return result;
                }
            }
        }

        public List<Category> GetCategories()
        {
            using (var connection = Open())
            using (var command = Command(connection, null, "SELECT category_id,name FROM dbo.categories ORDER BY name"))
            using (var reader = command.ExecuteReader())
            {
                var result = new List<Category>
                {
                    new Category
                    {
                        Id = 0,
                        Name = "Все категории"
                    }
                };
                while (reader.Read())
                {
                    result.Add(new Category { Id = reader.GetInt32(0), Name = reader.GetString(1) });
                }

                return result;
            }
        }

        public List<Product> GetProducts(DateTime date)
        {
            DateTime start = Pricing.PreviousMonthStart(date);
            using (var connection = Open())
            using (var command = Command(connection, null, @"SELECT p.product_id,p.category_id,p.name,c.name,sc.name,m.name,p.composition,p.description,
                p.image_path,p.price_kopecks,COALESCE((SELECT SUM(si.available_quantity) FROM dbo.stock_items si WHERE si.product_id=p.product_id),0),
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.order_items oi JOIN dbo.orders o ON o.order_id=oi.order_id
                JOIN dbo.stock_items si ON si.stock_item_id=oi.stock_item_id
                WHERE si.product_id=p.product_id AND o.order_date>=@p0 AND o.order_date<@p1) THEN 0 ELSE 1 END AS bit)
                FROM dbo.products p JOIN dbo.categories c ON c.category_id=p.category_id
                JOIN dbo.subcategories sc ON sc.subcategory_id=p.subcategory_id
                JOIN dbo.manufacturers m ON m.manufacturer_id=p.manufacturer_id ORDER BY p.product_id", start, start.AddMonths(1)))
            using (var reader = command.ExecuteReader())
            {
                var result = new List<Product>();
                while (reader.Read())
                {
                    result.Add(new Product { Id = reader.GetInt32(0), CategoryId = reader.GetInt32(1), Name = reader.GetString(2), Category = reader.GetString(3), Subcategory = reader.GetString(4), Manufacturer = reader.GetString(5), Composition = reader.GetString(6), Description = reader.GetString(7), ImagePath = reader.IsDBNull(8) ? null : reader.GetString(8), BasePriceKopecks = reader.GetInt64(9), Available = reader.GetInt32(10), HasDiscount = reader.GetBoolean(11) });
                }

                return result;
            }
        }

        public List<StockOption> GetStock(int productId)
        {
            using (var connection = Open())
            using (var command = Command(connection, null, @"SELECT si.stock_item_id,si.product_id,s.value,si.available_quantity
                FROM dbo.stock_items si JOIN dbo.sizes s ON s.size_id=si.size_id WHERE si.product_id=@p0 ORDER BY s.value", productId))
            using (var reader = command.ExecuteReader())
            {
                var result = new List<StockOption>();
                while (reader.Read())
                {
                    result.Add(new StockOption { Id = reader.GetInt32(0), ProductId = reader.GetInt32(1), Size = reader.GetDecimal(2), Available = reader.GetInt32(3) });
                }

                return result;
            }
        }

        public int CreateOrder(User actor, int customerId, IList<CartLine> lines, DateTime date)
        {
            if (lines == null || lines.Count == 0)
            {
                throw new InvalidOperationException("Добавьте хотя бы одну товарную позицию в корзину.");
            }

            var grouped = lines.GroupBy(x => x.StockId).Select(g => new { StockId = g.Key, Quantity = checked(g.Sum(x => x.Quantity)) }).OrderBy(x => x.StockId).ToList();
            if (lines.Any(x => x.Quantity < 1) || grouped.Any(x => x.Quantity > 10000))
            {
                throw new InvalidOperationException("Количество каждой позиции должно быть от 1 до 10 000.");
            }

            using (var connection = Open())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                var current = RequireUser(connection, transaction, actor, false, false);
                if (!current.IsStaff && customerId != current.Id)
                {
                    throw new InvalidOperationException("Покупатель может оформить заказ только на себя.");
                }

                using (var command = Command(connection, transaction, "SELECT COUNT(*) FROM dbo.users WHERE user_id=@p0", customerId))
                {
                    if ((int)command.ExecuteScalar() != 1)
                    {
                        throw new InvalidOperationException("Выберите существующего клиента.");
                    }
                }

                int orderId;
                using (var command = Command(connection, transaction, "INSERT dbo.orders(order_date,customer_id) VALUES(@p0,@p1); SELECT CAST(SCOPE_IDENTITY() AS int);", date.Date, customerId))
                {
                    orderId = (int)command.ExecuteScalar();
                }

                foreach (var line in grouped)
                {
                    long price;
                    bool discount;
                    DateTime start = Pricing.PreviousMonthStart(date);
                    // UPDLOCK + SERIALIZABLE и условный UPDATE защищают последнюю пару от двух покупателей.
                    using (var command = Command(connection, transaction, @"SELECT p.price_kopecks,CAST(CASE WHEN EXISTS(
                        SELECT 1 FROM dbo.orders o JOIN dbo.order_items oi ON oi.order_id=o.order_id
                        JOIN dbo.stock_items other ON other.stock_item_id=oi.stock_item_id
                        WHERE other.product_id=si.product_id AND o.order_date>=@p1 AND o.order_date<@p2) THEN 0 ELSE 1 END AS bit)
                        FROM dbo.stock_items si WITH(UPDLOCK,HOLDLOCK) JOIN dbo.products p ON p.product_id=si.product_id
                        WHERE si.stock_item_id=@p0", line.StockId, start, start.AddMonths(1)))
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            throw new InvalidOperationException("Позиция больше не существует. Обновите каталог.");
                        }

                        price = reader.GetInt64(0);
                        discount = reader.GetBoolean(1);
                    }

                    long actualPrice = Pricing.DiscountedKopecks(price, discount);
                    // До записи пользователь должен подтвердить именно текущую цену, показанную в корзине.
                    if (lines.Where(x => x.StockId == line.StockId).Any(x => x.UnitPrice != actualPrice / 100m))
                    {
                        throw new InvalidOperationException("Цена изменилась. Закройте корзину и откройте её снова для обновления цен.");
                    }

                    using (var command = Command(connection, transaction, @"UPDATE dbo.stock_items SET available_quantity=available_quantity-@p1
                        WHERE stock_item_id=@p0 AND available_quantity>=@p1", line.StockId, line.Quantity))
                    {
                        if (command.ExecuteNonQuery() != 1)
                        {
                            throw new InvalidOperationException("Недостаточно пар выбранного размера. Уменьшите количество или обновите каталог. Заказ не сохранён.");
                        }
                    }

                    using (var command = Command(connection, transaction, @"INSERT dbo.order_items(order_id,stock_item_id,quantity,unit_price_kopecks)
                        VALUES(@p0,@p1,@p2,@p3)", orderId, line.StockId, line.Quantity, actualPrice))
                    {
                        command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
                return orderId;
            }
        }

        public List<Order> GetOrders(User actor)
        {
            using (var connection = Open())
            {
                RequireUser(connection, null, actor, true, false);
                using (var command = Command(connection, null, @"SELECT o.order_id,o.order_date,CONCAT(u.last_name,N' ',u.first_name,N' ',u.middle_name),
                    COALESCE(SUM(CAST(oi.quantity AS bigint)*oi.unit_price_kopecks),0) FROM dbo.orders o
                    JOIN dbo.users u ON u.user_id=o.customer_id LEFT JOIN dbo.order_items oi ON oi.order_id=o.order_id
                    GROUP BY o.order_id,o.order_date,u.last_name,u.first_name,u.middle_name ORDER BY o.order_date DESC,o.order_id DESC"))
                using (var reader = command.ExecuteReader())
                {
                    var result = new List<Order>();
                    while (reader.Read())
                    {
                        result.Add(new Order { Id = reader.GetInt32(0), Date = reader.GetDateTime(1), Customer = reader.GetString(2), Total = reader.GetInt64(3) / 100m });
                    }

                    return result;
                }
            }
        }

        public List<OrderLine> GetOrderLines(User actor, int orderId)
        {
            using (var connection = Open())
            {
                RequireUser(connection, null, actor, true, false);
                using (var command = Command(connection, null, @"SELECT oi.order_item_id,p.name,m.name,s.value,oi.quantity,oi.unit_price_kopecks
                    FROM dbo.order_items oi JOIN dbo.stock_items si ON si.stock_item_id=oi.stock_item_id
                    JOIN dbo.products p ON p.product_id=si.product_id JOIN dbo.manufacturers m ON m.manufacturer_id=p.manufacturer_id
                    JOIN dbo.sizes s ON s.size_id=si.size_id WHERE oi.order_id=@p0 ORDER BY oi.order_item_id", orderId))
                using (var reader = command.ExecuteReader())
                {
                    var result = new List<OrderLine>();
                    while (reader.Read())
                    {
                        result.Add(new OrderLine { Id = reader.GetInt32(0), Name = reader.GetString(1), Manufacturer = reader.GetString(2), Size = reader.GetDecimal(3), Quantity = reader.GetInt32(4), UnitPrice = reader.GetInt64(5) / 100m });
                    }

                    return result;
                }
            }
        }

        public void ChangeOrderDate(User actor, int orderId, DateTime date)
        {
            using (var connection = Open())
            {
                RequireUser(connection, null, actor, false, true);
                using (var command = Command(connection, null, "UPDATE dbo.orders SET order_date=@p0 WHERE order_id=@p1", date.Date, orderId))
                {
                    if (command.ExecuteNonQuery() != 1)
                    {
                        throw new InvalidOperationException("Заказ уже удалён. Обновите список заказов.");
                    }
                }
            }
        }

        public void DeleteOrder(User actor, int orderId)
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                RequireUser(connection, transaction, actor, true, false);
                using (var command = Command(connection, transaction, "SELECT order_id FROM dbo.orders WITH(UPDLOCK,HOLDLOCK) WHERE order_id=@p0", orderId))
                {
                    if (command.ExecuteScalar() == null)
                    {
                        throw new InvalidOperationException("Заказ уже удалён. Обновите список.");
                    }
                }

                using (var command = Command(connection, transaction, @"UPDATE si SET available_quantity=si.available_quantity+oi.quantity
                    FROM dbo.stock_items si JOIN dbo.order_items oi ON oi.stock_item_id=si.stock_item_id WHERE oi.order_id=@p0;
                    DELETE FROM dbo.orders WHERE order_id=@p0;", orderId))
                {
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public void DeleteOrderLine(User actor, int orderId, int lineId)
        {
            using (var connection = Open())
            using (var transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                RequireUser(connection, transaction, actor, false, true);
                using (var command = Command(connection, transaction, "SELECT order_id FROM dbo.orders WITH(UPDLOCK,HOLDLOCK) WHERE order_id=@p0", orderId))
                {
                    if (command.ExecuteScalar() == null)
                    {
                        throw new InvalidOperationException("Заказ уже удалён. Обновите список.");
                    }
                }

                using (var command = Command(connection, transaction, "SELECT COUNT(*) FROM dbo.order_items WHERE order_id=@p0", orderId))
                {
                    if ((int)command.ExecuteScalar() <= 1)
                    {
                        throw new InvalidOperationException("Нельзя оставить заказ пустым. Используйте «Удалить заказ».");
                    }
                }

                using (var command = Command(connection, transaction, @"UPDATE si SET available_quantity=si.available_quantity+oi.quantity
                    FROM dbo.stock_items si JOIN dbo.order_items oi ON oi.stock_item_id=si.stock_item_id
                    WHERE oi.order_id=@p0 AND oi.order_item_id=@p1;
                    DELETE FROM dbo.order_items WHERE order_id=@p0 AND order_item_id=@p1;", orderId, lineId))
                {
                    if (command.ExecuteNonQuery() != 2)
                    {
                        throw new InvalidOperationException("Позиция уже удалена. Обновите состав заказа.");
                    }
                }

                transaction.Commit();
            }
        }
    }
}
