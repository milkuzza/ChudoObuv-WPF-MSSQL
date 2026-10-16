# Ручная сборка «Чудо Обувь»: Excel → MSSQL → Visual Studio → WPF

Эта инструкция позволяет самостоятельно создать базу через интерфейс SSMS, импортировать подготовленный Excel, настроить ограничения и собрать WPF-приложение в Visual Studio. Автоматические скрипты из комплекта сохранены; для описанного ниже маршрута они не запускаются.

Для примеров используется новая база **ChudoObuvManual**. Имя сервера в примерах — **(LocalDB)\MSSQLLocalDB**. Если у вас другой экземпляр, например `localhost\SQLEXPRESS`, подставляйте его на всех этапах.

Структура таблиц ниже соответствует готовому коду из комплекта. Собственная нормализованная книга подходит, если её поля сопоставлены с этой структурой. Значения идентификаторов могут отличаться от моего примера, но связанные таблицы должны использовать согласованные значения.

Оглавление:

1. Что установить и подготовить.
2. Проверить нормализованный Excel.
3. Создать пустую базу в SSMS.
4. Создать таблицы вручную.
5. Импортировать листы Excel.
6. Настроить уникальность, связи и проверки.
7. Проверить результат импорта.
8. При необходимости создать представление и диаграмму.
9. Создать проект WPF в Visual Studio.
10. Перенести C#, XAML и изображения.
11. Настроить проект и подключение.
12. Собрать, отладить и проверить приложение.
13. Разобраться в коде и продолжить разработку.
14. Исправить типичные ошибки.
15. Подготовить результат к передаче.

## 1. Что установить и подготовить

Понадобятся:

- **SQL Server Database Engine**: LocalDB, Express или Developer. SSMS является клиентом управления и сам сервер не заменяет.
- **SQL Server Management Studio (SSMS)** для создания базы, таблиц и импорта.
- **SQL Server Import and Export Wizard**. Если пункт импорта не работает или мастер не установлен, потребуется установить соответствующие компоненты SQL Server либо средства Integration Services. Наличие одного LocalDB не гарантирует наличие мастера. Варианты установки и запуска описаны в [документации Microsoft](https://learn.microsoft.com/en-us/sql/integration-services/import-export-data/start-the-sql-server-import-and-export-wizard).
- **Visual Studio** с компонентом «Разработка классических приложений .NET» / **.NET desktop development** и **.NET Framework 4.8 Targeting Pack**. Для воспроизведения готового проекта подходит Visual Studio 2022.
- Возможность открыть и проверить книгу `.xlsx`.

Создайте рабочую папку, например `C:\ExamManual`. Скопируйте в неё нормализованную книгу и отдельную копию исходников из моего решения. Ниже программа создаётся в новом проекте; исходная папка с автоматическими скриптами остаётся самостоятельным рабочим вариантом.

Запишите два значения и держите их под рукой:

```text
Сервер: (LocalDB)\MSSQLLocalDB
База:   ChudoObuvManual
```

Именно эта пара должна совпадать в SSMS, мастере импорта, обозревателе Visual Studio и `App.config`.

## 2. Проверить нормализованный Excel

### 2.1. Какие листы нужны

Для готового кода используются десять отношений. Одному отношению соответствует один лист:

| Лист / будущая таблица | Что содержит |
|---|---|
| `roles` | Роли и их программные коды |
| `users` | Пользователи со ссылкой на роль |
| `categories` | Категории аудитории |
| `subcategories` | Типы обуви |
| `manufacturers` | Производители |
| `products` | Модели обуви, свойства и текущая базовая цена |
| `sizes` | Размеры |
| `stock_items` | Остатки для сочетаний модели и размера |
| `orders` | Дата и клиент заказа |
| `order_items` | Позиции заказа, количество и историческая цена |

Поясняющий лист «Схема и импорт», если он есть, в базу не импортируется. Не импортируйте одновременно лист и дублирующий его именованный диапазон Excel.

Если ваша книга использует русские названия, например «Товары» и «КодТовара», при импорте можно сопоставить их с `dbo.products` и `product_id`. Готовый код обращается к английским именам таблиц и столбцов из этой инструкции. Изменение SQL-имён потребует изменения запросов в `Store.cs`.

### 2.2. Проверка каждого листа

1. Первая строка содержит только имена столбцов. Над ней нет заголовка отчёта или пустых строк.
2. Внутри таблицы нет объединённых ячеек, строк «Итого», комментариев вместо данных и повторных заголовков.
3. Каждая строка — одна запись. В `stock_items` нет списка размеров в одной ячейке; в `order_items` нет нескольких товаров в одном поле.
4. Все первичные идентификаторы заполнены, целые и уникальны в пределах своего листа.
5. Все внешние идентификаторы существуют в родительских листах. Например, каждый `users.role_id` встречается в `roles.role_id`.
6. Внешние ключи содержат идентификатор, а не подпись. В `orders.customer_id` находится число, а ФИО хранится в `users`.
7. Даты являются настоящими датами Excel. Проверяйте значение и формат, особенно если оно выглядит как `46114`.
8. Размеры являются числами. Сохраняются значения вроде `36,5` / `36.5`; формат отображения зависит от локали Excel.
9. В колонках `price_kopecks` и `unit_price_kopecks` лежат **целые копейки**. Например, 2 190 рублей записываются как `219000`. Если у вас цена в рублях, подготовьте колонку копеек до импорта, иначе программа покажет сумму в 100 раз меньше.
10. Все обязательные тексты заполнены. В этой схеме `middle_name` обязателен: если в вашей выборке есть пользователи без отчества, заранее согласуйте хранение пустой строки либо адаптируйте nullable-поле и чтение в `Store.cs`.
11. В `image_path` записан относительный путь, например `Images/IMG_KS_195217.png`. Имя файла должно совпадать с файлом в `Assets\Images`. Для отсутствующей фотографии допускается пустая ячейка.
12. Сохраните книгу и закройте Excel перед импортом.

Служебные коды ролей должны оставаться именно `admin`, `manager`, `customer`: их проверяет логика приложения. Названия ролей для отображения могут быть русскими.

### 2.3. Если нормализовали иначе

Нормализация сама по себе не гарантирует совместимость с готовой программой. Например, схема с `Product`, `ProductSize`, `Customer` тоже может быть в 3НФ, но существующие запросы к `products`, `stock_items`, `users` с ней не совпадут.

Для копирования кода без переработки слоя данных приведите структуру назначения к таблицам ниже. Собственные названия листов можно оставить и сопоставить их через мастер импорта. Не перенумеровывайте ключи отдельно в каждом листе после того, как связи уже подготовлены.

## 3. Создать пустую базу в SSMS

1. Откройте SSMS.
2. В окне подключения выберите **Database Engine** / «Компонент Database Engine».
3. В поле Server name укажите `(LocalDB)\MSSQLLocalDB` либо ваш реальный экземпляр.
4. Выберите **Windows Authentication** / «Проверка подлинности Windows».
5. Подключитесь.
6. В Object Explorer щёлкните правой кнопкой **Databases → New Database** / «Базы данных → Создать базу данных».
7. Введите `ChudoObuvManual`.
8. Для локального учебного варианта оставьте расположение файлов по умолчанию. На странице Options можно выбрать сопоставление `Cyrillic_General_100_CI_AS`, как в подготовленном решении.
9. Нажмите OK и обновите узел Databases.

Теперь у вас есть пустая база. Создание базы пока не создаёт таблицы и не переносит Excel.

Если база с таким именем уже существует и содержит данные, выберите другое новое имя и дальше используйте его во всех подключениях.

## 4. Создать таблицы вручную

### 4.1. Один раз разобрать создание таблицы

На примере `roles`:

1. Раскройте **ChudoObuvManual → Tables**.
2. Нажмите правой кнопкой **Tables → New → Table**.
3. Введите столбцы из таблицы ниже: имя, тип и разрешение NULL.
4. Для `role_id` выберите `int`, снимите Allow Nulls.
5. Щёлкните правой кнопкой по строке `role_id` и выберите **Set Primary Key** / «Задать первичный ключ». Появится значок ключа.
6. Выделите `role_id`. Внизу, в Column Properties, раскройте **Identity Specification**.
7. Установите **(Is Identity) = Yes**, **Identity Seed = 1**, **Identity Increment = 1**.
8. Нажмите Ctrl+S и сохраните таблицу с именем `roles`. В Object Explorer она должна появиться как `dbo.roles`.
9. Закройте конструктор.

Повторите этот порядок для всех десяти таблиц. **IDENTITY нужен только первому столбцу — первичному идентификатору каждой таблицы.** Например, `users.user_id` — IDENTITY, а `users.role_id` — обычный `int`.

На этом этапе создайте столбцы, первичные ключи и IDENTITY. Внешние ключи, уникальные ограничения и CHECK добавляются после импорта, чтобы сразу проверить уже загруженные данные.

### 4.2. Точный состав таблиц

В таблицах ниже «Нет» означает, что галочка **Allow Nulls** снята. Для каждого столбца, отмеченного `PK, IDENTITY`, задайте первичный ключ и автоувеличение, как выше.

**dbo.roles**

| Столбец | Тип SQL Server | NULL | Назначение |
|---|---|---|---|
| `role_id` | `int` | Нет | PK, IDENTITY |
| `code` | `varchar(20)` | Нет | `admin`, `manager`, `customer` |
| `name` | `nvarchar(80)` | Нет | Название роли |

**dbo.users**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `user_id` | `int` | Нет | PK, IDENTITY |
| `last_name` | `nvarchar(100)` | Нет | Фамилия |
| `first_name` | `nvarchar(100)` | Нет | Имя |
| `middle_name` | `nvarchar(100)` | Нет | Отчество |
| `login` | `nvarchar(100)` | Нет | Логин |
| `role_id` | `int` | Нет | Ссылка на роль |

**dbo.categories**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `category_id` | `int` | Нет | PK, IDENTITY |
| `name` | `nvarchar(100)` | Нет | Категория |

**dbo.subcategories**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `subcategory_id` | `int` | Нет | PK, IDENTITY |
| `name` | `nvarchar(100)` | Нет | Тип обуви |

**dbo.manufacturers**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `manufacturer_id` | `int` | Нет | PK, IDENTITY |
| `name` | `nvarchar(200)` | Нет | Производство |

**dbo.products**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `product_id` | `int` | Нет | PK, IDENTITY |
| `category_id` | `int` | Нет | Ссылка на категорию |
| `subcategory_id` | `int` | Нет | Ссылка на тип обуви |
| `manufacturer_id` | `int` | Нет | Ссылка на производство |
| `name` | `nvarchar(300)` | Нет | Наименование модели |
| `image_path` | `nvarchar(250)` | **Да** | Путь к фотографии |
| `description` | `nvarchar(max)` | Нет | Описание |
| `composition` | `nvarchar(2000)` | Нет | Состав |
| `price_kopecks` | `bigint` | Нет | Базовая цена в копейках |

**dbo.sizes**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `size_id` | `int` | Нет | PK, IDENTITY |
| `value` | `decimal(4,1)` | Нет | Размер, в том числе дробный |

**dbo.stock_items**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `stock_item_id` | `int` | Нет | PK, IDENTITY |
| `product_id` | `int` | Нет | Ссылка на модель |
| `size_id` | `int` | Нет | Ссылка на размер |
| `available_quantity` | `int` | Нет | Доступное количество |

**dbo.orders**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `order_id` | `int` | Нет | PK, IDENTITY |
| `order_date` | `date` | Нет | Дата заказа |
| `customer_id` | `int` | Нет | Ссылка на пользователя |

**dbo.order_items**

| Столбец | Тип | NULL | Назначение |
|---|---|---|---|
| `order_item_id` | `int` | Нет | PK, IDENTITY |
| `order_id` | `int` | Нет | Ссылка на заказ |
| `stock_item_id` | `int` | Нет | Ссылка на сочетание модели и размера |
| `quantity` | `int` | Нет | Число пар в строке заказа |
| `unit_price_kopecks` | `bigint` | Нет | Историческая цена одной пары в копейках |

Здесь важно сохранить именно типы. Готовый `Store.cs` читает идентификаторы методом `GetInt32`, цены — `GetInt64`, размеры — `GetDecimal`, дату — `GetDateTime`. Автоматически полученные `float`, `nvarchar(255)` или текстовые даты вместо этих типов могут вызвать ошибку чтения.

Если конструктор предлагает сохранить таблицу не в `dbo`, откройте свойства таблицы и задайте Schema = `dbo`.

## 5. Импортировать листы Excel

### 5.1. Порядок

Импортируйте по одному листу за запуск мастера:

```text
roles
users
categories
subcategories
manufacturers
products
sizes
stock_items
orders
order_items
```

Это удобный порядок «родитель до дочерней таблицы». Все десять таблиц назначения уже созданы вручную.

### 5.2. Настроить источник и назначение

1. В SSMS нажмите правой кнопкой по **ChudoObuvManual → Tasks → Import Data** / «Задачи → Импорт данных».
2. На странице Choose a Data Source выберите **Microsoft Excel**.
3. Укажите вашу нормализованную книгу `.xlsx`.
4. Выберите совместимый формат Excel и включите **First row has column names** / «Первая строка содержит имена столбцов».
5. На странице назначения выберите установленный провайдер подключения к SQL Server. Например, **.NET Framework Data Provider for SQL Server**.
6. Для этого провайдера задайте **Data Source** = ваш сервер, **Initial Catalog** = `ChudoObuvManual`, **Integrated Security** = `True`. Если используете Microsoft OLE DB Driver for SQL Server, те же сведения указываются в его окне подключения.
7. Выберите **Copy data from one or more tables or views** / «Скопировать данные из одной или нескольких таблиц или представлений».

Эти страницы мастера и соответствие полей подключения приведены в [примере импорта Microsoft](https://learn.microsoft.com/en-us/sql/integration-services/import-export-data/get-started-with-this-simple-example-of-the-import-and-export-wizard).

### 5.3. Сопоставить конкретный лист

Для первого запуска:

1. Отметьте **только** лист `roles$`.
2. В Destination выберите **существующую** таблицу `[dbo].[roles]`. Символ `$` относится к листу Excel и в имя таблицы MSSQL не включается.
3. Нажмите **Edit Mappings** / «Изменить сопоставления».
4. Выберите **Append rows to destination table** — добавление в уже созданную пустую таблицу.
5. Проверьте пары `role_id → role_id`, `code → code`, `name → name`.
6. Включите **Enable identity insert** / «Разрешить вставку значений идентификатора».
7. Убедитесь, что `role_id` не помечен как Ignore.
8. Проверьте Preview, затем Run immediately и Finish.
9. Убедитесь, что операция завершилась успешно и число строк совпадает с листом.

Опция **Enable identity insert** сохраняет уже подготовленные значения ключей. Если позволить SQL Server перенумеровать их, ссылки из других листов могут указывать на другие записи. Параметры сопоставления описаны в [документации Microsoft](https://learn.microsoft.com/en-us/sql/integration-services/import-export-data/column-mappings-sql-server-import-and-export-wizard).

После этого повторите мастер для каждого следующего листа, каждый раз выбирая нужную существующую таблицу и сохраняя исходные ID. Для всех десяти таблиц в этой схеме есть первичный IDENTITY-столбец.

Если мастер показывает создание новой таблицы `roles$`, вернитесь и выберите существующую `dbo.roles`. Если предлагает типы вроде `float` для ID, проверьте, что в качестве назначения действительно выбрана ваша таблица с заранее заданными типами.

### 5.4. Проверять импорт сразу

После каждой таблицы:

1. В SSMS обновите Tables.
2. Нажмите по таблице правой кнопкой → **Select Top 1000 Rows**.
3. Сравните несколько идентификаторов и значений с Excel, включая последнюю запись по ID.
4. Для `sizes` проверьте дробный размер; для `products` — цену в копейках и полный длинный текст; для `orders` — реальные календарные даты.

Для точного количества можно самостоятельно выполнить короткий проверочный запрос:

```sql
SELECT COUNT(*) AS row_count FROM dbo.roles;
```

Замените имя таблицы на нужное. Это проверка уже выполненной ручной работы; готовые скрипты создания и заполнения базы для этого не нужны.

Не запускайте импорт повторно в уже заполненную таблицу. Если предыдущая попытка закончилась ошибкой, сначала посмотрите, были ли сохранены строки: неудачный импорт не всегда означает полностью пустое назначение.

### 5.5. Если Excel отсутствует в списке или ACE не зарегистрирован

Разрядность мастера должна соответствовать установленному Excel/Access-провайдеру. Разрядность самого сервера здесь не определяет выбор. Компоненты подключения к Office-файлам можно получить в составе [Microsoft 365 Access Runtime](https://support.microsoft.com/en-us/access/download-and-install-microsoft-365-access-runtime); учитывайте совместимость с уже установленным Office.

Если есть два `DTSWizard.exe`, вариант в `Program Files` обычно 64-битный, в `Program Files (x86)` — 32-битный. Для SQL Server 2022 типовой путь 64-битного мастера — `C:\Program Files\Microsoft SQL Server\160\DTS\Binn\DTSWizard.exe`, если соответствующий компонент установлен. Примеры путей приведены в [документации запуска мастера](https://learn.microsoft.com/en-us/sql/integration-services/import-export-data/start-the-sql-server-import-and-export-wizard).

Смешанные числа и текст, пустые первые строки и длинные описания требуют отдельной проверки: тип, определённый Excel-провайдером, может отличаться от визуального формата ячеек. Если текст обрезался ещё при чтении источника, увеличение столбца назначения само по себе его не восстановит. Проверяйте источник, сопоставления и фактически загруженные строки; сведения об ограничениях собраны в [документации импорта Excel](https://learn.microsoft.com/en-us/sql/integration-services/load-data-to-from-excel-with-ssis).

Запасной ручной путь: сохранить каждый лист как CSV UTF-8 и в том же мастере выбрать **Flat File Source**, указать реальный разделитель файла, UTF-8, первую строку с именами и соответствующие столбцы назначения. Продолжайте импортировать в подготовленные таблицы с сохранением ID. Для дат используйте однозначный вид `2026-04-02`; размеры и денежные значения проверьте после преобразования. Этот вариант также не требует готового скрипта наполнения базы.

## 6. Настроить ограничения

### 6.1. Уникальные значения и сочетания

В SSMS откройте таблицу через Design, затем правой кнопкой в конструкторе → **Indexes/Keys**. Нажмите Add, задайте **Type = Unique Key**, укажите Columns и осмысленное имя. Для составного ограничения выберите оба столбца в одном ключе. Сохраните таблицу. Порядок описан в [документации Microsoft](https://learn.microsoft.com/en-us/sql/relational-databases/tables/create-unique-constraints).

Создайте:

| Таблица | Столбцы одного уникального ограничения | Пример имени |
|---|---|---|
| `roles` | `code` | `UQ_roles_code` |
| `roles` | `name` | `UQ_roles_name` |
| `users` | `login` | `UQ_users_login` |
| `categories` | `name` | `UQ_categories_name` |
| `subcategories` | `name` | `UQ_subcategories_name` |
| `manufacturers` | `name` | `UQ_manufacturers_name` |
| `products` | **`name`, `manufacturer_id` вместе** | `UQ_products_model` |
| `sizes` | `value` | `UQ_sizes_value` |
| `stock_items` | **`product_id`, `size_id` вместе** | `UQ_stock_items_variant` |
| `order_items` | **`order_id`, `stock_item_id` вместе** | `UQ_order_items_variant` |

Не создавайте отдельную уникальность для `stock_items.product_id`: одна модель должна иметь несколько размеров. Ограничение относится к паре значений. Аналогично один заказ может содержать несколько позиций.

Если SQL Server не разрешает создать уникальность, найдите повторяющиеся значения в данных. При выбранном сопоставлении без учёта регистра логины `User` и `user` считаются одинаковыми.

### 6.2. Внешние ключи

Разберите одну связь: `users.role_id → roles.role_id`.

1. Откройте `dbo.users` через Design.
2. Правой кнопкой в конструкторе → **Relationships → Add**.
3. Выберите **Tables and Columns Specification** и кнопку `…`.
4. Родительская таблица / Primary key table — `dbo.roles`, её столбец — `role_id`.
5. Дочерняя таблица / Foreign key table — `dbo.users`, её столбец — `role_id`.
6. Задайте имя `FK_users_roles`.
7. Проверьте **Enforce Foreign Key Constraint = Yes** и **Check Existing Data On Creation Or Re-Enabling = Yes**.
8. Для Delete Rule и Update Rule оставьте **No Action**.
9. Закройте окно и сохраните таблицу.

Расположение этих настроек показано в [документации Microsoft по связям](https://learn.microsoft.com/en-us/sql/relational-databases/tables/create-foreign-key-relationships).

По тому же принципу создайте все связи:

| Дочерняя таблица и FK | Родительская таблица и PK | Удаление |
|---|---|---|
| `users.role_id` | `roles.role_id` | No Action |
| `products.category_id` | `categories.category_id` | No Action |
| `products.subcategory_id` | `subcategories.subcategory_id` | No Action |
| `products.manufacturer_id` | `manufacturers.manufacturer_id` | No Action |
| `stock_items.product_id` | `products.product_id` | No Action |
| `stock_items.size_id` | `sizes.size_id` | No Action |
| `orders.customer_id` | `users.user_id` | No Action |
| `order_items.order_id` | `orders.order_id` | **Cascade** |
| `order_items.stock_item_id` | `stock_items.stock_item_id` | No Action |

Для всех связей обновление — **No Action**. Только у `order_items.order_id → orders.order_id` выберите удаление **Cascade**: при удалении заголовка его строки должны удаляться автоматически. Иначе готовый метод `DeleteOrder` не сможет удалить непустой заказ.

Возврат количества на склад выполняет C#-код в одной транзакции с удалением. Не добавляйте отдельный триггер возврата остатков поверх готового кода: тогда количество вернётся дважды.

Если связь не создаётся из-за существующих строк, это полезная диагностика: в дочернем поле есть ID, которого нет в родительской таблице. Исправьте данные или сопоставление импорта и повторите создание связи с проверкой данных.

### 6.3. CHECK — допустимые значения

В конструкторе таблицы откройте **Check Constraints**, добавьте ограничение и в поле Expression введите выражение из таблицы. Установите проверку существующих данных и применение при INSERT/UPDATE, сохраните. В некоторых версиях можно открыть этот диалог через узел **Constraints → New Constraint**. Параметры описаны в [документации Microsoft](https://learn.microsoft.com/en-us/sql/relational-databases/tables/create-check-constraints).

| Таблица | Выражение | Имя |
|---|---|---|
| `roles` | `code IN ('admin','manager','customer')` | `CK_roles_code` |
| `products` | `price_kopecks BETWEEN 0 AND 100000000000` | `CK_products_price` |
| `sizes` | `value > 0` | `CK_sizes_value` |
| `stock_items` | `available_quantity BETWEEN 0 AND 1000000` | `CK_stock_items_quantity` |
| `order_items` | `quantity BETWEEN 1 AND 10000` | `CK_order_items_quantity` |
| `order_items` | `unit_price_kopecks BETWEEN 0 AND 100000000000` | `CK_order_items_price` |

Для исходных данных границы достаточны и совпадают с готовой схемой приложения.

### 6.4. Индексы для запросов

Через **Indexes/Keys** создайте обычные неуникальные индексы Type = Index, Is Unique = No:

| Таблица | Столбцы в указанном порядке | Имя |
|---|---|---|
| `orders` | `order_date`, `order_id` | `IX_orders_date` |
| `orders` | `customer_id` | `IX_orders_customer` |
| `order_items` | `stock_item_id`, `order_id` | `IX_order_items_stock` |

Уникальные ограничения уже создают свои индексы; второй одинаковый индекс для них не нужен.

## 7. Проверить импорт и структуру

Откройте New Query именно для `ChudoObuvManual`. Сначала убедитесь, что подключились туда, куда планировали:

```sql
SELECT @@SERVERNAME AS server_name, DB_NAME() AS database_name;
```

Имя сервера, возвращаемое LocalDB, может выглядеть иначе, чем строка подключения. Основной контроль — выбранный экземпляр в Object Explorer и `database_name = ChudoObuvManual`.

Затем проверьте данные:

```sql
SELECT COUNT(*) AS products FROM dbo.products;
SELECT COUNT(*) AS variants FROM dbo.stock_items;
SELECT SUM(available_quantity) AS available_pairs FROM dbo.stock_items;
SELECT COUNT(*) AS orders FROM dbo.orders;
SELECT COUNT(*) AS order_lines FROM dbo.order_items;
SELECT SUM(quantity) AS ordered_pairs FROM dbo.order_items;
SELECT SUM(CAST(quantity AS bigint) * unit_price_kopecks) / 100.0 AS total_rubles
FROM dbo.order_items;
```

Для неизменённого набора из задания ожидаются соответственно **31, 92, 1 274, 10, 30, 36, 341 770,00**. Если вы меняли состав данных, сверяйте со своей книгой, а не с этими числами.

Проверьте разворачивание связей:

```sql
SELECT TOP (20)
    p.name,
    m.name AS manufacturer,
    s.value AS size,
    si.available_quantity
FROM dbo.stock_items AS si
JOIN dbo.products AS p ON p.product_id = si.product_id
JOIN dbo.manufacturers AS m ON m.manufacturer_id = p.manufacturer_id
JOIN dbo.sizes AS s ON s.size_id = si.size_id
ORDER BY p.product_id, s.value;
```

Здесь должны получиться осмысленные сочетания моделей, производства и размеров. Если строки пропали, проверьте ID и наличие связанных записей.

Для контроля ограничений:

```sql
DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS;
```

При отсутствии нарушений список проблем пуст. Эта команда проверяет существующие ограничения; она не заменяет создание недостающих FK, UNIQUE и CHECK.

Особенно проверьте генерацию новых номеров заказов. В свойствах `orders.order_id` и `order_items.order_item_id` должно быть **Is Identity = Yes**. Диагностика без изменения счётчика:

```sql
DBCC CHECKIDENT ('dbo.orders', NORESEED);
DBCC CHECKIDENT ('dbo.order_items', NORESEED);
```

Если текущий счётчик меньше максимального импортированного ID, его можно вручную согласовать:

```sql
DBCC CHECKIDENT ('dbo.orders', RESEED);
DBCC CHECKIDENT ('dbo.order_items', RESEED);
```

Не задавайте произвольное значение вроде 0 в заполненной таблице. Поведение команды и её ограничения описаны в [документации Microsoft](https://learn.microsoft.com/en-us/sql/t-sql/database-console-commands/dbcc-checkident-transact-sql).

## 8. Представление и диаграмма

### 8.1. Представление итогов — по желанию

Готовый WPF-код самостоятельно вычисляет итоги в SQL-запросах; представление `order_totals` не является условием его запуска. Оно удобно для просмотра и сверки.

Чтобы создать его вручную через SSMS:

1. Нажмите **Views → New View**.
2. Закройте окно добавления таблиц.
3. Откройте SQL Pane, если он скрыт.
4. Введите запрос:

```sql
SELECT
    o.order_id,
    o.order_date,
    o.customer_id,
    COALESCE(SUM(CAST(oi.quantity AS bigint) * oi.unit_price_kopecks), 0) AS total_kopecks
FROM dbo.orders AS o
LEFT JOIN dbo.order_items AS oi ON oi.order_id = o.order_id
GROUP BY o.order_id, o.order_date, o.customer_id;
```

5. Сохраните представление под именем `order_totals`.

Итоговую сумму в таблицу `orders` добавлять не нужно: это вычисляемое значение. Цена строки заказа при этом остаётся самостоятельным историческим фактом.

### 8.2. ER-диаграмма

После настройки FK откройте **Database Diagrams → New Database Diagram**. Если для вашей учебной базы SSMS предложит установить поддержку диаграмм, подтвердите её установку. Добавьте десять таблиц, расположите их так, чтобы связи читались, включите отображение всех необходимых столбцов и сохраните диаграмму.

Для сдачи задания выведите её на печать в **Microsoft Print to PDF**, выберите достаточный размер и ориентацию страницы. Откройте PDF и проверьте, что видны имена, ключи и все связи, а таблицы не обрезаны. Подготовленная `docs/ER_diagram.pdf` может служить образцом расположения.

## 9. Создать новый WPF-проект

1. Откройте Visual Studio.
2. Выберите **Create a new project**.
3. Найдите **WPF App (.NET Framework)** / «Приложение WPF (.NET Framework)», язык C#.
4. Назовите проект **ChudoObuv**. Для работы с готовыми ресурсами сохраните также Assembly name и Default namespace равными `ChudoObuv`.
5. Выберите .NET Framework **4.8** и рабочую папку внутри `C:\ExamManual`.
6. Создайте проект.

Готовый код использует `System.Data.SqlClient` из .NET Framework и `System.Configuration`. Для его прямого переноса выбирается именно шаблон .NET Framework 4.8. Переход на современный .NET возможен, но потребует других настроек и пакетов.

### Особенность именно подготовленного приложения

В обычном шаблоне окно описано в `MainWindow.xaml`, а точка запуска создаётся из `App.xaml`. В моём варианте:

- запуск находится в **App.cs**, метод `Main`;
- класс **MainWindow.cs** создаёт окно и связывает события;
- содержимое форм находится в **Views\*.xaml**;
- XAML загружается через `XamlReader` из **Embedded Resource**;
- `x:Class` в этих XAML-файлах не используется.

Поэтому нельзя оставить стандартный `App.xaml` с `StartupUri="MainWindow.xaml"` и добавить рядом все мои файлы: получится конфликт двух способов запуска.

## 10. Перенести код и ресурсы

### 10.1. Заменить шаблонные файлы в новом проекте

Удалите **только из нового созданного проекта**:

- шаблонный `App.xaml` вместе с `App.xaml.cs`;
- шаблонный `MainWindow.xaml` вместе с `MainWindow.xaml.cs`.

Затем через **Project → Add Existing Item** добавьте из `src\ChudoObuv` подготовленного решения:

```text
App.cs
Models.cs
Store.cs
Ui.cs
MainWindow.cs
App.config
```

Можно создать классы с этими именами и по очереди вставить их содержимое. Сохраняйте namespace `ChudoObuv`.

### 10.2. Добавить XAML

Создайте в новом проекте папку **Views**. Скопируйте туда:

```text
Theme.xaml
MainView.xaml
LoginView.xaml
ProductView.xaml
CartView.xaml
OrdersView.xaml
```

Сохраните их имена. `Ui.Load("MainView.xaml")` ищет конкретный встроенный ресурс `ChudoObuv.Views.MainView.xaml`.

### 10.3. Добавить изображения

Скопируйте в каталог нового проекта всю папку **Assets** из решения, сохраняя вложенность:

```text
Assets/
    app.ico
    logo.png
    picture.png
    Images/
        IMG_KS_195217.png
        ... остальные изображения ...
```

Файл из БД `Images/IMG_KS_195217.png` программа ищет как `Assets\Images\IMG_KS_195217.png` рядом с запускаемым EXE. Абсолютные пути с вашего рабочего стола в базу записывать не нужно.

## 11. Настроить проект и подключение

### 11.1. Проверить ссылки

В References должны присутствовать:

```text
System
System.Core
System.Configuration
System.Data
System.Xaml
System.Xml
WindowsBase
PresentationCore
PresentationFramework
```

Недостающие добавляются через **References → Add Reference → Assemblies → Framework**. Для этого варианта приложения Entity Framework, DataSet Designer и NuGet-пакеты не требуются: запросы выполняет класс `Store` через ADO.NET.

### 11.2. Настроить встраивание XAML и точку входа

Для каждого файла `Views\*.xaml` в Properties установите:

```text
Build Action: Embedded Resource
Copy to Output Directory: Do not copy
```

Для `.cs` используется **Compile**.

В свойствах проекта выберите Startup object = **ChudoObuv.App**. В готовом `App.cs` уже есть атрибут TargetFramework, поэтому в проекте нужно отключить повторную автоматическую генерацию такого атрибута.

Чтобы исключить неоднозначность настройки, ниже приведён целиком минимальный проект для указанной структуры. После копирования файлов можно выгрузить **новый** проект через **Unload Project**, выбрать **Edit ChudoObuv.csproj**, заменить его содержимое этим текстом, сохранить и выполнить Reload Project. Это файл сборки WPF, а не скрипт создания базы.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" DefaultTargets="Build"
         xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <Configuration Condition="'$(Configuration)' == ''">Debug</Configuration>
    <Platform Condition="'$(Platform)' == ''">AnyCPU</Platform>
    <ProjectGuid>{3D6B2972-FB0D-4E6D-A75E-3654F52D1B02}</ProjectGuid>
    <OutputType>WinExe</OutputType>
    <RootNamespace>ChudoObuv</RootNamespace>
    <AssemblyName>ChudoObuv</AssemblyName>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
    <StartupObject>ChudoObuv.App</StartupObject>
    <OutputPath>bin\$(Configuration)\</OutputPath>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <DebugSymbols Condition="'$(Configuration)' == 'Debug'">true</DebugSymbols>
    <DebugType Condition="'$(Configuration)' == 'Debug'">full</DebugType>
    <Optimize Condition="'$(Configuration)' == 'Release'">true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System" />
    <Reference Include="System.Core" />
    <Reference Include="System.Configuration" />
    <Reference Include="System.Data" />
    <Reference Include="System.Xaml" />
    <Reference Include="System.Xml" />
    <Reference Include="WindowsBase" />
    <Reference Include="PresentationCore" />
    <Reference Include="PresentationFramework" />
    <Compile Include="App.cs" />
    <Compile Include="Models.cs" />
    <Compile Include="Store.cs" />
    <Compile Include="Ui.cs" />
    <Compile Include="MainWindow.cs" />
    <None Include="App.config" />
    <EmbeddedResource Include="Views\*.xaml">
      <LogicalName>ChudoObuv.Views.%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
    <Content Include="Assets\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

Этот минимальный файл перечисляет пять классов приложения. Если позже добавите новые `.cs`, включайте их через Add Existing Item / Add Class, чтобы Visual Studio добавила Compile-запись. После полной замены файла шаблонный `Properties\AssemblyInfo.cs` не включён в сборку; для запуска приложения он не нужен.

Сохраните `ProjectGuid`, который уже был у вашего нового проекта: при замене XML подставьте его вместо GUID в образце. Тогда запись проекта в созданном `.sln` сохранит соответствие.

Итоговая структура внутри каталога проекта:

```text
ChudoObuv.csproj
App.config
App.cs
Models.cs
Store.cs
Ui.cs
MainWindow.cs
Views/
    Theme.xaml
    MainView.xaml
    LoginView.xaml
    ProductView.xaml
    CartView.xaml
    OrdersView.xaml
Assets/
    app.ico
    logo.png
    picture.png
    Images/...
```

### 11.3. Подключить базу в обозревателе Visual Studio

1. Откройте **View → Server Explorer** / «Вид → Обозреватель серверов».
2. Нажмите правой кнопкой **Data Connections → Add Connection**.
3. Выберите источник **Microsoft SQL Server**.
4. Укажите тот же сервер, что в SSMS, и Windows Authentication.
5. Выберите существующую базу `ChudoObuvManual`.
6. Нажмите **Test Connection**, затем OK.
7. Раскройте Tables и убедитесь, что видны ваши десять таблиц.

Альтернативный интерфейс — **View → SQL Server Object Explorer → Add SQL Server**. Оба способа описаны в [документации Microsoft](https://learn.microsoft.com/en-us/visualstudio/data-tools/add-new-connections).

Подключение в обозревателе позволяет просматривать базу из IDE. **Программа получает собственное подключение через `App.config`; успешный Test Connection сам по себе его не настраивает.** База уже создана на экземпляре сервера, поэтому создавать ещё один `.mdf` через Add New Item для этого маршрута не требуется.

### 11.4. Указать подключение приложения

Откройте **App.config именно нового проекта** и задайте:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
  <connectionStrings>
    <add name="ChudoObuv"
         providerName="System.Data.SqlClient"
         connectionString="Data Source=(LocalDB)\MSSQLLocalDB;Initial Catalog=ChudoObuvManual;Integrated Security=True;Connect Timeout=15;Application Name=ChudoObuvManual" />
  </connectionStrings>
</configuration>
```

Для обычного Express замените только сервер, например:

```xml
connectionString="Data Source=localhost\SQLEXPRESS;Initial Catalog=ChudoObuvManual;Integrated Security=True;Connect Timeout=15;Application Name=ChudoObuvManual"
```

Имя записи **name="ChudoObuv"** оставьте: по этому имени `App.cs` получает настройку. Имя базы задаётся отдельно в **Initial Catalog** и может быть другим.

В XML обратный слеш записывается один раз. Если будете писать строку подключения непосредственно в C#, используйте `@"Data Source=(LocalDB)\MSSQLLocalDB;..."` либо экранируйте обратный слеш в обычной строке.

В готовом `App.cs` переменная окружения **CHUDOOBUV_CONNECTION_STRING** имеет приоритет над конфигурацией. Если задавали её раньше, проверьте, что она не направляет программу в старую базу. После изменения переменных окружения перезапустите Visual Studio, чтобы новый процесс получил нужные значения.

В новых диалогах подключения Visual Studio могут встречаться Encrypt = Mandatory / Optional. Для используемого здесь `System.Data.SqlClient` при явном указании параметра в конфигурации используются значения `Encrypt=True` или `Encrypt=False`. Не переносите весь текст настроек другого провайдера без проверки. Для локального учебного экземпляра без настроенного сертификата в диалоге VS можно выбрать Encrypt = Optional; параметры шифрования и сертификатов описаны у [Microsoft](https://learn.microsoft.com/en-us/visualstudio/data-tools/add-new-connections).

## 12. Собрать и проверить приложение

### 12.1. Первый запуск

1. В Solution Explorer нажмите правой кнопкой на `ChudoObuv` → **Set as Startup Project**.
2. Выберите Debug / Any CPU.
3. Выполните **Build → Rebuild Solution**.
4. Если есть ошибки компиляции, сначала исправьте их, затем повторите сборку.
5. Проверьте папку `bin\Debug`. В ней должны быть:

```text
ChudoObuv.exe
ChudoObuv.exe.config
Assets\logo.png
Assets\picture.png
Assets\app.ico
Assets\Images\...
```

6. Откройте `bin\Debug\ChudoObuv.exe.config` и убедитесь, что там указан `ChudoObuvManual`. Это результат копирования `App.config` при сборке.
7. Нажмите **F5** или **Ctrl+F5**.

Для этого ручного маршрута запускается проект из Visual Studio или собранный EXE. **Launch.cmd и Initialize-Database.ps1 остаются для автоматического маршрута.** Вручную созданная база не содержит служебной отметки автоматического импортёра; его запуск не нужен для работы WPF-приложения и может остановиться с сообщением о существующих таблицах.

Если общее сообщение об ошибке готового приложения предлагает выполнить `Initialize-Database.ps1`, для ручного маршрута сначала проверьте свою базу по разделу 7 и строку подключения по разделу 11. Запуск импортёра не является обязательным способом исправления подключения.

### 12.2. Если хотите сначала проследить подключение

Поставьте breakpoint в `Store.Open()` на строке `connection.Open()` и запустите F5. В отладчике проверьте `connectionString`, затем выполните шаг F10. Если подключение открылось, поставьте следующую точку в `GetProducts`, чтобы увидеть выполнение запроса и чтение результата.

Если приложение открывает соединение, но падает на `reader.GetInt32` / `GetInt64` / `GetDecimal`, проверьте тип соответствующего столбца назначения. Ошибка часто означает, что мастер создал столбец другого типа или загружено неожиданное NULL.

### 12.3. Проверка функций по порядку

1. **Гость.** Каталог должен показывать модели, изображения, состав, цену и наличие.
2. **Вход.** Найдите логин администратора и покупателя в своей `users`; если данные не меняли, подойдут `isivanov`, `papetrov`, `asidorova`. Пароль не вводится. ФИО отображается справа вверху.
3. **Каталог.** Проверьте поиск по названию и описанию, категорию и сортировку цены. Они должны работать совместно.
4. **Модель.** Откройте товар, проверьте описание, полный размерный ряд и доступные размеры.
5. **Корзина.** Добавьте несколько позиций, измените количество одной, удалите другую, вернитесь к выбору.
6. **Оформление.** Подтвердите заказ, запишите его номер.
7. **База.** В SSMS обновите просмотр `orders`, `order_items`, `stock_items`. Должны появиться заголовок и строки, а остатки — уменьшиться ровно на заказанное количество.
8. **Менеджер.** Проверьте просмотр состава и оформление на выбранного клиента.
9. **Администратор.** Измените дату и удалите отдельную позицию заказа. Итог уменьшится, количество вернётся в остатки.
10. **Удаление заказа через приложение.** Удалите тестовый заказ и убедитесь, что остатки восстановлены.

Заказы удаляйте через программу, когда проверяете возврат количества: простое удаление строки в SSMS не вызывает C#-метод возврата остатков.

### 12.4. Проверка скидки

Скидка 25% определяется отсутствием заказов модели в предыдущем **календарном** месяце, по всем её размерам. При текущей дате октября проверяется сентябрь, а не последние 30 дней. В исходном наборе заказы за апрель и май 2026 года; при запуске в другой месяц скидка может быть сразу у всех моделей.

Чтобы проверить отсутствие скидки, создайте тестовый заказ на модель и администратором измените дату этого заказа на предыдущий календарный месяц. После обновления каталога у этой модели должна появиться обычная цена. Цена уже сохранённой строки заказа при изменении даты остаётся исторической.

## 13. Разобраться в готовом коде

Для дальнейшей самостоятельной разработки удобно читать и переносить код в таком порядке:

| Файл | Назначение | Что проверить после переноса |
|---|---|---|
| `Models.cs` | Типы пользователей, товаров, размеров, корзины и заказов; `Pricing` | Типы полей и единицы денег |
| `Store.cs` | Подключение, SQL-запросы, проверка ролей, транзакции | Имена таблиц/полей, чтение SQL-типов |
| `Ui.cs` | Загрузка XAML, изображений, общие диалоги | Имена встроенных ресурсов и Assets |
| `Views/Theme.xaml` | Общие цвета, шрифт и стили | Ключи `Ink`, `Mint`, `Accent`, `Primary` |
| `Views/MainView.xaml` | Содержимое каталога | Имена элементов, на которые ссылается C# |
| Остальные `Views/*.xaml` | Вход, модель, корзина и заказы | Имена полей, списков и кнопок |
| `MainWindow.cs` | Создание окна, события, переключение ролей и модальные формы | Привязка событий к именованным элементам |
| `App.cs` | Точка входа, ресурсы и строка подключения | `StartupObject` и `App.config` |

Пример связи интерфейса и логики:

```text
MainView.xaml: кнопка CartButton
    → MainWindow.cs: обработчик Click
    → ShowCart(): чтение корзины и показ CartView.xaml
    → ConfirmOrder: подтверждение пользователя
    → Store.CreateOrder(): проверка прав, цен и остатков, транзакция
    → таблицы orders, order_items, stock_items
    → Reload(): обновление каталога
```

Если меняете `x:Name="CartButton"` в XAML, поменяйте и строку поиска этого элемента в C#. Для нового поля модели добавьте свойство в C#, заполнение из запроса и Binding в XAML.

Поиск по каталогу здесь выполняется через представление коллекции WPF; SQL загружает доступный каталог и актуальные цены. Оформление заказа повторно проверяет данные в MSSQL. Сохраняйте эту повторную проверку даже при наличии проверки количества в интерфейсе.

Сначала добейтесь запуска без изменений логики, затем меняйте по одному небольшому участку и проверяйте соответствующую функцию. Если хотите перейти к обычным окнам с `x:Class` и `.xaml.cs`, нужно согласованно заменить механизм загрузки в `Ui.cs` и создание форм; одиночного изменения Build Action для этого недостаточно.

## 14. Частые ошибки

| Ошибка / признак | Причина и исправление |
|---|---|
| `Invalid object name 'dbo.products'` | Приложение подключено не к той базе либо таблица называется `products$`, имеет другую схему или ещё не создана. Проверьте Data Source, Initial Catalog и Object Explorer. |
| `Invalid column name` | Столбец назван иначе. Сверьте точные имена с разделом 4 и запросами `Store.cs`. |
| `Specified cast is not valid` при чтении | Например, импортировал ID как `float`, цену как `decimal`, размер как текст. Сверьте SQL-типы с разделом 4. |
| Нельзя вставить NULL в `order_id` или не получается `SCOPE_IDENTITY()` | У `orders.order_id` не включён IDENTITY. Такое же свойство нужно для `order_items.order_item_id`. |
| Нельзя вставить явный ID при импорте | Не включён Enable identity insert в сопоставлениях. |
| Ошибка дублирования PK | Таблица уже содержит импорт или в Excel повторяются ID. Проверьте строки до повторного запуска мастера. |
| FK не создаётся | Есть отсутствующий родительский ID либо типы FK и PK различаются. Исправьте сопоставления и данные. |
| Цена в 100 раз меньше | В поле копеек импортированы рубли. Подготовьте правильные единицы в Excel и исправьте импорт. |
| Размер 36,5 стал 36 | Использован `int`; нужен `decimal(4,1)`, затем восстановление правильных значений из источника. |
| `String or binary data would be truncated` | Текст длиннее столбца. Проверьте размер назначения и чтение Excel-провайдером. |
| `Microsoft.ACE.OLEDB... is not registered` | Нет подходящего провайдера или не совпадает разрядность с мастером. См. раздел 5.5. |
| `Не найден встроенный интерфейс MainView.xaml` | Неправильный Build Action, имя ресурса, Assembly name либо файл не включён. Проверьте `.csproj` из раздела 11.2. |
| Ошибка `InitializeComponent` | Остался код стандартного шаблона, который ссылается на удалённый XAML. Сверьте набор файлов нового проекта. |
| Две точки входа / несколько `Main` | Остался `App.xaml` как ApplicationDefinition или неверный StartupObject. В этом варианте запускается только `ChudoObuv.App`. |
| Duplicate `TargetFrameworkAttribute` | В проекте не задано GenerateTargetFrameworkAttribute=false при сохранённом атрибуте в `App.cs`. |
| Нет `System.Configuration` / `ConfigurationManager` | Не добавлена ссылка System.Configuration либо выбран другой целевой framework. |
| Вместо всех фото заглушки | Проверьте `bin\Debug\Assets\Images`, относительные пути и Copy to Output Directory. |
| Меняется старая база | Неверный Initial Catalog либо задана CHUDOOBUV_CONNECTION_STRING. Проверьте фактическую строку в отладчике. |
| Test Connection успешен, F5 не подключается | IDE и приложение используют разные настройки. Сверьте `App.config` и конфигурацию рядом с EXE. |
| Ошибка сертификата в новом диалоге VS | Для локального учебного экземпляра проверьте Encrypt/Trust Server Certificate; для рабочего удалённого сервера используйте корректный сертификат и настройки его подключения. |
| SSMS не сохраняет изменение, требующее пересоздания таблицы | Лучше исправить схему пустых таблиц до импорта. Для заполненной базы сначала сделайте резервную копию и разберите требуемое изменение; не отключайте защиту пересоздания вслепую. |

При первом запуске открывайте **View → Output → Debug** и Error List. Для ошибки запроса ставьте breakpoint на `ExecuteReader` / `ExecuteNonQuery` в `Store.cs` и смотрите конкретное сообщение MSSQL.

## 15. Подготовить результат к передаче

1. После проверок удалите только созданные для испытаний заказы через приложение.
2. Сохраните решение Visual Studio и исходники.
3. Выберите Release и выполните Rebuild Solution.
4. Для запуска передайте содержимое `bin\Release` вместе с `Assets` и `ChudoObuv.exe.config`.
5. Отдельно сохраните подготовленную книгу Excel, ER-диаграмму и резервную копию базы.
6. Для выполнения требования задания о SQL-файле можно **после ручного создания** базы открыть в SSMS **Tasks → Generate Scripts**, выбрать таблицы и представление, в Advanced установить **Types of data to script = Schema and data**, сохранить полученный файл. Он фиксирует результат вашей работы и не использовался для создания базы в этом маршруте. Настройку выгрузки данных описывает [Microsoft](https://learn.microsoft.com/en-us/ssms/scripting/generate-and-publish-scripts-wizard).

Готовый EXE не содержит саму серверную базу. На другом компьютере потребуется восстановить созданную вами базу либо повторить ручной импорт, после чего указать правильный сервер и имя базы в конфигурации.

Критерий готовности: из Visual Studio запускается WPF-приложение, каталог читает именно `ChudoObuvManual`, новый заказ появляется в этой базе и уменьшает остатки, а удаление через приложение возвращает количество. При этом автоматические скрипты исходного решения остаются доступны как отдельный способ подготовки базы.
