using DevExtreme.AspNet.Data.Aggregation;
using DevExtreme.AspNet.Data.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace DevExtreme.AspNet.Data.RemoteGrouping {

    class RemoteGroupExpressionCompiler : ExpressionCompiler {
        bool _expandSumType;
        AnonTypeNewTweaks _anonTypeNewTweaks;
        IEnumerable<GroupingInfo> _grouping;
        IEnumerable<SummaryInfo>
            _totalSummary,
            _groupSummary;

        public RemoteGroupExpressionCompiler(Type itemType, bool guardNulls, bool expandSumType, AnonTypeNewTweaks anonTypeNewTweaks, IEnumerable<GroupingInfo> grouping, IEnumerable<SummaryInfo> totalSummary, IEnumerable<SummaryInfo> groupSummary)
            : base(itemType, guardNulls) {
            _expandSumType = expandSumType;
            _anonTypeNewTweaks = anonTypeNewTweaks;
            _grouping = grouping;
            _totalSummary = totalSummary;
            _groupSummary = groupSummary;
        }

#if DEBUG
        public RemoteGroupExpressionCompiler(Type itemType, IEnumerable<GroupingInfo> grouping, IEnumerable<SummaryInfo> totalSummary, IEnumerable<SummaryInfo> groupSummary)
            : this(itemType, false, false, null, grouping, totalSummary, groupSummary) {
        }
#endif

        public Expression Compile(Expression target) {
            // 注意：字符串类型字段的分组会自动禁用远程分组，改为使用内存分组（GroupHelper）
            // 所以这里不需要处理字符串分割逻辑

            // 使用原始类型
            var currentItemType = ItemType;
            var groupByParam = Expression.Parameter(currentItemType, "obj");
            var groupKeyExprList = new List<Expression>();
            var descendingList = new List<bool>();

            if(_grouping != null) {
                foreach(var i in _grouping) {
                    Expression selectorExpr;
                    if(String.IsNullOrEmpty(i.GroupInterval)) {
                        selectorExpr = CompileAccessorExpression(groupByParam, i.Selector, liftToNullable: true);
                    } else {
                        selectorExpr = CompileGroupInterval(groupByParam, i.Selector, i.GroupInterval);
                    }

                    groupKeyExprList.Add(selectorExpr);
                    descendingList.Add(i.Desc);
                }
            }

            var groupKeyTypeFacade = new AnonTypeFacade(groupKeyExprList);
            var groupKeyLambda = Expression.Lambda(groupKeyTypeFacade.CreateNewExpression(_anonTypeNewTweaks), groupByParam);
            var groupingType = typeof(IGrouping<,>).MakeGenericType(groupKeyLambda.ReturnType, currentItemType);

            target = Expression.Call(typeof(Queryable), nameof(Queryable.GroupBy), new[] { currentItemType, groupKeyLambda.ReturnType }, target, Expression.Quote(groupKeyLambda));

            for(var i = 0; i < groupKeyExprList.Count; i++) {
                var orderParam = Expression.Parameter(groupingType, "g");
                var orderAccessor = groupKeyTypeFacade.CreateMemberAccessor(
                    Expression.Property(orderParam, "Key"),
                    i
                );

                target = Expression.Call(
                    typeof(Queryable),
                    Utils.GetSortMethod(i == 0, descendingList[i]),
                    new[] { groupingType, orderAccessor.Type },
                    target,
                    Expression.Quote(Expression.Lambda(orderAccessor, orderParam))
                );
            }

            return MakeAggregatingProjection(target, groupingType, groupKeyTypeFacade);
        }

        Expression MakeAggregatingProjection(Expression target, Type groupingType, AnonTypeFacade groupKeyTypeFacade) {
            var param = Expression.Parameter(groupingType, "g");
            var groupCount = groupKeyTypeFacade.MemberCount;
            var currentItemType = ItemType;

            var projectionExprList = new List<Expression> {
                Expression.Call(typeof(Enumerable), nameof(Enumerable.Count), new[] { currentItemType }, param)
            };

            for(var i = 0; i < groupCount; i++)
                projectionExprList.Add(groupKeyTypeFacade.CreateMemberAccessor(Expression.Property(param, "Key"), i));

            projectionExprList.AddRange(MakeAggregates(param, _totalSummary));

            if(groupCount > 0)
                projectionExprList.AddRange(MakeAggregates(param, _groupSummary));

            var projectionTypeFacade = new AnonTypeFacade(projectionExprList);
            var projectionLambda = Expression.Lambda(projectionTypeFacade.CreateNewExpression(_anonTypeNewTweaks), param);

            return Expression.Call(typeof(Queryable), nameof(Queryable.Select), new[] { param.Type, projectionLambda.ReturnType }, target, Expression.Quote(projectionLambda));
        }

        IEnumerable<Expression> MakeAggregates(Expression aggregateTarget, IEnumerable<SummaryInfo> summary) {
            foreach(var s in TransformSummary(summary)) {
                yield return MakeAggregate(aggregateTarget, s);
            }
        }

        Expression MakeAggregate(Expression aggregateTarget, SummaryInfo s) {
            // 获取分组中的元素类型
            var groupingType = aggregateTarget.Type;
            var elementType = groupingType.GetGenericArguments().Last();
            var itemParam = Expression.Parameter(elementType, "obj");

            // 构建字段访问表达式
            var selectorExpr = CompileAccessorExpression(itemParam, s.Selector, liftToNullable: true);
            var selectorType = selectorExpr.Type;

            var callType = typeof(Enumerable);
            var isCountNotNull = s.SummaryType == AggregateName.COUNT_NOT_NULL;

            if(isCountNotNull && Utils.CanAssignNull(selectorType)) {
                return Expression.Call(
                    callType,
                    nameof(Enumerable.Sum),
                    Type.EmptyTypes,
                    Expression.Call(
                        typeof(Enumerable),
                        nameof(Enumerable.Select),
                        new[] { elementType, typeof(int) },
                        aggregateTarget,
                        Expression.Lambda(
                            Expression.Condition(
                                Expression.NotEqual(selectorExpr, Expression.Constant(null, selectorType)),
                                Expression.Constant(1),
                                Expression.Constant(0)
                            ),
                            itemParam
                        )
                    )
                );
            } else {
                var callMethod = GetPreAggregateMethodName(s.SummaryType);
                var callMethodTypeParams = new List<Type> { elementType };
                var callArgs = new List<Expression> { aggregateTarget };

                try {
                    if(s.SummaryType == AggregateName.MIN || s.SummaryType == AggregateName.MAX) {
                        if(!IsWellKnownAggregateDataType(selectorType))
                            callMethodTypeParams.Add(selectorType);
                    } else if(s.SummaryType == AggregateName.SUM) {
                        if(DynamicBindingHelper.ShouldUseDynamicBinding(ItemType)) {
                            callType = typeof(DynamicSum);
                            callMethod = nameof(DynamicSum.Calculate);
                        } else {
                            selectorExpr = ConvertSumSelector(selectorExpr);
                        }
                    }

                    if(!isCountNotNull)
                        callArgs.Add(Expression.Lambda(selectorExpr, itemParam));

                    return Expression.Call(callType, callMethod, callMethodTypeParams.ToArray(), callArgs.ToArray());
                } catch(Exception x) {
                    var message = $"Failed to translate the '{s.SummaryType}' aggregate for the '{s.Selector}' member ({selectorExpr.Type}). See InnerException for details.";
                    throw new Exception(message, x);
                }
            }
        }

        static bool IsWellKnownAggregateDataType(Type type) {
            type = Utils.StripNullableType(type);
            return type == typeof(decimal)
                || type == typeof(double)
                || type == typeof(float)
                || type == typeof(int)
                || type == typeof(long);
        }

        Expression ConvertSumSelector(Expression expr) {
            var type = expr.Type;
            var nullable = Utils.IsNullable(type);

            if(nullable)
                type = Utils.StripNullableType(type);

            var sumType = GetSumType(type, _expandSumType);
            if(sumType == type)
                return expr;

            if(nullable)
                sumType = Utils.MakeNullable(sumType);

            return Expression.Convert(expr, sumType);
        }

        internal static Type GetSumType(Type type, bool expand) {
            if(type == typeof(decimal) || type == typeof(double) || type == typeof(long))
                return type;

            if(type == typeof(int) || type == typeof(byte) || type == typeof(short) || type == typeof(sbyte) || type == typeof(ushort))
                return expand ? typeof(long) : typeof(int);

            if(type == typeof(float))
                return expand ? typeof(double) : typeof(float);

            if(type == typeof(uint))
                return typeof(long);

            return typeof(decimal);
        }

        static string GetPreAggregateMethodName(string summaryType) {
            switch(summaryType) {
                case AggregateName.MIN:
                    return nameof(Enumerable.Min);
                case AggregateName.MAX:
                    return nameof(Enumerable.Max);
                case AggregateName.SUM:
                    return nameof(Enumerable.Sum);
                case AggregateName.COUNT_NOT_NULL:
                    return nameof(Enumerable.Count);
            }

            if(CustomAggregators.IsRegistered(summaryType)) {
                var message = $"The custom aggregate '{summaryType}' cannot be translated to a LINQ expression."
                    + $" Set {nameof(DataSourceLoadOptionsBase)}.{nameof(DataSourceLoadOptionsBase.RemoteGrouping)} to False to enable in-memory aggregate calculation.";
                throw new InvalidOperationException(message);
            }

            throw new NotSupportedException();
        }

        /// <summary>
        /// 编译展开逗号分隔字符串的表达式
        /// 将每个对象根据指定字段的逗号分隔值展开成多行
        /// 返回 SelectMany 表达式，结果类型是匿名类型 { OriginalItem, TagValue }
        /// 注意：由于字符串分割操作无法在数据库端执行，需要先将数据加载到内存
        /// </summary>
        Expression CompileSplitExpand(Expression target, int splitGroupIndex) {
            var groupingInfo = _grouping.ElementAt(splitGroupIndex);
            var selector = groupingInfo.Selector;

            // 先将数据加载到内存（使用 AsEnumerable），因为字符串分割操作无法在数据库端执行
            // AsEnumerable 是 Enumerable 类的扩展方法，用于将 IQueryable 转换为 IEnumerable
            var asEnumerableMethod = typeof(Enumerable).GetMethods()
                .First(m => m.Name == "AsEnumerable" && m.GetParameters().Length == 1)
                .MakeGenericMethod(ItemType);

            var enumerableTarget = Expression.Call(asEnumerableMethod, target);

            // 获取字段访问表达式
            var itemParam = Expression.Parameter(ItemType, "obj");
            var fieldExpr = CompileAccessorExpression(itemParam, selector, liftToNullable: false);

            // 确保字段是字符串类型
            var fieldType = Utils.StripNullableType(fieldExpr.Type);
            if(fieldType != typeof(string)) {
                throw new InvalidOperationException($"Split grouping can only be applied to string fields. Field '{selector}' is of type '{fieldExpr.Type}'.");
            }

            // 处理可空字符串：如果为null，返回空字符串
            Expression stringExpr = fieldExpr;
            if(Utils.CanAssignNull(fieldExpr.Type)) {
                stringExpr = Expression.Condition(
                    Expression.Equal(fieldExpr, Expression.Constant(null, fieldExpr.Type)),
                    Expression.Constant("", typeof(string)),
                    Expression.Convert(fieldExpr, typeof(string))
                );
            }

            // 调用 Split(',') 方法分割字符串
            var splitMethod = typeof(string).GetMethod(nameof(string.Split), new[] { typeof(char[]) });
            var splitExpr = Expression.Call(
                stringExpr,
                splitMethod,
                Expression.Constant(new[] { ',' }, typeof(char[]))
            );

            // 过滤空字符串和空白字符串
            var splitItemParam = Expression.Parameter(typeof(string), "tag");
            var trimmedExpr = Expression.Call(splitItemParam, typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes));
            var notEmptyExpr = Expression.NotEqual(trimmedExpr, Expression.Constant(""));

            var whereLambda = Expression.Lambda(notEmptyExpr, splitItemParam);
            var whereMethod = typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.Where) && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(string));

            var filteredSplitExpr = Expression.Call(
                whereMethod,
                splitExpr,
                whereLambda
            );

            // 使用 SelectMany 展开：每个原始对象根据分割后的标签数组展开成多行
            // SelectMany(item => item.Tags.Split(',').Where(tag => tag.Trim() != "").Select(tag => new { Item0 = item, Item1 = tag.Trim() }))
            var tagParam = Expression.Parameter(typeof(string), "tag");
            var trimmedTagExpr = Expression.Call(tagParam, typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes));

            // 创建匿名类型：包含原始对象（Item0）和标签值（Item1）
            var anonTypeFacade = new AnonTypeFacade(new Expression[] { itemParam, trimmedTagExpr });
            var anonTypeNewExpr = anonTypeFacade.CreateNewExpression(_anonTypeNewTweaks);
            var selectLambda = Expression.Lambda(anonTypeNewExpr, tagParam);

            // SelectMany 的 collectionSelector：item => filteredSplit.Select(tag => new { Item0 = item, Item1 = tag.Trim() })
            // 由于字符串分割操作需要在内存中执行（数据库不支持），我们使用 Enumerable.Select
            // SelectMany 需要 collectionSelector 返回 IEnumerable<T>，而不是 IQueryable<T>
            var selectMethod = typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.Select) && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(string), anonTypeNewExpr.Type);

            var collectionSelectorBody = Expression.Call(
                selectMethod,
                filteredSplitExpr,
                selectLambda  // Enumerable.Select 不需要 Quote，因为它不是表达式树
            );
            var collectionSelectorLambda = Expression.Lambda(collectionSelectorBody, itemParam);

            // 使用 Enumerable.SelectMany（因为数据已经在内存中）
            var selectManyMethod = typeof(Enumerable).GetMethods()
                .First(m => m.Name == nameof(Enumerable.SelectMany) && m.GetParameters().Length == 2)
                .MakeGenericMethod(ItemType, anonTypeNewExpr.Type);

            var selectManyResult = Expression.Call(
                selectManyMethod,
                enumerableTarget,
                collectionSelectorLambda
            );

            // 将结果转换回 IQueryable（使用 AsQueryable）
            var asQueryableMethod = typeof(Queryable).GetMethods()
                .First(m => m.Name == nameof(Queryable.AsQueryable) && m.GetParameters().Length == 1)
                .MakeGenericMethod(anonTypeNewExpr.Type);

            return Expression.Call(asQueryableMethod, selectManyResult);
        }

        Expression CompileGroupInterval(Expression target, string selector, string intervalString) {
            if(Char.IsDigit(intervalString[0]))
                return CompileNumericGroupInterval(target, selector, intervalString);

            return CompileDateGroupInterval(target, selector, intervalString);
        }

        Expression CompileNumericGroupInterval(Expression target, string selector, string intervalString) {
            return CompileAccessorExpression(
                target,
                selector,
                progression => {
                    var lastIndex = progression.Count - 1;
                    var last = progression[lastIndex];

                    var intervalExpr = Expression.Constant(
                        Utils.ConvertClientValue(intervalString, last.Type),
                        last.Type
                    );

                    progression[lastIndex] = Expression.MakeBinary(
                        ExpressionType.Subtract,
                        last,
                        Expression.MakeBinary(ExpressionType.Modulo, last, intervalExpr)
                    );
                },
                true
            );
        }

        Expression CompileDateGroupInterval(Expression target, string selector, string intervalString) {
            return CompileAccessorExpression(
                target,
                selector + "." + (intervalString == "quarter" ? "month" : intervalString),
                progression => {
                    var lastIndex = progression.Count - 1;
                    var last = progression[lastIndex];

                    if(intervalString == "quarter") {
                        progression[lastIndex] = Expression.MakeBinary(
                            ExpressionType.Divide,
                            Expression.MakeBinary(ExpressionType.Add, last, Expression.Constant(2)),
                            Expression.Constant(3)
                        );
                    } else if(intervalString == "dayOfWeek") {
                        var hasNullable = progression.Any(i => Utils.CanAssignNull(i.Type));
                        progression[lastIndex] = Expression.Convert(last, hasNullable ? typeof(int?) : typeof(int));
                    }
                },
                true
            );
        }

        static IEnumerable<SummaryInfo> TransformSummary(IEnumerable<SummaryInfo> source) {
            if(source == null)
                yield break;

            foreach(var i in source) {
                if(i.SummaryType == AggregateName.COUNT)
                    continue;
                if(i.SummaryType == AggregateName.AVG) {
                    yield return new SummaryInfo { Selector = i.Selector, SummaryType = AggregateName.SUM };
                    yield return new SummaryInfo { Selector = i.Selector, SummaryType = AggregateName.COUNT_NOT_NULL };
                } else {
                    yield return i;
                }
            }
        }

    }

}

