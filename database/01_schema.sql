SET XACT_ABORT ON;
BEGIN TRANSACTION;
CREATE TABLE dbo.roles (
    role_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_roles PRIMARY KEY,
    code varchar(20) NOT NULL CONSTRAINT UQ_roles_code UNIQUE,
    name nvarchar(80) NOT NULL CONSTRAINT UQ_roles_name UNIQUE,
    CONSTRAINT CK_roles_code CHECK(code IN ('admin','manager','customer'))
);
CREATE TABLE dbo.users (
    user_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_users PRIMARY KEY,
    last_name nvarchar(100) NOT NULL,
    first_name nvarchar(100) NOT NULL,
    middle_name nvarchar(100) NOT NULL,
    login nvarchar(100) NOT NULL CONSTRAINT UQ_users_login UNIQUE,
    role_id int NOT NULL,
    CONSTRAINT FK_users_roles FOREIGN KEY(role_id) REFERENCES dbo.roles(role_id)
);
CREATE TABLE dbo.categories (
    category_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_categories PRIMARY KEY,
    name nvarchar(100) NOT NULL CONSTRAINT UQ_categories_name UNIQUE
);
CREATE TABLE dbo.subcategories (
    subcategory_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_subcategories PRIMARY KEY,
    name nvarchar(100) NOT NULL CONSTRAINT UQ_subcategories_name UNIQUE
);
CREATE TABLE dbo.manufacturers (
    manufacturer_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_manufacturers PRIMARY KEY,
    name nvarchar(200) NOT NULL CONSTRAINT UQ_manufacturers_name UNIQUE
);
CREATE TABLE dbo.products (
    product_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_products PRIMARY KEY,
    category_id int NOT NULL,
    subcategory_id int NOT NULL,
    manufacturer_id int NOT NULL,
    name nvarchar(300) NOT NULL,
    image_path nvarchar(250) NULL,
    description nvarchar(max) NOT NULL,
    composition nvarchar(2000) NOT NULL,
    price_kopecks bigint NOT NULL,
    CONSTRAINT UQ_products_model UNIQUE(name,manufacturer_id),
    CONSTRAINT CK_products_price CHECK(price_kopecks BETWEEN 0 AND 100000000000),
    CONSTRAINT FK_products_categories FOREIGN KEY(category_id) REFERENCES dbo.categories(category_id),
    CONSTRAINT FK_products_subcategories FOREIGN KEY(subcategory_id) REFERENCES dbo.subcategories(subcategory_id),
    CONSTRAINT FK_products_manufacturers FOREIGN KEY(manufacturer_id) REFERENCES dbo.manufacturers(manufacturer_id)
);
CREATE TABLE dbo.sizes (
    size_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_sizes PRIMARY KEY,
    value decimal(4,1) NOT NULL CONSTRAINT UQ_sizes_value UNIQUE,
    CONSTRAINT CK_sizes_value CHECK(value>0)
);
CREATE TABLE dbo.stock_items (
    stock_item_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_stock_items PRIMARY KEY,
    product_id int NOT NULL,
    size_id int NOT NULL,
    available_quantity int NOT NULL,
    CONSTRAINT UQ_stock_items_variant UNIQUE(product_id,size_id),
    CONSTRAINT CK_stock_items_quantity CHECK(available_quantity BETWEEN 0 AND 1000000),
    CONSTRAINT FK_stock_items_products FOREIGN KEY(product_id) REFERENCES dbo.products(product_id),
    CONSTRAINT FK_stock_items_sizes FOREIGN KEY(size_id) REFERENCES dbo.sizes(size_id)
);
CREATE TABLE dbo.orders (
    order_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_orders PRIMARY KEY,
    order_date date NOT NULL,
    customer_id int NOT NULL,
    CONSTRAINT FK_orders_users FOREIGN KEY(customer_id) REFERENCES dbo.users(user_id)
);
CREATE TABLE dbo.order_items (
    order_item_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_order_items PRIMARY KEY,
    order_id int NOT NULL,
    stock_item_id int NOT NULL,
    quantity int NOT NULL,
    unit_price_kopecks bigint NOT NULL,
    CONSTRAINT UQ_order_items_variant UNIQUE(order_id,stock_item_id),
    CONSTRAINT CK_order_items_quantity CHECK(quantity BETWEEN 1 AND 10000),
    CONSTRAINT CK_order_items_price CHECK(unit_price_kopecks BETWEEN 0 AND 100000000000),
    CONSTRAINT FK_order_items_orders FOREIGN KEY(order_id) REFERENCES dbo.orders(order_id) ON DELETE CASCADE,
    CONSTRAINT FK_order_items_stock_items FOREIGN KEY(stock_item_id) REFERENCES dbo.stock_items(stock_item_id)
);
CREATE INDEX IX_orders_date ON dbo.orders(order_date,order_id);
CREATE INDEX IX_orders_customer ON dbo.orders(customer_id);
CREATE INDEX IX_order_items_stock ON dbo.order_items(stock_item_id,order_id);
COMMIT TRANSACTION;
GO
CREATE VIEW dbo.order_totals AS
SELECT o.order_id,o.order_date,o.customer_id,
    COALESCE(SUM(CAST(oi.quantity AS bigint)*oi.unit_price_kopecks),0) AS total_kopecks
FROM dbo.orders o LEFT JOIN dbo.order_items oi ON oi.order_id=o.order_id
GROUP BY o.order_id,o.order_date,o.customer_id;
GO
