// <copyright file="QueryModelBuilder.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Globalization;
using System.Reflection;
using System.Text;
using LeadCMS.Data;
using LeadCMS.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeadCMS.Infrastructure
{
#pragma warning disable SA1649, SA1402, SA1516, SA1201 // Multiple types in file is acceptable here

    /// <summary>
    /// Represents a property path that can contain one or more properties for nested access.
    /// Property names are matched case-insensitively (e.g., "account.name", "Account.Name", and "ACCOUNT.NAME" are equivalent).
    /// </summary>
    public class PropertyPath
    {
        public List<PropertyInfo> Properties { get; set; } = new List<PropertyInfo>();

        public bool ContainsCollectionNavigation { get; set; }

        public PropertyInfo LeafProperty => Properties.Last();

        public bool IsNested => Properties.Count > 1;

        public PropertyPath(PropertyInfo property)
        {
            Properties.Add(property);
        }

        public PropertyPath(List<PropertyInfo> properties)
        {
            Properties = properties;
        }

        public PropertyPath(List<PropertyInfo> properties, bool containsCollectionNavigation)
        {
            Properties = properties;
            ContainsCollectionNavigation = containsCollectionNavigation;
        }
    }

    public class QueryModelBuilder<T>
        where T : BaseEntityWithId
    {
        private readonly PgDbContext dbContext;

        /// <summary>
        /// Gets a value indicating whether where filters on <typeparamref name="T"/> may go through collection navigations, e.g. "contacts.id".
        /// </summary>
        internal static bool AllowsCollectionNavigationFilters => typeof(T) == typeof(Contact) || typeof(T) == typeof(Deal);

        private static PropertyPath ParsePropertyPath(string propertyName, QueryCommand cmd, bool allowCollectionNavigations)
        {
            var parts = propertyName.Split('.');
            var properties = new List<PropertyInfo>();
            Type currentType = typeof(T);
            var containsCollectionNavigation = false;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var typeProperties = currentType.GetProperties();
                var property = typeProperties.FirstOrDefault(p => p.Name.ToLowerInvariant() == part.ToLowerInvariant());

                if (property == null)
                {
                    throw new QueryException(cmd.Source, $"No such property '{part}' on type '{currentType.Name}'");
                }

                properties.Add(property);

                if (i >= parts.Length - 1)
                {
                    continue;
                }

                if (TryGetCollectionElementType(property.PropertyType, out var elementType))
                {
                    if (!allowCollectionNavigations)
                    {
                        throw new QueryException(cmd.Source, $"Collection navigation '{property.Name}' is not supported in this query.");
                    }

                    containsCollectionNavigation = true;
                    currentType = elementType;
                    continue;
                }

                currentType = property.PropertyType;
                if (Nullable.GetUnderlyingType(currentType) != null)
                {
                    currentType = Nullable.GetUnderlyingType(currentType)!;
                }
            }

            return new PropertyPath(properties, containsCollectionNavigation);
        }

        private static bool TryGetCollectionElementType(Type propertyType, out Type elementType)
        {
            elementType = null!;

            if (propertyType == typeof(string))
            {
                return false;
            }

            if (propertyType.IsArray)
            {
                elementType = propertyType.GetElementType()!;
                return true;
            }

            if (propertyType.IsGenericType && propertyType.GetGenericArguments().Length == 1)
            {
                var genericDefinition = propertyType.GetGenericTypeDefinition();
                if (genericDefinition == typeof(ICollection<>)
                    || genericDefinition == typeof(IEnumerable<>)
                    || genericDefinition == typeof(IList<>)
                    || genericDefinition == typeof(List<>))
                {
                    elementType = propertyType.GetGenericArguments()[0];
                    return true;
                }
            }

            var enumerableInterface = propertyType
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            if (enumerableInterface != null)
            {
                elementType = enumerableInterface.GetGenericArguments()[0];
                return true;
            }

            return false;
        }

        public QueryModelBuilder(List<QueryCommand> commands, int maxLimitSize, PgDbContext dbContext)
        {
            this.dbContext = dbContext;
            IncludeData = ParseIncludeCommands(commands);
            Limit = ParseLimitCommands(commands, maxLimitSize);
            Skip = ParseSkipCommands(commands);
            SearchData = ParseSearchCommands(commands);
            WhereData = ParseWhereCommands(commands);
            OrderData = ParseOrderCommands(commands);
            SelectData = ParseSelectCommands(commands);
            Ids = ParseIdsCommands(commands);

            // Automatically include relationships for nested properties in SelectData
            AddAutomaticIncludesForNestedSelects();
        }

        public int Limit { get; set; } = 0;

        public int Skip { get; set; } = 0;

        public List<string> SearchData { get; set; }

        public List<WhereCommandData> WhereData { get; set; }

        public List<OrderCommandData> OrderData { get; set; }

        public List<PropertyInfo> IncludeData { get; set; }

        public SelectCommandData SelectData { get; set; }

        public List<int> Ids { get; private set; }

        private static PropertyInfo ParseProperty(string? propertyName, QueryCommand cmd)
        {
            if (propertyName == null || string.IsNullOrWhiteSpace(propertyName))
            {
                throw new QueryException(cmd.Source, "Property field not found");
            }

            var typeProperties = typeof(T).GetProperties();
            var property = typeProperties.FirstOrDefault(p => p.Name.ToLowerInvariant() == propertyName.ToLowerInvariant());

            if (property == null)
            {
                throw new QueryException(cmd.Source, $"No such property '{propertyName}'");
            }

            return property;
        }

        private SelectCommandData ParseSelectCommands(List<QueryCommand> commands)
        {
            var trueFields = new List<PropertyInfo>();
            var falseFields = new List<PropertyInfo>();
            var nestedProperties = new List<PropertyPath>();

            var validFieldCommands = commands.Where(c => c.Type == FilterType.Fields);
            foreach (var command in validFieldCommands)
            {
                var parseSuccess = bool.TryParse(command.Value, out var value);
                if (parseSuccess)
                {
                    var propertyName = command.Props.ElementAtOrDefault(0);
                    if (string.IsNullOrWhiteSpace(propertyName))
                    {
                        continue;
                    }

                    // Handle nested properties (e.g., contact.fullName)
                    if (propertyName.Contains('.'))
                    {
                        if (value)
                        {
                            try
                            {
                                var propertyPath = ParseNestedPropertyPath(propertyName, command);
                                nestedProperties.Add(propertyPath);
                            }
                            catch (QueryException)
                            {
                                // If nested property doesn't exist, skip it
                                continue;
                            }
                        }

                        continue;
                    }

                    PropertyInfo? pi;
                    try
                    {
                        pi = command.ParseProperty<T>();
                    }
                    catch (QueryException)
                    {
                        // Unknown field name — silently skip (e.g. virtual UI-only columns)
                        continue;
                    }

                    if (value)
                    {
                        trueFields.Add(pi);
                    }
                    else
                    {
                        falseFields.Add(pi);
                    }
                }
                else
                {
                    throw new QueryException(command.Source, "Incorrect argument in field command");
                }
            }

            if (trueFields.Count == 0 && falseFields.Count == 0 && nestedProperties.Count == 0)
            {
                return new SelectCommandData
                {
                    SelectedProperties = new List<PropertyInfo>(),
                    SelectedNestedProperties = new List<PropertyPath>(),
                    IsSelect = false,
                };
            }

            if (trueFields.Count == 0)
            {
                trueFields = typeof(T).GetProperties().ToList();
            }

            var selectedProps = trueFields.Where(p => !falseFields.Contains(p)).ToList();

            return new SelectCommandData
            {
                SelectedProperties = selectedProps,
                SelectedNestedProperties = nestedProperties,
                IsSelect = true,
            };
        }

        private List<OrderCommandData> ParseOrderCommands(List<QueryCommand> commands)
        {
            var result = new List<OrderCommandData>();

            var orderCommands = commands.Where(c => c.Type == FilterType.Order).ToArray();
            if (orderCommands.Length > 1)
            {
                Array.ForEach(orderCommands, c =>
                {
                    if (c.Props.ElementAtOrDefault(0) == null || string.IsNullOrEmpty(c.Props[0]))
                    {
                        throw new QueryException(c.Source, "Failed to parse. Check syntax.");
                    }
                });

                orderCommands = orderCommands.OrderBy(c => c.Props[0]).ToArray();
            }

            foreach (var oc in orderCommands)
            {
                result.Add(new OrderCommandData(oc));
            }

            if (result.Count == 0)
            {
                result.Add(new OrderCommandData("Id"));
            }

            return result;
        }

        private List<string> ParseSearchCommands(List<QueryCommand> commands)
        {
            var result = new List<string>();
            foreach (var cmdValue in commands.Where(c => c.Type == FilterType.Search && c.Value.Length > 0).Select(cmd => cmd.Value))
            {
                result.Add(cmdValue);
            }

            return result;
        }

        private List<WhereCommandData> ParseWhereCommands(List<QueryCommand> commands)
        {
            var result = new List<WhereCommandData>();
            var orResult = new List<WhereUnitData>();
            var errorList = new List<QueryException>();

            foreach (var cmd in commands.Where(c => c.Type == FilterType.Where).ToArray())
            {
                try
                {
                    var unitData = new WhereUnitData(cmd);

                    if (unitData.OrOperation)
                    {
                        orResult.Add(unitData);
                    }
                    else
                    {
                        var tempResult = new List<WhereUnitData>();
                        tempResult.Add(unitData);
                        result.Add(new WhereCommandData
                        {
                            Data = tempResult,
                            OrOperation = false,
                        });
                    }
                }
                catch (QueryException ex)
                {
                    if (!cmd.IsImplicit)
                    {
                        errorList.Add(ex);
                    }
                }
            }

            if (errorList.Any())
            {
                throw new QueryException(errorList);
            }

            if (orResult.Count > 0)
            {
                result.Add(new WhereCommandData
                {
                    Data = orResult,
                    OrOperation = true,
                });
            }

            return result;
        }

        private int ParseLimitCommands(List<QueryCommand> commands, int maxLimitSize)
        {
            var res = maxLimitSize;
            var limitCommand = commands.FirstOrDefault(c => c.Type == FilterType.Limit);
            if (limitCommand != null)
            {
                if (!int.TryParse(limitCommand.Value, out res))
                {
                    throw new QueryException(limitCommand.Source, $"Failed to parse number '{limitCommand.Value}'");
                }

                if (res <= 0)
                {
                    throw new QueryException(limitCommand.Source, $"Invalid limit size. (Maximum {maxLimitSize})");
                }

                if (res > maxLimitSize)
                {
                    throw new QueryException(limitCommand.Source, $"Max limit size exceeded. (Maximum {maxLimitSize})");
                }
            }

            return res;
        }

        private int ParseSkipCommands(List<QueryCommand> commands)
        {
            var res = 0;
            var skipCommand = commands.FirstOrDefault(c => c.Type == FilterType.Skip);
            if (skipCommand != null)
            {
                if (!int.TryParse(skipCommand.Value, out res))
                {
                    throw new QueryException(skipCommand.Source, $"Failed to parse number '{skipCommand.Value}'");
                }

                if (res < 0)
                {
                    throw new QueryException(skipCommand.Source, "Invalid skip size");
                }
            }

            return res;
        }

        private List<PropertyInfo> ParseIncludeCommands(List<QueryCommand> commands)
        {
            var result = new List<PropertyInfo>();
            foreach (var cmd in commands.Where(c => c.Type == FilterType.Include).ToArray())
            {
                var property = ParseProperty(cmd.Value, cmd);
                var isCollection = property.PropertyType.IsGenericType && typeof(ICollection<>).MakeGenericType(property.PropertyType.GetGenericArguments()).IsAssignableFrom(property.PropertyType);

                if (dbContext.Model.FindEntityType(property.PropertyType) != null
                    || (isCollection && dbContext.Model.FindEntityType(property.PropertyType.GetGenericArguments().Single()) != null))
                {
                    result.Add(property);
                }
                else
                {
                    throw new QueryException(cmd.Source, "Invalid property in include query");
                }
            }

            return result;
        }

        private List<int> ParseIdsCommands(List<QueryCommand> commands)
        {
            var cmd = commands.FirstOrDefault(c => c.Type == FilterType.Ids);
            if (cmd == null || string.IsNullOrWhiteSpace(cmd.Value))
            {
                return new List<int>();
            }

            return cmd.Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id, out var val) ? val : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
        }

        private PropertyPath ParseNestedPropertyPath(string propertyName, QueryCommand cmd)
        {
            return ParsePropertyPath(propertyName, cmd, allowCollectionNavigations: false);
        }

        private void AddAutomaticIncludesForNestedSelects()
        {
            if (SelectData.SelectedNestedProperties.Count == 0)
            {
                return;
            }

            // Extract relationship properties that need to be included
            foreach (var nestedProp in SelectData.SelectedNestedProperties)
            {
                if (nestedProp.IsNested)
                {
                    // Get the first property in the path (the relationship)
                    var relationshipProperty = nestedProp.Properties[0];

                    // Check if it's already included
                    if (!IncludeData.Exists(p => p.Name == relationshipProperty.Name))
                    {
                        // Verify it's a valid relationship
                        var isCollection = relationshipProperty.PropertyType.IsGenericType &&
                                          typeof(ICollection<>).MakeGenericType(relationshipProperty.PropertyType.GetGenericArguments())
                                              .IsAssignableFrom(relationshipProperty.PropertyType);

                        if (dbContext.Model.FindEntityType(relationshipProperty.PropertyType) != null ||
                            (isCollection && dbContext.Model.FindEntityType(relationshipProperty.PropertyType.GetGenericArguments().Single()) != null))
                        {
                            IncludeData.Add(relationshipProperty);
                        }
                    }
                }
            }
        }

        public sealed class SelectCommandData
        {
            public List<PropertyInfo> SelectedProperties { get; set; } = new List<PropertyInfo>();

            public List<PropertyPath> SelectedNestedProperties { get; set; } = new List<PropertyPath>();

            public bool IsSelect { get; set; } = false;
        }

        public sealed class OrderCommandData
        {
            public OrderCommandData(string propertyName)
            {
                PropertyPath = InitPropertyPath(propertyName);
                Ascending = true;
            }

            public OrderCommandData(QueryCommand cmd)
            {
                var valueProps = cmd.Value.Split(' ');

                var propertyName = string.Empty;
                Ascending = true;

                switch (valueProps.Length)
                {
                    case 0:
                        propertyName = "Id";
                        break;
                    case 1:
                        propertyName = valueProps.First();
                        break;
                    case 2:
                        propertyName = valueProps.First();
                        Ascending = valueProps.ElementAt(1).ToLowerInvariant() != "desc";
                        break;
                    default:
                        throw new QueryException(cmd.Source, "Failed to parse. Check syntax.");
                }

                PropertyPath = InitPropertyPath(propertyName);
            }

            public PropertyPath PropertyPath { get; set; }

            public bool Ascending { get; set; }

            private PropertyPath InitPropertyPath(string propertyName)
            {
                return ParsePropertyPath(propertyName, new QueryCommand { Source = propertyName }, allowCollectionNavigations: false);
            }
        }

        public sealed class WhereCommandData
        {
            public IList<WhereUnitData> Data { get; set; } = new List<WhereUnitData>();

            public bool OrOperation { get; set; } = false;
        }

        public sealed class WhereUnitData
        {
            public readonly string StringValue;
            public readonly PropertyPath PropertyPath;
            public readonly WOperand Operation;
            public readonly bool OrOperation;
            public readonly QueryCommand Cmd;

#pragma warning disable S1133 // Obsolete member kept for backward compatibility
            [Obsolete("Use PropertyPath instead")]
            public PropertyInfo Property => PropertyPath.LeafProperty;
#pragma warning restore S1133

            public WhereUnitData(QueryCommand cmd)
            {
                Cmd = cmd;
                StringValue = cmd.Value;

                var fProp = cmd.Props.ElementAtOrDefault(0);

                if (fProp == null || string.IsNullOrWhiteSpace(fProp))
                {
                    throw new QueryException(cmd.Source, "Property field not found");
                }

                var indexOffset = 0;
                if (fProp == "or")
                {
                    if (cmd.Props.Length == 1)
                    {
                        throw new QueryException(cmd.Source, "Property fields not found");
                    }

                    indexOffset = 1;
                    OrOperation = true;
                }
                else
                {
                    OrOperation = false;
                }

                var propertyName = cmd.Props.ElementAtOrDefault(indexOffset);
                var rawOperation = cmd.Props.ElementAtOrDefault(indexOffset + 1);
                PropertyPath = ResolvePropertyPath(propertyName, cmd);
                Operation = ParseOperation(rawOperation, cmd);
            }

            private PropertyPath ResolvePropertyPath(string? propertyName, QueryCommand cmd)
            {
                if (propertyName == null || string.IsNullOrWhiteSpace(propertyName))
                {
                    throw new QueryException(cmd.Source, "Property field not found");
                }

                return QueryModelBuilder<T>.ParsePropertyPath(
                    propertyName,
                    cmd,
                    allowCollectionNavigations: AllowsCollectionNavigationFilters);
            }

            public enum ContainsType
            {
                MatchAll,
                Substring,
            }

            public List<Tuple<ContainsType, string>> ParseContainValue(string value)
            {
                if (value.Length == 0)
                {
                    throw new QueryException(Cmd.Source, "Empty contain query argument");
                }

                var result = new List<Tuple<ContainsType, string>>();
                var sb = new StringBuilder();

                for (var i = 0; i < value.Length; ++i)
                {
                    if (value[i] == '*')
                    {
                        if (i == 0 || value[i - 1] != '\\')
                        {
                            if (sb.Length > 0)
                            {
                                result.Add(new Tuple<ContainsType, string>(ContainsType.Substring, sb.ToString()));
                            }

                            result.Add(new Tuple<ContainsType, string>(ContainsType.MatchAll, string.Empty));
                            sb.Clear();
                        }
                        else
                        {
                            sb.Remove(sb.Length - 1, 1);
                            sb.Append('*');
                        }
                    }
                    else
                    {
                        sb.Append(value[i]);
                    }
                }

                if (sb.Length > 0)
                {
                    result.Add(new Tuple<ContainsType, string>(ContainsType.Substring, sb.ToString()));
                }

                return result;
            }

            public List<object?> ParseValues(IEnumerable<string> input)
            {
                var result = new List<object?>();

                foreach (var sv in input)
                {
                    if ((sv == "null" || sv == string.Empty) && GetUnderlyingPropertyType() != typeof(string))
                    {
                        result.Add(null);
                    }
                    else
                    {
                        if (GetUnderlyingPropertyType() == typeof(DateTime) && DateTime.TryParse(sv, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var date))
                        {
                            result.Add(date);
                        }
                        else if (GetUnderlyingPropertyType() == typeof(decimal) && decimal.TryParse(sv, out var decimalValue))
                        {
                            result.Add(decimalValue);
                        }
                        else if (GetUnderlyingPropertyType() == typeof(double) && double.TryParse(sv, out var doubleValue))
                        {
                            result.Add(doubleValue);
                        }
                        else if (GetUnderlyingPropertyType() == typeof(int) && int.TryParse(sv, out var intValue))
                        {
                            result.Add(intValue);
                        }
                        else if (GetUnderlyingPropertyType() == typeof(bool) && bool.TryParse(sv, out var boolValue))
                        {
                            result.Add(boolValue);
                        }
                        else if (GetUnderlyingPropertyType().IsEnum && !int.TryParse(sv, out _) && Enum.TryParse(GetUnderlyingPropertyType(), sv, true, out var enumValue))
                        {
                            result.Add(enumValue);
                        }
                        else if (GetUnderlyingPropertyType() == typeof(string))
                        {
                            result.Add(sv);
                            if (sv == string.Empty)
                            {
                                result.Add(null);
                            }
                        }
                        else
                        {
                            throw new QueryException(Cmd.Source, "Property type and provided type value do not match");
                        }
                    }
                }

                return result;
            }

            public HashSet<string> ParseStringValues()
            {
                var result = new HashSet<string>();

                var sb = new StringBuilder();

                for (var i = 0; i < StringValue.Length; ++i)
                {
                    if (StringValue[i] == '|')
                    {
                        if (i == 0 || StringValue[i - 1] != '\\')
                        {
                            result.Add(sb.ToString());
                            sb.Clear();
                        }
                        else
                        {
                            sb.Remove(sb.Length - 1, 1);
                            sb.Append('|');
                        }
                    }
                    else
                    {
                        sb.Append(StringValue[i]);
                    }
                }

                result.Add(sb.ToString());

                return result;
            }

            public bool IsNullableProperty()
            {
                var context = new NullabilityInfoContext();
                var info = context.Create(PropertyPath.LeafProperty);
                return info.WriteState == NullabilityState.Nullable;
            }

            private Type GetUnderlyingPropertyType()
            {
                var nt = Nullable.GetUnderlyingType(PropertyPath.LeafProperty.PropertyType);
                return nt == null ? PropertyPath.LeafProperty.PropertyType : nt!;
            }

            private WOperand ParseOperation(string? rawOperation, QueryCommand cmd)
            {
                if (rawOperation == null)
                {
                    rawOperation = "eq";
                }

                if (string.IsNullOrEmpty(rawOperation))
                {
                    throw new QueryException(cmd.Source, "Empty operand");
                }

                if (!QueryCommand.OperandMappings.ContainsKey(rawOperation))
                {
                    throw new QueryException(cmd.Source, $"No such operand '{rawOperation}'");
                }

                return QueryCommand.OperandMappings.FirstOrDefault(m => m.Key == rawOperation).Value;
            }
        }
    }
}