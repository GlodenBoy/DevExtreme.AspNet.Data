# SQL 查询支持功能使用说明

## 功能概述

新增了从 SQL 字符串开始查询的功能，允许你使用原始 SQL 作为基础查询，然后在此基础上应用 DataSourceLoader 的过滤、排序、分页、分组等功能。所有操作都会在数据库层面执行，提供更好的性能。

## 适用场景

- ✅ 需要使用复杂的 SQL 查询逻辑（如存储过程、视图、复杂 JOIN）
- ✅ 需要在 SQL 层面执行自定义查询
- ✅ 需要将 SQL 查询与 DataSourceLoader 的功能结合使用
- ✅ 需要优化查询性能，避免在内存中处理大量数据

## 前置要求

- Entity Framework Core 2.1 或更高版本
- 已配置 DbContext 和 DbSet

## 使用方法

### 方法1：使用 `LoadFromSqlAsync` 方法（推荐）

这是最直接的方式，直接传入 SQL 字符串和 DbSet。

#### 基本用法

```csharp
using DevExtreme.AspNet.Data;
using Microsoft.EntityFrameworkCore;

// 在 Controller 中
[HttpGet("orders-from-sql")]
public async Task<IActionResult> OrdersFromSql(DataSourceLoadOptions loadOptions)
{
    // 定义 SQL 查询
    var sql = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";
    
    // 使用 LoadFromSqlAsync 执行查询
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,  // DbSet<Order>
        sql,              // SQL 查询字符串
        loadOptions       // DataSourceLoadOptions（包含过滤、排序、分页等）
    );
    
    return Json(result);
}
```

#### 带参数的 SQL 查询

```csharp
[HttpGet("orders-from-sql-params")]
public async Task<IActionResult> OrdersFromSqlWithParams(
    DateTime? minDate, 
    DataSourceLoadOptions loadOptions)
{
    // 使用参数化查询（推荐，防止 SQL 注入）
    var sql = "SELECT * FROM Orders WHERE OrderDate >= {0}";
    var parameters = new object[] { minDate ?? new DateTime(1996, 1, 1) };
    
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,
        sql,
        loadOptions,
        CancellationToken.None,
        parameters  // SQL 参数
    );
    
    return Json(result);
}
```

#### 多个参数的 SQL 查询

```csharp
var sql = "SELECT * FROM Orders WHERE OrderDate >= {0} AND ShipCountry = {1}";
var parameters = new object[] { 
    new DateTime(1996, 1, 1), 
    "USA" 
};

var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
    _context.Orders,
    sql,
    loadOptions,
    CancellationToken.None,
    parameters
);
```

### 方法2：使用 `DataSourceLoadOptionsBase.FromSqlRaw` 属性

你也可以在 `DataSourceLoadOptions` 中设置 SQL，然后使用普通的 `LoadAsync` 方法。

```csharp
[HttpGet("orders-via-options")]
public async Task<IActionResult> OrdersViaOptions(DataSourceLoadOptions loadOptions)
{
    // 在 options 中设置 SQL
    loadOptions.FromSqlRaw = "SELECT * FROM Orders WHERE OrderDate >= '1996-01-01'";
    loadOptions.FromSqlParameters = null; // 如果没有参数，设为 null
    
    // 注意：这种方式需要传入 DbSet，而不是 IQueryable
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,
        loadOptions.FromSqlRaw,
        loadOptions,
        CancellationToken.None,
        loadOptions.FromSqlParameters
    );
    
    return Json(result);
}
```

## 完整示例

### 示例1：基础 SQL 查询 + 过滤 + 排序 + 分页

```csharp
[HttpGet("orders")]
public async Task<IActionResult> GetOrders(DataSourceLoadOptions loadOptions)
{
    // SQL 查询作为基础查询
    var sql = @"
        SELECT o.*, c.CompanyName 
        FROM Orders o
        INNER JOIN Customers c ON o.CustomerID = c.CustomerID
        WHERE o.OrderDate >= '1996-01-01'
    ";
    
    var result = await DataSourceLoader.LoadFromSqlAsync<Order>(
        _context.Orders,
        sql,
        loadOptions  // 前端传入的过滤、排序、分页等参数会自动应用
    );
    
    return Json(result);
}
```

**前端调用示例：**
```javascript
// 前端 DevExtreme DataGrid 会自动发送这些参数
// GET /api/orders?skip=0&take=20&sort=OrderDate&filter=["ShipCountry","=","USA"]
```

### 示例2：使用存储过程或视图

```csharp
[HttpGet("order-summary")]
public async Task<IActionResult> GetOrderSummary(DataSourceLoadOptions loadOptions)
{
    // 从视图或存储过程查询
    var sql = "SELECT * FROM vw_OrderSummary WHERE Year = 1996";
    
    var result = await DataSourceLoader.LoadFromSqlAsync<OrderSummary>(
        _context.Set<OrderSummary>(),  // 使用 Set<T>() 方法
        sql,
        loadOptions
    );
    
    return Json(result);
}
```

### 示例3：复杂 SQL + 分组

```csharp
[HttpGet("sales-by-country")]
public async Task<IActionResult> GetSalesByCountry(DataSourceLoadOptions loadOptions)
{
    var sql = @"
        SELECT 
            c.Country,
            COUNT(o.OrderID) as OrderCount,
            SUM(od.Quantity * od.UnitPrice) as TotalSales
        FROM Orders o
        INNER JOIN Customers c ON o.CustomerID = c.CustomerID
        INNER JOIN OrderDetails od ON o.OrderID = od.OrderID
        WHERE o.OrderDate >= '1996-01-01'
        GROUP BY c.Country
    ";
    
    // 注意：如果 SQL 中已经包含 GROUP BY，loadOptions 中的 Group 会在此基础上进一步分组
    var result = await DataSourceLoader.LoadFromSqlAsync<SalesByCountry>(
        _context.Set<SalesByCountry>(),
        sql,
        loadOptions
    );
    
    return Json(result);
}
```

## 参数说明

### `LoadFromSqlAsync<T>` 方法参数

| 参数 | 类型 | 说明 | 必填 |
|------|------|------|------|
| `dbSet` | `object` | DbSet&lt;T&gt; 实例 | ✅ 是 |
| `sql` | `string` | SQL 查询字符串 | ✅ 是 |
| `options` | `DataSourceLoadOptionsBase` | 数据加载选项 | ✅ 是 |
| `cancellationToken` | `CancellationToken` | 取消令牌 | ❌ 否 |
| `parameters` | `object[]` | SQL 参数数组 | ❌ 否 |

### `DataSourceLoadOptionsBase` 新增属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `FromSqlRaw` | `string` | 原始 SQL 查询字符串 |
| `FromSqlParameters` | `object[]` | SQL 查询参数数组 |

### `DataSourceLoadOptions` 常用属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Skip` | `int` | 跳过的记录数（分页） |
| `Take` | `int` | 获取的记录数（分页） |
| `Sort` | `SortingInfo[]` | 排序规则 |
| `Filter` | `IList` | 过滤条件 |
| `Group` | `GroupingInfo[]` | 分组规则 |
| `Select` | `string[]` | 选择的字段 |
| `RequireTotalCount` | `bool` | 是否需要总数 |

## 执行流程说明

1. **SQL 转换为 IQueryable**
   - `FromSqlRaw` 将 SQL 转换为 `IQueryable<T>`
   - 等价于：`_context.Orders.FromSqlRaw(sql)`

2. **构建 LINQ 表达式树**
   - 根据 `loadOptions` 添加 `Where`、`OrderBy`、`Skip`、`Take` 等操作
   - 最终表达式：`FromSqlRaw(sql).Where(...).OrderBy(...).Skip(...).Take(...)`

3. **转换为 SQL 并执行**
   - EFCore 将完整的表达式树转换为 SQL
   - SQL 查询会作为子查询，后续操作在外层执行
   - 最终在数据库层面执行

4. **返回结果**
   - 返回 `LoadResult` 对象，包含数据和总数等信息

## 注意事项

### ⚠️ 重要提示

1. **SQL 注入防护**
   - ✅ **推荐**：使用参数化查询（`{0}`, `{1}` 占位符）
   - ❌ **不推荐**：直接拼接字符串

   ```csharp
   // ✅ 正确：使用参数
   var sql = "SELECT * FROM Orders WHERE OrderDate >= {0}";
   var parameters = new object[] { minDate };
   
   // ❌ 错误：直接拼接（存在 SQL 注入风险）
   var sql = $"SELECT * FROM Orders WHERE OrderDate >= '{minDate}'";
   ```

2. **SQL 查询要求**
   - SQL 必须返回与实体类型匹配的列
   - 列名必须与实体属性名匹配（或使用别名）
   - 如果使用 `Select`，SQL 必须包含所有需要的列

3. **性能考虑**
   - SQL 查询会作为子查询执行
   - 确保 SQL 查询本身有适当的索引
   - 避免在 SQL 中返回过多数据，让分页在数据库层面执行

4. **与现有功能的关系**
   - SQL 查询作为**基础查询**
   - `loadOptions` 中的过滤、排序、分页会**叠加**在 SQL 查询之上
   - 如果 SQL 中已有 `WHERE`，`loadOptions.Filter` 会进一步过滤

5. **类型匹配**
   - `LoadFromSqlAsync<T>` 中的 `T` 必须与 `DbSet<T>` 的类型匹配
   - SQL 返回的列必须能映射到实体类型 `T`

## 常见问题

### Q1: SQL 查询中的列名与实体属性名不匹配怎么办？

**A:** 在 SQL 中使用别名：

```csharp
var sql = @"
    SELECT 
        OrderID as OrderId,
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

### Q4: 支持哪些数据库？

**A:** 支持所有 EFCore 支持的数据库（SQL Server、MySQL、PostgreSQL、SQLite 等）

### Q5: 如何调试生成的 SQL？

**A:** 启用 EFCore 的日志记录：

```csharp
// 在 DbContext 配置中
optionsBuilder.UseSqlServer(connectionString)
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging();
```

## 错误处理

### 常见错误及解决方案

1. **"Entity Framework Core FromSqlRaw method not found"**
   - 确保使用 EFCore 2.1 或更高版本
   - 检查是否正确引用了 `Microsoft.EntityFrameworkCore` 包

2. **"The dbSet parameter must be a DbSet<T> instance"**
   - 确保传入的是 `DbSet<T>`，而不是 `IQueryable<T>`
   - 使用 `_context.Orders` 而不是 `_context.Orders.Where(...)`

3. **"Failed to execute FromSqlRaw"**
   - 检查 SQL 语法是否正确
   - 确保 SQL 返回的列与实体属性匹配
   - 检查参数是否正确传递

## 最佳实践

1. **使用参数化查询**：防止 SQL 注入
2. **限制 SQL 返回的数据量**：在 SQL 中添加适当的 WHERE 条件
3. **使用索引**：确保 SQL 查询涉及的列有索引
4. **测试性能**：使用 EFCore 日志查看实际执行的 SQL
5. **错误处理**：添加 try-catch 处理可能的异常

## 示例代码完整版

```csharp
using DevExtreme.AspNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading;

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

        [HttpGet]
        public async Task<IActionResult> GetOrders(DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 方式1：直接使用 SQL
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

        [HttpGet("with-params")]
        public async Task<IActionResult> GetOrdersWithParams(
            DateTime? minDate,
            DataSourceLoadOptions loadOptions)
        {
            try
            {
                // 方式2：使用参数化查询（推荐）
                var sql = "SELECT * FROM Orders WHERE OrderDate >= {0}";
                var parameters = new object[] { minDate ?? new DateTime(1996, 1, 1) };
                
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
    }
}
```

## 总结

新增的 SQL 查询支持功能让你可以：
- ✅ 使用原始 SQL 作为基础查询
- ✅ 在此基础上应用 DataSourceLoader 的所有功能
- ✅ 在数据库层面执行所有操作，获得更好的性能
- ✅ 灵活处理复杂的查询场景

如有问题，请参考示例代码或查看项目文档。

