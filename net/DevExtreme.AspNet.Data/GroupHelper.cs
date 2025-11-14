using DevExtreme.AspNet.Data.Helpers;
using DevExtreme.AspNet.Data.ResponseModel;

using System;
using System.Collections.Generic;
using System.Linq;

namespace DevExtreme.AspNet.Data {

    class GroupHelper<T> {
        readonly static object NULL_KEY = new object();

        IAccessor<T> _accessor;

        public GroupHelper(IAccessor<T> accessor) {
            _accessor = accessor;
        }

        public List<Group> Group(IEnumerable<T> data, IEnumerable<GroupingInfo> groupInfo) {
            var firstGroupInfo = groupInfo.First();

            // 自动检测：如果第一个分组字段是字符串类型，自动展开（按逗号分隔）
            // 直接检查字段类型，而不是通过读取值来判断
            bool shouldExpand = false;
            if(String.IsNullOrEmpty(firstGroupInfo.GroupInterval)) {
                try {
                    // 使用反射检查字段类型
                    var itemType = typeof(T);
                    var property = itemType.GetProperty(firstGroupInfo.Selector,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase);

                    if(property != null) {
                        var propertyType = property.PropertyType;
                        // 处理可空类型
                        if(propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                            propertyType = propertyType.GetGenericArguments()[0];
                        }
                        // 如果是字符串类型，就自动展开
                        if(propertyType == typeof(string)) {
                            shouldExpand = true;
                        }
                    }
                } catch {
                    // 如果无法访问字段，尝试通过读取值来判断
                    var firstItem = data.FirstOrDefault();
                    if(firstItem != null) {
                        try {
                            var memberValue = _accessor.Read(firstItem, firstGroupInfo.Selector);
                            if(memberValue is string) {
                                shouldExpand = true;
                            }
                        } catch {
                            // 忽略
                        }
                    }
                }
            }

            if(shouldExpand) {
                // 先展开数据：每个对象根据标签字段展开成多行
                var expandedData = ExpandSplitData(data, firstGroupInfo.Selector);
                // 然后按展开后的标签值分组
                var groups = GroupExpanded(expandedData, firstGroupInfo);

                // 如果有多个分组级别，继续递归分组
                if(groupInfo.Count() > 1) {
                    groups = groups
                        .Select(g => new Group {
                            key = g.key,
                            items = Group(g.items.Cast<T>(), groupInfo.Skip(1))
                        })
                        .ToList();
                }

                return groups;
            }

            var groups_normal = Group(data, firstGroupInfo);

            if(groupInfo.Count() > 1) {
                groups_normal = groups_normal
                    .Select(g => new Group {
                        key = g.key,
                        items = Group(g.items.Cast<T>(), groupInfo.Skip(1))
                    })
                    .ToList();
            }

            return groups_normal;
        }

        /// <summary>
        /// 展开逗号分隔字符串数据：将每个对象根据指定字段的逗号分隔值展开成多行
        /// 返回包含原始对象和标签值的元组列表
        /// </summary>
        IEnumerable<(T Original, string Tag)> ExpandSplitData(IEnumerable<T> data, string selector) {
            foreach(var item in data) {
                var memberValue = _accessor.Read(item, selector);
                if(memberValue == null)
                    continue;

                var tagString = memberValue.ToString();
                if(String.IsNullOrWhiteSpace(tagString))
                    continue;

                // 分割字符串并过滤空值
                var tags = tagString
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !String.IsNullOrEmpty(t));

                foreach(var tag in tags) {
                    yield return (item, tag);
                }
            }
        }

        /// <summary>
        /// 对展开后的数据进行分组
        /// </summary>
        List<Group> GroupExpanded(IEnumerable<(T Original, string Tag)> expandedData, GroupingInfo groupInfo) {
            var groupsIndex = new Dictionary<object, Group>();
            var groups = new List<Group>();

            foreach(var (original, tag) in expandedData) {
                var groupKey = tag; // 使用标签值作为分组键
                var groupIndexKey = groupKey ?? NULL_KEY;

                Group group;
                if(!groupsIndex.TryGetValue(groupIndexKey, out group)) {
                    var newGroup = new Group { key = groupKey };
                    groupsIndex.Add(groupIndexKey, group = newGroup);
                    groups.Add(newGroup);
                }

                if(group.items == null)
                    group.items = new List<T>();
                // 添加原始对象（注意：可能会有重复，但这是预期的，因为一个对象可能属于多个标签组）
                group.items.Add(original);
            }

            return groups;
        }


        List<Group> Group(IEnumerable<T> data, GroupingInfo groupInfo) {
            var groupsIndex = new Dictionary<object, Group>();
            var groups = new List<Group>();

            foreach(var item in data) {
                var groupKey = GetKey(item, groupInfo);
                var groupIndexKey = groupKey ?? NULL_KEY;

                Group group;
                if(!groupsIndex.TryGetValue(groupIndexKey, out group)) {
                    var newGroup = new Group { key = groupKey };
                    groupsIndex.Add(groupIndexKey, group = newGroup);
                    groups.Add(newGroup);
                }

                if(group.items == null)
                    group.items = new List<T>();
                group.items.Add(item);
            }

            return groups;
        }

        object GetKey(T obj, GroupingInfo groupInfo) {
            var memberValue = _accessor.Read(obj, groupInfo.Selector);

            var intervalString = groupInfo.GroupInterval;
            if(String.IsNullOrEmpty(intervalString) || memberValue == null)
                return memberValue;

            if(Char.IsDigit(intervalString[0])) {
                var number = Convert.ToDecimal(memberValue);
                var interval = Decimal.Parse(intervalString);
                return number - number % interval;
            }

            switch(intervalString) {
                case "year":
                    return ToDateTime(memberValue).Year;
                case "quarter":
                    return (ToDateTime(memberValue).Month + 2) / 3;
                case "month":
                    return ToDateTime(memberValue).Month;
                case "day":
                    return ToDateTime(memberValue).Day;
                case "dayOfWeek":
                    return (int)ToDateTime(memberValue).DayOfWeek;
                case "hour":
                    return ToDateTime(memberValue).Hour;
                case "minute":
                    return ToDateTime(memberValue).Minute;
                case "second":
                    return ToDateTime(memberValue).Second;
            }

            throw new NotSupportedException();
        }

        static DateTime ToDateTime(object value) {
            if(value is DateTimeOffset offset)
                return offset.DateTime;

#if NET6_0_OR_GREATER
            if(value is DateOnly date)
                return date.ToDateTime(TimeOnly.MinValue);
#endif

            return Convert.ToDateTime(value);
        }
    }

}
