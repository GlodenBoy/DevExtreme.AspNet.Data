using DevExtreme.AspNet.Data.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DevExtreme.AspNet.Data {

    class FilterExpressionCompiler : ExpressionCompiler {
        const string
            CONTAINS = "contains",
            NOT_CONTAINS = "notcontains",
            STARTS_WITH = "startswith",
            ENDS_WITH = "endswith";

        bool _stringToLower;

        readonly bool _supportsEqualsMethod;
        readonly IReadOnlyList<GroupingInfo> _groupingInfo;
        readonly IEnumerable<SortingInfo> _sortInfo;

        public FilterExpressionCompiler(Type itemType, bool guardNulls, bool stringToLower = false, bool supportsEqualsMethod = true, IReadOnlyList<GroupingInfo> groupingInfo = null, IEnumerable<SortingInfo> sortInfo = null)
            : base(itemType, guardNulls) {
            _stringToLower = stringToLower;
            _supportsEqualsMethod = supportsEqualsMethod;
            _groupingInfo = groupingInfo;
            _sortInfo = sortInfo;
        }

        public LambdaExpression Compile(IList criteriaJson) {
            var dataItemExpr = CreateItemParam();
            return Expression.Lambda(CompileCore(dataItemExpr, criteriaJson), dataItemExpr);
        }

        Expression CompileCore(ParameterExpression dataItemExpr, IList criteriaJson) {
            if(IsCriteria(criteriaJson[0]))
                return CompileGroup(dataItemExpr, criteriaJson);

            if(IsUnary(criteriaJson)) {
                return CompileUnary(dataItemExpr, criteriaJson);
            }

            return CompileBinary(dataItemExpr, criteriaJson);
        }

        Expression CompileBinary(ParameterExpression dataItemExpr, IList criteriaJson) {
            var hasExplicitOperation = criteriaJson.Count > 2;

            var clientAccessor = Convert.ToString(criteriaJson[0]);
            var clientOperation = hasExplicitOperation ? Convert.ToString(criteriaJson[1]).ToLower() : "=";
            var clientValue = Utils.UnwrapNewtonsoftValue(criteriaJson[hasExplicitOperation ? 2 : 1]);

            // 检查是否是分组字段或排序字段（用于精确匹配）
            bool isGroupingOrSortField = false;

            // 检查字段是否是字符串类型的分组字段或排序字段
            try {
                var property = ItemType.GetProperty(clientAccessor,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase);
                if(property != null) {
                    var propertyType = property.PropertyType;
                    // 处理可空类型
                    if(propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                        propertyType = propertyType.GetGenericArguments()[0];
                    }
                    // 如果是字符串类型，检查是否是分组字段或排序字段
                    if(propertyType == typeof(string)) {
                        // 检查是否是分组字段
                        bool isGroupingField = _groupingInfo != null && _groupingInfo.Any(g =>
                            g.Selector.Equals(clientAccessor, StringComparison.OrdinalIgnoreCase));

                        // 检查是否是排序字段
                        bool isSortField = _sortInfo != null && _sortInfo.Any(s =>
                            s.Selector.Equals(clientAccessor, StringComparison.OrdinalIgnoreCase));

                        // 如果是分组字段或排序字段
                        if(isGroupingField || isSortField) {
                            isGroupingOrSortField = true;
                            // 如果操作符是 "="，自动转换为 "contains"（因为逗号分隔的字符串需要使用精确匹配）
                            if(clientOperation == "=") {
                                clientOperation = CONTAINS;
                            }
                        }
                    }
                }
            } catch {
                // 忽略错误
            }

            var isStringOperation = clientOperation == CONTAINS || clientOperation == NOT_CONTAINS || clientOperation == STARTS_WITH || clientOperation == ENDS_WITH;

            if(CustomFilterCompilers.Binary.CompilerFuncs.Count > 0) {
                var customResult = CustomFilterCompilers.Binary.TryCompile(new BinaryExpressionInfo {
                    DataItemExpression = dataItemExpr,
                    AccessorText = clientAccessor,
                    Operation = clientOperation,
                    Value = clientValue,
                    StringToLower = _stringToLower
                });

                if(customResult != null)
                    return customResult;
            }

            var accessorExpr = CompileAccessorExpression(dataItemExpr, clientAccessor, progression => {
                if(isStringOperation)
                    ForceToString(progression);

                if(_stringToLower)
                    AddToLower(progression);
            });

            if(isStringOperation) {
                // 如果是分组字段或排序字段的 contains 操作，使用精确匹配（避免"张三"匹配到"张三丰"）
                if(isGroupingOrSortField && clientOperation == CONTAINS) {
                    return CompileExactStringMatch(accessorExpr, Convert.ToString(clientValue), false);
                }
                if(isGroupingOrSortField && clientOperation == NOT_CONTAINS) {
                    return CompileExactStringMatch(accessorExpr, Convert.ToString(clientValue), true);
                }
                return CompileStringFunction(accessorExpr, clientOperation, Convert.ToString(clientValue));

            } else {
                var useDynamicBinding = accessorExpr.Type == typeof(Object);
                var expressionType = TranslateBinaryOperation(clientOperation);

                if(!useDynamicBinding) {
                    try {
                        clientValue = Utils.ConvertClientValue(clientValue, accessorExpr.Type);
                    } catch {
                        return Expression.Constant(false);
                    }
                }

                if(clientValue == null && !Utils.CanAssignNull(accessorExpr.Type)) {
                    switch(expressionType) {
                        case ExpressionType.GreaterThan:
                        case ExpressionType.GreaterThanOrEqual:
                        case ExpressionType.LessThan:
                        case ExpressionType.LessThanOrEqual:
                            return Expression.Constant(false);

                        case ExpressionType.Equal:
                        case ExpressionType.NotEqual:
                            accessorExpr = Expression.Convert(accessorExpr, Utils.MakeNullable(accessorExpr.Type));
                            break;
                    }
                }

                if(_stringToLower && clientValue is String)
                    clientValue = ((string)clientValue).ToLower();

                Expression valueExpr = Expression.Constant(clientValue, accessorExpr.Type);

                if(useDynamicBinding) {
                    var compareMethod = typeof(Utils).GetMethod(nameof(Utils.DynamicCompare));
                    return Expression.MakeBinary(
                        expressionType,
                        Expression.Call(compareMethod, accessorExpr, valueExpr, Expression.Constant(_stringToLower)),
                        Expression.Constant(0)
                    );
                }

                if(expressionType == ExpressionType.Equal || expressionType == ExpressionType.NotEqual) {
                    var type = Utils.StripNullableType(accessorExpr.Type);
                    if(_supportsEqualsMethod && !HasEqualityOperator(type)) {
                        if(type.IsValueType) {
                            accessorExpr = Expression.Convert(accessorExpr, typeof(Object));
                            valueExpr = Expression.Convert(valueExpr, typeof(Object));
                        }
                        Expression result = Expression.Call(typeof(Object), "Equals", Type.EmptyTypes, accessorExpr, valueExpr);
                        if(expressionType == ExpressionType.NotEqual)
                            result = Expression.Not(result);
                        return result;
                    }
                }

                if(IsInequality(expressionType)) {
                    var type = Utils.StripNullableType(accessorExpr.Type);
                    if(type.IsEnum) {
                        EnumToUnderlyingType(ref accessorExpr, ref valueExpr);
                    } else if(!HasComparisonOperator(type)) {
                        if(type.IsValueType) {
                            var compareToMethod = type.GetMethod("CompareTo", new[] { type }) ?? type.GetMethod("CompareTo", new[] { typeof(object) });
                            if(compareToMethod != null && !compareToMethod.IsStatic && compareToMethod.ReturnType == typeof(int))
                                return CompileCompareToCall(accessorExpr, expressionType, clientValue, compareToMethod);
                        }

                        var compareMethod = type.GetMethod("Compare", new[] { type, type });
                        if(compareMethod != null && compareMethod.IsStatic && compareMethod.ReturnType == typeof(int)) {
                            return Expression.MakeBinary(
                                expressionType,
                                Expression.Call(compareMethod, accessorExpr, valueExpr),
                                Expression.Constant(0)
                            );
                        }

                        // Comparer<T>.Default fallback?
                    }
                }

                return Expression.MakeBinary(expressionType, accessorExpr, valueExpr);
            }

        }

        bool IsInequality(ExpressionType type) {
            return type == ExpressionType.LessThan || type == ExpressionType.LessThanOrEqual || type == ExpressionType.GreaterThanOrEqual || type == ExpressionType.GreaterThan;
        }

        bool HasEqualityOperator(Type type) {
            if(type.IsEnum || (int)Type.GetTypeCode(type) > 2)
                return true;

            if(type == typeof(Guid) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
                return true;

            var operatorMethod = type.GetMethod("op_Equality", new[] { type, type });
            return operatorMethod != null && operatorMethod.ReturnType == typeof(bool);
        }

        bool HasComparisonOperator(Type type) {
            // Starting with target net7 type.GetMethod("op_GreaterThan", ...) returns not null for Guid
            if(type == typeof(Guid))
                return false;

            if(type.IsEnum)
                return false;

            var code = (int)Type.GetTypeCode(type);
            if(code > 4 && code < 18)
                return true;

            if(type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
                return true;

            return type.GetMethod("op_GreaterThan", new[] { type, type }) != null;
        }

        Expression CompileCompareToCall(Expression accessorExpr, ExpressionType expressionType, object clientValue, MethodInfo compareToMethod) {
            if(clientValue == null)
                return Expression.Constant(false);

            var result = Expression.MakeBinary(
                expressionType,
                Expression.Call(
                    Utils.IsNullable(accessorExpr.Type) ? Expression.Property(accessorExpr, "Value") : accessorExpr,
                    compareToMethod,
                    Expression.Constant(clientValue, compareToMethod.GetParameters()[0].ParameterType)
                ),
                Expression.Constant(0)
            );

            if(GuardNulls) {
                return Expression.Condition(
                    Expression.MakeBinary(ExpressionType.Equal, accessorExpr, Expression.Constant(null)),
                    Expression.Constant(false),
                    result
                );
            }

            return result;
        }

        /// <summary>
        /// 编译精确字符串匹配表达式（用于逗号分隔的字符串字段）
        /// 匹配规则：
        /// 1. field == value （完全相等，可使用索引，性能最好）
        /// 2. field.StartsWith(value + ",") （开头匹配，可使用索引，性能较好）
        /// 3. field.Contains("," + value + ",") （中间匹配，无法使用索引，性能较差）
        /// 4. field.EndsWith("," + value) （结尾匹配，无法使用索引，性能较差）
        /// 这样可以避免"张三"匹配到"张三丰"的问题
        ///
        /// 性能说明：
        /// - 前两个条件（== 和 StartsWith）可以使用索引，性能较好
        /// - 后两个条件（Contains 和 EndsWith）需要全表扫描，性能较差
        /// - 数据库优化器通常会先执行可以使用索引的条件，如果找到匹配就不执行后面的条件
        /// - 建议在字段上创建索引以提升性能：CREATE INDEX idx_field ON table(field)
        /// </summary>
        Expression CompileExactStringMatch(Expression accessorExpr, string value, bool invert) {
            if(_stringToLower && value != null)
                value = value.ToLower();

            if(GuardNulls)
                accessorExpr = Expression.Coalesce(accessorExpr, Expression.Constant(""));

            // 构建四个匹配条件（按性能从好到差排序，让数据库优化器优先执行可以使用索引的条件）
            var stringType = typeof(string);
            var valueConst = Expression.Constant(value, stringType);
            var valueCommaConst = Expression.Constant(value + ",", stringType);
            var commaValueConst = Expression.Constant("," + value, stringType);
            var commaValueCommaConst = Expression.Constant("," + value + ",", stringType);

            // 1. field == value （完全相等，可使用索引，性能最好）
            var equalsExpr = Expression.Equal(accessorExpr, valueConst);

            // 2. field.StartsWith(value + ",") （开头匹配，可使用索引，性能较好）
            var startsWithMethod = stringType.GetMethod(nameof(string.StartsWith), new[] { stringType });
            var startsWithExpr = Expression.Call(accessorExpr, startsWithMethod, valueCommaConst);

            // 3. field.Contains("," + value + ",") （中间匹配，无法使用索引，性能较差）
            var containsMethod = stringType.GetMethod(nameof(string.Contains), new[] { stringType });
            var containsExpr = Expression.Call(accessorExpr, containsMethod, commaValueCommaConst);

            // 4. field.EndsWith("," + value) （结尾匹配，无法使用索引，性能较差）
            var endsWithMethod = stringType.GetMethod(nameof(string.EndsWith), new[] { stringType });
            var endsWithExpr = Expression.Call(accessorExpr, endsWithMethod, commaValueConst);

            // 组合四个条件：OR 连接
            // 注意：将可以使用索引的条件放在前面，数据库优化器可能会优先执行这些条件
            // 如果前面的条件已经找到匹配，可能不会执行后面的全表扫描条件
            Expression result = Expression.OrElse(equalsExpr, startsWithExpr);
            result = Expression.OrElse(result, containsExpr);
            result = Expression.OrElse(result, endsWithExpr);

            // 如果需要取反（NOT_CONTAINS）
            if(invert)
                result = Expression.Not(result);

            return result;
        }

        Expression CompileStringFunction(Expression accessorExpr, string clientOperation, string value) {
            if(_stringToLower && value != null)
                value = value.ToLower();

            var invert = false;

            if(clientOperation == NOT_CONTAINS) {
                clientOperation = CONTAINS;
                invert = true;
            }

            if(GuardNulls)
                accessorExpr = Expression.Coalesce(accessorExpr, Expression.Constant(""));

            var operationMethod = typeof(String).GetMethod(GetStringOperationMethodName(clientOperation), new[] { typeof(String) });

            Expression result = Expression.Call(accessorExpr, operationMethod, Expression.Constant(value));

            if(invert)
                result = Expression.Not(result);

            return result;
        }

        Expression CompileGroup(ParameterExpression dataItemExpr, IList criteriaJson) {
            var operands = new List<Expression>();
            var isAnd = true;
            var nextIsAnd = true;

            foreach(var item in criteriaJson) {
                var operandJson = item as IList;

                if(IsCriteria(operandJson)) {
                    if(operands.Count > 1 && isAnd != nextIsAnd)
                        throw new ArgumentException("Mixing of and/or is not allowed inside a single group");

                    isAnd = nextIsAnd;
                    operands.Add(CompileCore(dataItemExpr, operandJson));
                    nextIsAnd = true;
                } else {
                    nextIsAnd = Regex.IsMatch(Convert.ToString(item), "and|&", RegexOptions.IgnoreCase);
                }
            }

            Expression result = null;
            var op = isAnd ? ExpressionType.AndAlso : ExpressionType.OrElse;

            foreach(var operand in operands) {
                if(result == null)
                    result = operand;
                else
                    result = Expression.MakeBinary(op, result, operand);
            }

            return result;
        }

        Expression CompileUnary(ParameterExpression dataItemExpr, IList criteriaJson) {
            return Expression.Not(CompileCore(dataItemExpr, (IList)criteriaJson[1]));
        }

        ExpressionType TranslateBinaryOperation(string clientOperation) {
            switch(clientOperation) {
                case "=":
                    return ExpressionType.Equal;

                case "<>":
                    return ExpressionType.NotEqual;

                case ">":
                    return ExpressionType.GreaterThan;

                case ">=":
                    return ExpressionType.GreaterThanOrEqual;

                case "<":
                    return ExpressionType.LessThan;

                case "<=":
                    return ExpressionType.LessThanOrEqual;
            }

            throw new NotSupportedException();
        }

        bool IsCriteria(object item) {
            return item is IList && !(item is String);
        }

        internal bool IsUnary(IList criteriaJson) {
            return Convert.ToString(criteriaJson[0]) == "!";
        }

        string GetStringOperationMethodName(string clientOperation) {
            if(clientOperation == STARTS_WITH)
                return nameof(String.StartsWith);

            if(clientOperation == ENDS_WITH)
                return nameof(String.EndsWith);

            return nameof(String.Contains);
        }

        static void AddToLower(List<Expression> progression) {
            var last = progression.Last();

            if(last.Type != typeof(String))
                return;

            var toLowerMethod = typeof(String).GetMethod(nameof(String.ToLower), Type.EmptyTypes);
            var toLowerCall = Expression.Call(last, toLowerMethod);

            if(last is MethodCallExpression lastCall && lastCall.Method.Name == nameof(ToString))
                progression.RemoveAt(progression.Count - 1);

            progression.Add(toLowerCall);
        }

        static void EnumToUnderlyingType(ref Expression accessorExpr, ref Expression valueExpr) {
            var isNullable = Utils.IsNullable(accessorExpr.Type);

            var underlyingType = Enum.GetUnderlyingType(Utils.StripNullableType(accessorExpr.Type));
            if(isNullable)
                underlyingType = typeof(Nullable<>).MakeGenericType(underlyingType);

            accessorExpr = Expression.Convert(accessorExpr, underlyingType);

            if(valueExpr is ConstantExpression valueConstExpr) {
                var newValue = Utils.ConvertClientValue(valueConstExpr.Value, underlyingType);
                valueExpr = Expression.Constant(newValue, underlyingType);
            } else {
                valueExpr = Expression.Convert(valueExpr, underlyingType);
            }
        }

        class BinaryExpressionInfo : IBinaryExpressionInfo {
            public Expression DataItemExpression { get; set; }
            public string AccessorText { get; set; }
            public string Operation { get; set; }
            public object Value { get; set; }
            public bool StringToLower { get; set; }
        }
    }

}
