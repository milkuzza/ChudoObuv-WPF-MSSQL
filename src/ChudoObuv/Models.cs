using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace ChudoObuv
{
    public sealed class User
    {
        public int Id { get; set; }
        public string Login { get; set; }
        public string FullName { get; set; }
        public string Role { get; set; }
        public string RoleName { get; set; }

        public bool IsStaff
        {
            get
            {
                return Role == "admin" || Role == "manager";
            }
        }

        public bool IsAdmin
        {
            get
            {
                return Role == "admin";
            }
        }
    }

    public sealed class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class Product
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Subcategory { get; set; }
        public string Manufacturer { get; set; }
        public string Composition { get; set; }
        public string Description { get; set; }
        public string ImagePath { get; set; }
        public long BasePriceKopecks { get; set; }
        public int Available { get; set; }
        public bool HasDiscount { get; set; }

        public decimal Price
        {
            get
            {
                return Pricing.DiscountedKopecks(BasePriceKopecks, HasDiscount) / 100m;
            }
        }

        public string PriceText
        {
            get
            {
                return Price.ToString("N2", Ui.Russian) + " ₽";
            }
        }

        public string OriginalPriceText
        {
            get
            {
                return (BasePriceKopecks / 100m).ToString("N2", Ui.Russian) + " ₽";
            }
        }

        public string StockText
        {
            get
            {
                return Available > 5 ? "много" : "мало";
            }
        }

        public bool LowStock
        {
            get
            {
                return Available <= 3;
            }
        }

        public string DiscountText
        {
            get
            {
                return HasDiscount ? "Скидка 25%" : "Обычная цена";
            }
        }

        public System.Windows.Media.Imaging.BitmapImage Image
        {
            get
            {
                return Ui.Image(ImagePath);
            }
        }
    }

    public sealed class StockOption
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public decimal Size { get; set; }
        public int Available { get; set; }

        public string Label
        {
            get
            {
                return Size.ToString("0.#", Ui.Russian) + " — в наличии " + Available + " пар";
            }
        }
    }

    public sealed class CartLine
    {
        public int StockId { get; set; }
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public decimal Size { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        public decimal Total
        {
            get
            {
                return Quantity * UnitPrice;
            }
        }
    }

    public sealed class Order
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Customer { get; set; }
        public decimal Total { get; set; }
    }

    public sealed class OrderLine
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public decimal Size { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        public decimal Total
        {
            get
            {
                return Quantity * UnitPrice;
            }
        }
    }

    public static class Pricing
    {
        public static DateTime PreviousMonthStart(DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1).AddMonths(-1);
        }

        public static long DiscountedKopecks(long price, bool hasDiscount)
        {
            return (long)Math.Round(price * (hasDiscount ? 0.75m : 1m), 0, MidpointRounding.AwayFromZero);
        }

        public static int ParseQuantity(string text)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 1 || value > 10000)
            {
                throw new InvalidOperationException("Введите целое количество от 1 до 10 000 пар.");
            }

            return value;
        }
    }
}
