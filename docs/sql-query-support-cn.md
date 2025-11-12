# SQL 查询支持功能使用说明书

## 📋 目录

1. [功能概述](#功能概述)
2. [快速开始](#快速开始)
3. [详细使用方法](#详细使用方法)
4. [参数说明](#参数说明)
5. [完整示例](#完整示例)
6. [注意事项](#注意事项)
7. [常见问题](#常见问题)

---

## 功能概述

### 什么是 SQL 查询支持？

新增的功能允许你**直接使用 SQL 字符串作为基础查询**，然后在这个基础上应用 DataSourceLoader 的所有功能（过滤、排序、分页、分组等）。所有操作都会在**数据库层面执行**，提供更好的性能。

### 适用场景

✅ **适合使用的情况：**
- 需要使用复杂的 SQL 查询逻辑（存储过程、视图、复杂 JOIN）
- 需要在 SQL 层面执行自定义查询
- 需要优化查询性能，避免在内存中处理大量数据
- 需要将 SQL 查询与 DataSourceLoader 的功能结合使用

❌ **不适合的情况：**
- 简单的 LINQ 查询已经足够
- 不需要 SQL 层面的复杂逻辑

---

## 快速开始

### 前置要求

- ✅ Entity Framework Core 2.1 或更高版本
- ✅ 已配置 DbContext 和 DbSet

### 最简单的例子

```csharp
using DevExtreme.AspNet.Data;
using Microsoft.AspNetCore.Mvc;

[HttpGet("orders")]
public async Task<IActionResult> GetOrders(DataSourceLoadOptions loadOptions)
{
    // 1. 定义 SQL 查询
    var sql = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";
    
    // 2. 使用 LoadFromSqlAsync 执行查询
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,  // DbSet<Order>
        sql,              // SQL 查询字符串
        loadOptions       // 前端传入的过滤、排序、分页参数
    );
    
    // 3. 返回结果
    return Json(result);
}
```

**前端调用：**
```javascript
// DevExtreme DataGrid 会自动发送这些参数
// GET /api/orders?skip=0&take=20&sort=OrderDate&filter=["ShipCountry","=","USA"]
```

---

## 详细使用方法

### 方法1：基本 SQL 查询

```csharp
[HttpGet("orders")]
public async Task<IActionResult> GetOrders(DataSourceLoadOptions loadOptions)
{
    var sql = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";
    
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,
        sql,
        loadOptions
    );
    
    return Json(result);
}
```

### 方法2：带参数的 SQL 查询（推荐）

**为什么要使用参数？**
- ✅ 防止 SQL 注入攻击
- ✅ 提高性能（查询计划缓存）
- ✅ 代码更清晰

```csharp
[HttpGet("orders")]
public async Task<IActionResult> GetOrders(
    DateTime? minDate,
    DataSourceLoadOptions loadOptions)
{
    // 使用 {0} 作为占位符
    var sql = "SELECT * FROM Orders WHERE OrderDate >= {0}";
    
    // 传入参数数组
    var parameters = new object[] { 
        minDate ?? new DateTime(1996, 1, 1) 
    };
    
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,
        sql,
        loadOptions,
        CancellationToken.None,
        parameters  // 传入参数
    );
    
    return Json(result);
}
```

### 方法3：多个参数

```csharp
var sql = "SELECT * FROM Orders WHERE OrderDate >= {0} AND ShipCountry = {1}";
var parameters = new object[] { 
    new DateTime(1996, 1, 1),  // {0}
    "USA"                        // {1}
};

var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
    _context.Orders,
    sql,
    loadOptions,
    CancellationToken.None,
    parameters
);
```

### 方法4：使用 JOIN 查询

```csharp
[HttpGet("orders-with-customer")]
public async Task<IActionResult> GetOrdersWithCustomer(DataSourceLoadOptions loadOptions)
{
    var sql = @"
        SELECT 
            o.OrderID,
            o.OrderDate,
            o.ShipCountry,
            c.CompanyName as CustomerName
        FROM Orders o
        INNER JOIN Customers c ON o.CustomerID = c.CustomerID
        WHERE o.OrderDate >= '1996-01-01'
    ";
    
    // 注意：需要创建一个包含这些字段的 DTO 类
    var result = await DataSourceLoader.LoadFromSqlAsync<OrderDto>(
        _context.Set<OrderDto>(),
        sql,
        loadOptions
    );
    
    return Json(result);
}

// DTO 类
public class OrderDto
{
    public int OrderID { get; set; }
    public DateTime OrderDate { get; set; }
    public string ShipCountry { get; set; }
    public string CustomerName { get; set; }
}
```

---

## 参数说明

### `LoadFromSqlAsync<T>` 方法参数

| 参数名 | 类型 | 说明 | 必填 | 示例 |
|--------|------|------|------|------|
| `dbSet` | `object` | DbSet&lt;T&gt; 实例 | ✅ | `_context.Orders` |
| `sql` | `string` | SQL 查询字符串 | ✅ | `"SELECT * FROM Orders"` |
| `options` | `DataSourceLoadOptionsBase` | 数据加载选项 | ✅ | `loadOptions` |
| `cancellationToken` | `CancellationToken` | 取消令牌 | ❌ | `CancellationToken.None` |
| `parameters` | `object[]` | SQL 参数数组 | ❌ | `new object[] { date }` |

### `DataSourceLoadOptions` 常用属性

| 属性 | 类型 | 说明 | 示例 |
|------|------|------|------|
| `Skip` | `int` | 跳过的记录数（分页） | `10` |
| `Take` | `int` | 获取的记录数（分页） | `20` |
| `Sort` | `SortingInfo[]` | 排序规则 | `[{ selector: "OrderDate", desc: true }]` |
| `Filter` | `IList` | 过滤条件 | `["ShipCountry", "=", "USA"]` |
| `Group` | `GroupingInfo[]` | 分组规则 | `[{ selector: "ShipCountry" }]` |
| `Select` | `string[]` | 选择的字段 | `["OrderID", "OrderDate"]` |
| `RequireTotalCount` | `bool` | 是否需要总数 | `true` |

---

## 完整示例

### 示例1：基础查询 + 过滤 + 排序 + 分页

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly NorthwindContext _context;

    public OrdersController(NorthwindContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders(DataSourceLoadOptions loadOptions)
    {
        // SQL 作为基础查询
        var sql = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";
        
        // loadOptions 会自动应用前端传入的过滤、排序、分页等
        var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
            _context.Orders,
            sql,
            loadOptions
        );
        
        return Ok(result);
    }
}
```

**前端调用：**
```
GET /api/orders?skip=0&take=20&sort=OrderDate&filter=["ShipCountry","=","USA"]
```

**实际执行的 SQL（简化版）：**
```sql
SELECT * FROM (
    SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'
) AS [t]
WHERE [t].[ShipCountry] = N'USA'
ORDER BY [t].[OrderDate] DESC
OFFSET 0 ROWS FETCH NEXT 20 ROWS ONLY
```

### 示例2：使用存储过程或视图

```csharp
[HttpGet("summary")]
public async Task<IActionResult> GetOrderSummary(DataSourceLoadOptions loadOptions)
{
    // 从视图查询
    var sql = "SELECT * FROM vw_OrderSummary WHERE Year = 1996";
    
    var result = await DataSourceLoader.LoadFromSqlAsync<OrderSummary>(
        _context.Set<OrderSummary>(),
        sql,
        loadOptions
    );
    
    return Ok(result);
}
```

### 示例3：复杂查询 + 错误处理

```csharp
[HttpGet("complex")]
public async Task<IActionResult> GetComplexData(
    DateTime? minDate,
    string country,
    DataSourceLoadOptions loadOptions)
{
    try
    {
        // 参数化查询
        var sql = @"
            SELECT 
                o.OrderID,
                o.OrderDate,
                o.ShipCountry,
                c.CompanyName as CustomerName,
                COUNT(od.OrderDetailID) as ItemCount
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerID = c.CustomerID
            LEFT JOIN OrderDetails od ON o.OrderID = od.OrderID
            WHERE o.OrderDate >= {0} AND o.ShipCountry = {1}
            GROUP BY o.OrderID, o.OrderDate, o.ShipCountry, c.CompanyName
        ";
        
        var parameters = new object[] { 
            minDate ?? new DateTime(1996, 1, 1),
            country ?? "USA"
        };
        
        var result = await DataSourceLoader.LoadFromSqlAsync<OrderComplexDto>(
            _context.Set<OrderComplexDto>(),
            sql,
            loadOptions,
            CancellationToken.None,
            parameters
        );
        
        return Ok(result);
    }
    catch (Exception ex)
    {
        // 错误处理
        return StatusCode(500, new { 
            error = "查询失败",
            message = ex.Message 
        });
    }
}
```

---

## 注意事项

### ⚠️ 安全提示

#### 1. SQL 注入防护（非常重要！）

**❌ 错误做法：**
```csharp
// 危险！存在 SQL 注入风险
var sql = $"SELECT * FROM Orders WHERE CustomerID = '{customerId}'";
```

**✅ 正确做法：**
```csharp
// 安全！使用参数化查询
var sql = "SELECT * FROM Orders WHERE CustomerID = {0}";
var parameters = new object[] { customerId };
```

#### 2. SQL 查询要求

- SQL 必须返回与实体类型匹配的列
- 列名必须与实体属性名匹配（或使用别名）
- 如果使用 `Select`，SQL 必须包含所有需要的列

**示例：**
```csharp
// 实体类
public class Order
{
    public int OrderId { get; set; }
    public DateTime OrderDate { get; set; }
}

// SQL 查询（使用别名匹配属性名）
var sql = @"
    SELECT 
        OrderID as OrderId,      -- 使用别名匹配属性名
        OrderDate as OrderDate
    FROM Orders
";
```

#### 3. 性能优化建议

- ✅ 在 SQL 中添加适当的 WHERE 条件，限制返回的数据量
- ✅ 确保 SQL 查询涉及的列有索引
- ✅ 使用参数化查询，利用查询计划缓存
- ✅ 避免在 SQL 中返回过多数据，让分页在数据库层面执行

#### 4. 与现有功能的关系

- SQL 查询作为**基础查询**
- `loadOptions` 中的过滤、排序、分页会**叠加**在 SQL 查询之上
- 如果 SQL 中已有 `WHERE`，`loadOptions.Filter` 会进一步过滤

**示例：**
```csharp
// SQL 中已有 WHERE
var sql = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";

// loadOptions.Filter 会进一步过滤
// 最终效果：SQL 的 WHERE + loadOptions.Filter
```

---

## 常见问题

### Q1: SQL 查询中的列名与实体属性名不匹配怎么办？

**A:** 在 SQL 中使用别名：

```csharp
var sql = @"
    SELECT 
        OrderID as OrderId,           -- 匹配实体属性名
        OrderDate as OrderDate,
        ShipCountry as ShipCountry
    FROM Orders
";
```

### Q2: 可以使用存储过程吗？

**A:** 可以，但需要确保存储过程返回的结果能映射到实体类型：

```csharp
var sql = "EXEC sp_GetOrders @Year = 1996";
// 注意：存储过程返回的列必须与实体属性匹配
```

### Q3: SQL 查询中的 WHERE 和 loadOptions.Filter 会冲突吗？

**A:** 不会冲突，它们是叠加关系：
- SQL 中的 WHERE 作为基础过滤
- `loadOptions.Filter` 会在此基础上进一步过滤

**示例：**
```sql
-- SQL 查询
SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'

-- loadOptions.Filter: ["ShipCountry", "=", "USA"]

-- 最终执行的 SQL
SELECT * FROM (
    SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'
) AS [t]
WHERE [t].[ShipCountry] = N'USA'
```

### Q4: 支持哪些数据库？

**A:** 支持所有 EFCore 支持的数据库：
- ✅ SQL Server
- ✅ MySQL
- ✅ PostgreSQL
- ✅ SQLite
- ✅ Oracle
- ✅ 其他 EFCore 支持的数据库

### Q5: 如何调试生成的 SQL？

**A:** 启用 EFCore 的日志记录：

```csharp
// 在 DbContext 配置中（Startup.cs 或 Program.cs）
services.AddDbContext<YourDbContext>(options =>
{
    options.UseSqlServer(connectionString)
        .LogTo(Console.WriteLine, LogLevel.Information)  // 输出到控制台
        .EnableSensitiveDataLogging();                      // 显示敏感数据
});
```

### Q6: 如何处理 SQL 查询返回的列与实体不匹配？

**A:** 创建一个 DTO 类来匹配 SQL 返回的列：

```csharp
// SQL 返回的列
var sql = @"
    SELECT 
        o.OrderID,
        c.CompanyName as CustomerName
    FROM Orders o
    JOIN Customers c ON o.CustomerID = c.CustomerID
";

// 创建匹配的 DTO
public class OrderWithCustomerDto
{
    public int OrderID { get; set; }
    public string CustomerName { get; set; }
}

// 使用 DTO
var result = await DataSourceLoader.LoadFromSqlAsync<OrderWithCustomerDto>(
    _context.Set<OrderWithCustomerDto>(),
    sql,
    loadOptions
);
```

### Q7: 同步方法如何使用？

**A:** 使用 `LoadFromSql` 方法（同步版本）：

```csharp
var sql = "SELECT * FROM Orders";
var result = DataSourceLoader.LoadFromSql<Order>(
    _context.Orders,
    sql,
    loadOptions
);
```

---

## 错误处理

### 常见错误及解决方案

#### 1. "Entity Framework Core FromSqlRaw method not found"

**原因：** EFCore 版本太低

**解决方案：**
- 确保使用 EFCore 2.1 或更高版本
- 检查是否正确引用了 `Microsoft.EntityFrameworkCore` 包

#### 2. "The dbSet parameter must be a DbSet<T> instance"

**原因：** 传入的不是 DbSet

**解决方案：**
```csharp
// ❌ 错误
var query = _context.Orders.Where(o => o.OrderDate > DateTime.Now);
DataSourceLoader.LoadFromSqlAsync<Order>(query, sql, options);  // 错误！

// ✅ 正确
DataSourceLoader.LoadFromSqlAsync<Order>(_context.Orders, sql, options);  // 正确！
```

#### 3. "Failed to execute FromSqlRaw"

**原因：** SQL 语法错误或列不匹配

**解决方案：**
- 检查 SQL 语法是否正确
- 确保 SQL 返回的列与实体属性匹配
- 检查参数是否正确传递

---

## 最佳实践总结

1. ✅ **使用参数化查询**：防止 SQL 注入
2. ✅ **限制 SQL 返回的数据量**：在 SQL 中添加适当的 WHERE 条件
3. ✅ **使用索引**：确保 SQL 查询涉及的列有索引
4. ✅ **测试性能**：使用 EFCore 日志查看实际执行的 SQL
5. ✅ **错误处理**：添加 try-catch 处理可能的异常
6. ✅ **使用 DTO**：当 SQL 返回的列与实体不匹配时，创建 DTO 类

---

## 完整示例代码

```csharp
using DevExtreme.AspNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly YourDbContext _context;

        public OrdersController(YourDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// 基础 SQL 查询示例
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetOrders(DataSourceLoadOptions loadOptions)
        {
            try
            {
                var sql = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";
                
                var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
                    _context.Orders,
                    sql,
                    loadOptions
                );
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 参数化 SQL 查询示例（推荐）
        /// </summary>
        [HttpGet("with-params")]
        public async Task<IActionResult> GetOrdersWithParams(
            DateTime? minDate,
            DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 使用参数化查询
                var sql = "SELECT * FROM Orders WHERE OrderDate >= {0}";
                var parameters = new object[] { 
                    minDate ?? new DateTime(1996, 1, 1) 
                };
                
                var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
                    _context.Orders,
                    sql,
                    loadOptions,
                    CancellationToken.None,
                    parameters
                );
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 复杂 JOIN 查询示例
        /// </summary>
        [HttpGet("with-customer")]
        public async Task<IActionResult> GetOrdersWithCustomer(DataSourceLoadOptions loadOptions)
        {
            try
            {
                var sql = @"
                    SELECT 
                        o.OrderID,
                        o.OrderDate,
                        o.ShipCountry,
                        c.CompanyName as CustomerName
                    FROM Orders o
                    INNER JOIN Customers c ON o.CustomerID = c.CustomerID
                    WHERE o.OrderDate >= '1996-01-01'
                ";
                
                var result = await DataSourceLoader.LoadFromSqlAsync<OrderWithCustomerDto>(
                    _context.Set<OrderWithCustomerDto>(),
                    sql,
                    loadOptions
                );
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    // DTO 类
    public class OrderWithCustomerDto
    {
        public int OrderID { get; set; }
        public DateTime OrderDate { get; set; }
        public string ShipCountry { get; set; }
        public string CustomerName { get; set; }
    }
}
```

---

## 总结

新增的 SQL 查询支持功能让你可以：

- ✅ 使用原始 SQL 作为基础查询
- ✅ 在此基础上应用 DataSourceLoader 的所有功能
- ✅ 在数据库层面执行所有操作，获得更好的性能
- ✅ 灵活处理复杂的查询场景

**记住：**
- 🔒 始终使用参数化查询防止 SQL 注入
- 📊 使用 EFCore 日志调试 SQL
- 🎯 创建 DTO 类匹配 SQL 返回的列
- ⚡ 优化 SQL 查询性能

如有问题，请参考示例代码或查看项目文档。

