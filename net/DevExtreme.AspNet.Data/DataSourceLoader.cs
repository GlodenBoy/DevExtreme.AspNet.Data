using DevExtreme.AspNet.Data.ResponseModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DevExtreme.AspNet.Data {

    /// <summary>
    /// Provides static methods for loading data from collections that implement the
    /// <see cref="System.Collections.Generic.IEnumerable{T}"/> or <see cref="System.Linq.IQueryable{T}"/> interface.
    /// </summary>
    public class DataSourceLoader {

        /// <summary>
        /// Loads data from a collection that implements the <see cref="System.Collections.Generic.IEnumerable{T}"/> interface.
        /// </summary>
        /// <typeparam name="T">The type of objects in the collection.</typeparam>
        /// <param name="source">A collection that implements the <see cref="System.Collections.Generic.IEnumerable{T}"/> interface.</param>
        /// <param name="options">Data processing settings when loading data.</param>
        /// <returns>The load result.</returns>
        public static LoadResult Load<T>(IEnumerable<T> source, DataSourceLoadOptionsBase options) {
            return Load(source.AsQueryable(), options);
        }

        /// <summary>
        /// Loads data from a collection that implements the <see cref="System.Linq.IQueryable{T}"/> interface.
        /// </summary>
        /// <typeparam name="T">The type of objects in the collection.</typeparam>
        /// <param name="source">A collection that implements the <see cref="System.Linq.IQueryable{T}"/> interface.</param>
        /// <param name="options">Data processing settings when loading data.</param>
        /// <returns>The load result.</returns>
        public static LoadResult Load<T>(IQueryable<T> source, DataSourceLoadOptionsBase options) {
            return LoadAsync(source, options, CancellationToken.None, true).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously loads data from a collection that implements the <see cref="System.Linq.IQueryable{T}"/> interface.
        /// </summary>
        /// <typeparam name="T">The type of objects in the collection.</typeparam>
        /// <param name="source">A collection that implements the <see cref="System.Linq.IQueryable{T}"/> interface.</param>
        /// <param name="options">Data processing settings when loading data.</param>
        /// <param name="cancellationToken">A <see cref="System.Threading.CancellationToken"/> object that delivers a cancellation notice to the running operation.</param>
        /// <returns>
        /// A <see cref="System.Threading.Tasks.Task{TResult}"/> object that represents the asynchronous operation.
        /// The task result contains the load result.
        /// </returns>
        public static Task<LoadResult> LoadAsync<T>(IQueryable<T> source, DataSourceLoadOptionsBase options, CancellationToken cancellationToken = default(CancellationToken)) {
            return LoadAsync(source, options, cancellationToken, false);
        }

        /// <summary>
        /// Loads data from a raw SQL query using Entity Framework Core.
        /// This method creates a queryable source from SQL and then applies the data processing options.
        /// </summary>
        /// <typeparam name="T">The type of objects in the collection (entity type).</typeparam>
        /// <param name="dbSet">A DbSet&lt;T&gt; instance from Entity Framework Core.</param>
        /// <param name="sql">The raw SQL query string.</param>
        /// <param name="options">Data processing settings when loading data.</param>
        /// <param name="parameters">Optional parameters for the SQL query.</param>
        /// <returns>The load result.</returns>
        /// <exception cref="InvalidOperationException">Thrown when Entity Framework Core is not available or the dbSet is not a DbSet.</exception>
        public static LoadResult LoadFromSql<T>(object dbSet, string sql, DataSourceLoadOptionsBase options, params object[] parameters) {
            return LoadFromSqlAsync<T>(dbSet, sql, options, CancellationToken.None, parameters).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously loads data from a raw SQL query using Entity Framework Core.
        /// This method creates a queryable source from SQL and then applies the data processing options.
        /// </summary>
        /// <typeparam name="T">The type of objects in the collection (entity type).</typeparam>
        /// <param name="dbSet">A DbSet&lt;T&gt; instance from Entity Framework Core.</param>
        /// <param name="sql">The raw SQL query string.</param>
        /// <param name="options">Data processing settings when loading data.</param>
        /// <param name="cancellationToken">A <see cref="System.Threading.CancellationToken"/> object that delivers a cancellation notice to the running operation.</param>
        /// <param name="parameters">Optional parameters for the SQL query.</param>
        /// <returns>
        /// A <see cref="System.Threading.Tasks.Task{TResult}"/> object that represents the asynchronous operation.
        /// The task result contains the load result.
        /// </returns>
        /// <exception cref="InvalidOperationException">Thrown when Entity Framework Core is not available or the dbSet is not a DbSet.</exception>
        public static Task<LoadResult> LoadFromSqlAsync<T>(object dbSet, string sql, DataSourceLoadOptionsBase options, CancellationToken cancellationToken = default(CancellationToken), params object[] parameters) {
            return LoadFromSqlAsync<T>(dbSet, sql, options, cancellationToken, false, parameters);
        }

        static Task<LoadResult> LoadFromSqlAsync<T>(object dbSet, string sql, DataSourceLoadOptionsBase options, CancellationToken ct, bool sync, params object[] parameters) {
            var queryable = CreateQueryableFromSql<T>(dbSet, sql, parameters);
            return LoadAsync(queryable, options, ct, sync);
        }

        static IQueryable<T> CreateQueryableFromSql<T>(object dbSet, string sql, params object[] parameters) {
            if(dbSet == null)
                throw new ArgumentNullException(nameof(dbSet));
            if(string.IsNullOrEmpty(sql))
                throw new ArgumentException("SQL query string cannot be null or empty.", nameof(sql));

            // FromSqlRaw 是扩展方法，定义在 Microsoft.EntityFrameworkCore.RelationalQueryableExtensions 中
            // 方法签名: public static IQueryable<TEntity> FromSqlRaw<TEntity>(this DbSet<TEntity> source, string sql, params object[] parameters)
            MethodInfo fromSqlRawMethod = null;

            try {
                // 查找扩展方法所在的类型
#pragma warning disable DX0004 // known assembly and types
                var extensionsType = Type.GetType("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions, Microsoft.EntityFrameworkCore.Relational");
#pragma warning restore DX0004 // known assembly and types

                if(extensionsType == null) {
                    // 如果 Relational 程序集不存在，尝试在 Microsoft.EntityFrameworkCore 中查找
#pragma warning disable DX0004 // known assembly and types
                    extensionsType = Type.GetType("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions, Microsoft.EntityFrameworkCore");
#pragma warning restore DX0004 // known assembly and types
                }

                if(extensionsType != null) {
                    // 查找 FromSqlRaw 扩展方法
                    var methods = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == "FromSqlRaw" && m.IsGenericMethod)
                        .ToList();

                    // 查找匹配的方法签名: FromSqlRaw<TEntity>(DbSet<TEntity> source, string sql, params object[] parameters)
                    // params 参数在反射中可能显示为单个 object[] 参数（2个参数）或多个参数（3+个参数）
                    foreach(var method in methods) {
                        var methodParams = method.GetParameters();
                        // 检查第一个参数是否是 DbSet<T>
                        if(methodParams.Length >= 2 &&
                           methodParams[0].ParameterType.IsGenericType &&
                           methodParams[0].ParameterType.GetGenericTypeDefinition().Name == "DbSet`1" &&
                           methodParams[1].ParameterType == typeof(string)) {
                            // 检查最后一个参数是否是 object[]（params 参数）
                            var lastParam = methodParams[methodParams.Length - 1];
                            if(lastParam.ParameterType == typeof(object[]) && lastParam.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0) {
                                // 找到匹配的方法
                                fromSqlRawMethod = method;
                                break;
                            } else if(methodParams.Length == 2) {
                                // 如果只有2个参数，第二个是 string，可能是没有 params 的重载，但我们仍然可以使用
                                fromSqlRawMethod = method;
                                break;
                            }
                        }
                    }

                    // 如果没找到，尝试更宽松的匹配（只要第一个参数是 DbSet，第二个是 string）
                    if(fromSqlRawMethod == null) {
                        fromSqlRawMethod = methods.FirstOrDefault(m => {
                            var methodParams = m.GetParameters();
                            return methodParams.Length >= 2 &&
                                   methodParams[0].ParameterType.IsGenericType &&
                                   methodParams[0].ParameterType.GetGenericTypeDefinition().Name == "DbSet`1" &&
                                   methodParams[1].ParameterType == typeof(string);
                        });
                    }
                }
            } catch {
                // 忽略异常，继续查找
            }

            // 如果还是没找到，提供更详细的错误信息
            if(fromSqlRawMethod == null) {
                var dbSetType = dbSet.GetType();
                var typeName = dbSetType.FullName ?? dbSetType.Name;
                var isQueryable = dbSet is IQueryable;

                var errorMessage = "Entity Framework Core FromSqlRaw extension method not found. ";
                errorMessage += $"The provided object type is: {typeName}. ";

                if(isQueryable) {
                    errorMessage += "The object is an IQueryable. ";
                    errorMessage += "Please ensure you are passing a DbSet<T> instance (e.g., _context.Orders or _context.Set<T>()). ";
                    errorMessage += "Do not pass a filtered query (e.g., _context.Orders.Where(...)). ";
                } else {
                    errorMessage += "Please ensure you are using Entity Framework Core 2.1 or later, ";
                    errorMessage += "and that the Microsoft.EntityFrameworkCore.Relational package is installed.";
                }

                throw new InvalidOperationException(errorMessage);
            }

            try {
                // 调用扩展方法：FromSqlRaw<T>(dbSet, sql, parameters)
                var genericMethod = fromSqlRawMethod.MakeGenericMethod(typeof(T));
                var result = genericMethod.Invoke(null, new object[] { dbSet, sql, parameters ?? new object[0] });
                return (IQueryable<T>)result;
            } catch(Exception ex) {
                var dbSetType = dbSet.GetType();
                var typeName = dbSetType.FullName ?? dbSetType.Name;
                var errorMessage = $"Failed to execute FromSqlRaw on type {typeName}. ";
                errorMessage += "Make sure the SQL query is valid and Entity Framework Core is properly configured.";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        static Task<LoadResult> LoadAsync<T>(IQueryable<T> source, DataSourceLoadOptionsBase options, CancellationToken ct, bool sync) {
            return new DataSourceLoaderImpl<T>(source, options, ct, sync).LoadAsync();
        }

    }

}
