// <copyright file="DBQueryProvider.cs" company="WavePoint Co. Ltd.">
// Licensed under the MIT license. See LICENSE file in the samples root for full license information.
// </copyright>

using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using LeadCMS.DataAnnotations;
using LeadCMS.Entities;
using LeadCMS.Helpers;
using LeadCMS.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace LeadCMS.Infrastructure
{
    public class DBQueryProvider<T> : IQueryProvider<T>
        where T : BaseEntityWithId
    {
        private readonly QueryModelBuilder<T> queryBuilder;

        public DBQueryProvider(IQueryable<T> query, QueryModelBuilder<T> queryBuilder)
        {
            BuiltQuery = query;
            this.queryBuilder = queryBuilder;
        }

        public IQueryable<T> BuiltQuery { get; private set; }

        public Array? DynamicResults { get; private set; }

        public int Skip => queryBuilder.Skip;

        public void SetBuiltQuery(IQueryable<T> query)
        {
            BuiltQuery = query;
        }

        public void ApplyFilters()
        {
            if (queryBuilder.Ids != null && queryBuilder.Ids.Count > 0)
            {
                BuiltQuery = BuiltQuery.Where(e => queryBuilder.Ids.Contains(e.Id));
            }

            AddWhereCommands();
            AddSearchCommands();
        }

        public void ApplyPaging()
        {
            AddSkipCommand();
            AddLimitCommand();
        }

        public async Task<QueryResult<T>> GetResult()
        {
            ApplyFilters();

            var totalCount = BuiltQuery.Count();
            IList<T>? records;

            ApplyIncludes();
            AddOrderCommands();
            ApplyPaging();
            if (queryBuilder.SelectData.IsSelect)
            {
                records = await GetSelectResult();
                var result = new QueryResult<T>(records, totalCount);
                result.DynamicResults = DynamicResults;
                return result;
            }
            else
            {
                records = await BuiltQuery.ToListAsync();
            }

            return new QueryResult<T>(records, totalCount, "DB");
        }

        /// <summary>
        /// Applies include commands parsed from the query string to <see cref="BuiltQuery"/>.
        /// Called automatically by <see cref="GetResult"/>, but can also be invoked
        /// directly when the caller needs the includes without executing the full query pipeline.
        /// </summary>
        public void ApplyIncludes()
        {
            foreach (var data in queryBuilder.IncludeData)
            {
                BuiltQuery = BuiltQuery.Include(data.Name);
            }
        }

        private static bool CanTranslateToString(Type propertyType)
        {
            // Only allow types that Entity Framework can successfully translate ToString() calls for
            var underlyingType = Nullable.GetUnderlyingType(propertyType);
            var typeToCheck = underlyingType ?? propertyType;

            // Allowed types that EF can translate
            return typeToCheck == typeof(int) ||
                   typeToCheck == typeof(long) ||
                   typeToCheck == typeof(short) ||
                   typeToCheck == typeof(byte) ||
                   typeToCheck == typeof(sbyte) ||
                   typeToCheck == typeof(uint) ||
                   typeToCheck == typeof(ulong) ||
                   typeToCheck == typeof(ushort) ||
                   typeToCheck == typeof(float) ||
                   typeToCheck == typeof(double) ||
                   typeToCheck == typeof(decimal) ||
                   typeToCheck == typeof(bool) ||
                   typeToCheck == typeof(char) ||
                   typeToCheck == typeof(Guid);
        }

        private static bool IsNavigationProperty(Type propertyType)
        {
            var typeToCheck = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return typeToCheck.IsClass
                && typeToCheck != typeof(string)
                && typeof(BaseEntityWithId).IsAssignableFrom(typeToCheck);
        }

        private static List<Expression> BuildNavigationSearchExpressions(
            ParameterExpression paramExpr,
            PropertyInfo navProp,
            string searchValue,
            MethodInfo containsMethod)
        {
            var results = new List<Expression>();
            var navAccess = Expression.Property(paramExpr, navProp.Name);
            var navNotNull = Expression.NotEqual(navAccess, Expression.Constant(null, navProp.PropertyType));
            var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
            var valueLower = Expression.Constant(searchValue.ToLower());

            var innerProps = navProp.PropertyType
                .GetProperties()
                .Where(p => p.IsDefined(typeof(SearchableAttribute), false) && p.PropertyType == typeof(string));

            foreach (var innerProp in innerProps)
            {
                var innerAccess = Expression.Property(navAccess, innerProp.Name);
                var innerNotNull = Expression.NotEqual(innerAccess, Expression.Constant(null, typeof(string)));
                var innerLower = Expression.Call(innerAccess, toLowerMethod);
                var containsCall = Expression.Call(innerLower, containsMethod, valueLower);

                // entity.Nav != null && entity.Nav.Prop != null && entity.Nav.Prop.ToLower().Contains(term)
                var combined = Expression.AndAlso(Expression.AndAlso(navNotNull, innerNotNull), containsCall);
                results.Add(combined);
            }

            return results;
        }

        private void AddOrderCommands()
        {
            if (queryBuilder.OrderData.Count == 0)
            {
                BuiltQuery = BuiltQuery.OrderBy(t => t.Id);
            }
            else
            {
                var moreThanOne = false;
                foreach (var orderCmd in queryBuilder.OrderData)
                {
                    var expressionParameter = Expression.Parameter(typeof(T));
                    var orderPropertyType = orderCmd.PropertyPath.LeafProperty.PropertyType;
                    Expression orderPropertyExpression;

                    // Add includes for nested properties
                    if (orderCmd.PropertyPath.IsNested)
                    {
                        // Build the include path
                        var includePath = string.Join(".", orderCmd.PropertyPath.Properties.Take(orderCmd.PropertyPath.Properties.Count - 1).Select(p => p.Name));
                        if (!string.IsNullOrEmpty(includePath))
                        {
                            BuiltQuery = BuiltQuery.Include(includePath);
                        }
                    }

                    // Special handling for updatedAt: use coalesce(updatedAt, createdAt)
                    if (string.Equals(orderCmd.PropertyPath.LeafProperty.Name, "UpdatedAt", StringComparison.OrdinalIgnoreCase) && !orderCmd.PropertyPath.IsNested)
                    {
                        var updatedAtProp = typeof(T).GetProperty("UpdatedAt");
                        var createdAtProp = typeof(T).GetProperty("CreatedAt");
                        if (updatedAtProp != null && createdAtProp != null)
                        {
                            var updatedAtExpr = Expression.Property(expressionParameter, updatedAtProp);
                            var createdAtExpr = Expression.Property(expressionParameter, createdAtProp);
                            // Ensure both are nullable for coalesce
                            Expression updatedAtNullable = updatedAtExpr.Type == typeof(DateTime)
                                ? Expression.Convert(updatedAtExpr, typeof(DateTime?))
                                : updatedAtExpr;
                            Expression createdAtNullable = createdAtExpr.Type == typeof(DateTime)
                                ? Expression.Convert(createdAtExpr, typeof(DateTime?))
                                : createdAtExpr;
                            // Coalesce: updatedAt ?? createdAt
                            orderPropertyExpression = Expression.Coalesce(updatedAtNullable, createdAtNullable);
                            orderPropertyType = typeof(DateTime?);
                        }
                        else
                        {
                            // Fallback to original property if not found
                            orderPropertyExpression = BuildNestedPropertyExpression(expressionParameter, orderCmd.PropertyPath);
                        }
                    }
                    else
                    {
                        orderPropertyExpression = BuildNestedPropertyExpression(expressionParameter, orderCmd.PropertyPath);
                    }

                    var orderDelegateType = typeof(Func<,>).MakeGenericType(typeof(T), orderPropertyType);
                    var orderLambda = Expression.Lambda(orderDelegateType, orderPropertyExpression, expressionParameter);
                    var methodName = string.Empty;

                    if (orderCmd.Ascending)
                    {
                        methodName = moreThanOne ? "ThenBy" : "OrderBy";
                    }
                    else
                    {
                        methodName = moreThanOne ? "ThenByDescending" : "OrderByDescending";
                    }

                    moreThanOne = true;

                    var orderMethod = typeof(Queryable).GetMethods().First(
                        m => m.Name == methodName &&
                        m.GetGenericArguments().Length == 2 &&
                        m.GetParameters().Length == 2).MakeGenericMethod(typeof(T), orderPropertyType);
                    BuiltQuery = (IOrderedQueryable<T>)orderMethod.Invoke(null, new object?[] { BuiltQuery, orderLambda })!;
                }
            }
        }

        private void AddSkipCommand()
        {
            if (queryBuilder.Skip > 0)
            {
                BuiltQuery = BuiltQuery.Skip(queryBuilder.Skip);
            }
        }

        private void AddLimitCommand()
        {
            if (queryBuilder.Limit > 0)
            {
                BuiltQuery = BuiltQuery.Take(queryBuilder.Limit);
            }
        }

        private void AddSearchCommands()
        {
            foreach (var cmdValue in queryBuilder.SearchData)
            {
                var props = typeof(T).GetProperties().Where(p => p.IsDefined(typeof(SearchableAttribute), false));

                Expression orExpression = Expression.Constant(false);
                var paramExpr = Expression.Parameter(typeof(T), "entity");
                var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) });

                foreach (var prop in props)
                {
                    if (prop != null)
                    {
                        // Check if this is a navigation property (entity class with its own [Searchable] fields)
                        if (IsNavigationProperty(prop.PropertyType))
                        {
                            var navSearchExpressions = BuildNavigationSearchExpressions(
                                paramExpr, prop, cmdValue, containsMethod!);
                            foreach (var navExpr in navSearchExpressions)
                            {
                                orExpression = Expression.Or(orExpression, navExpr);
                            }

                            continue;
                        }

                        var n = prop.Name;
                        var me = Expression.Property(paramExpr, n);
                        Expression containsExpression;

                        if (prop.PropertyType == typeof(string))
                        {
                            var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes);
                            var meLower = Expression.Call(me, toLowerMethod!);
                            var valueLower = Expression.Constant(cmdValue.ToLower());
                            containsExpression = Expression.Call(meLower, containsMethod!, valueLower);
                        }
                        else if (prop.PropertyType == typeof(string[]))
                        {
                            continue;
                        }
                        else if (prop.PropertyType.IsArray)
                        {
                            // Skip other array types that can't be easily converted to string
                            continue;
                        }
                        else
                        {
                            // Skip types that Entity Framework cannot translate to SQL
                            var underlyingType = Nullable.GetUnderlyingType(prop.PropertyType);
                            var typeToCheck = underlyingType ?? prop.PropertyType;

                            // Skip enums (including nullable enums) as EF cannot translate enum.ToString()
                            if (typeToCheck.IsEnum)
                            {
                                continue;
                            }

                            // Skip complex types like Dictionary, custom classes, etc.
                            if (!typeToCheck.IsPrimitive && typeToCheck != typeof(DateTime) && typeToCheck != typeof(decimal) && typeToCheck != typeof(Guid))
                            {
                                continue;
                            }

                            // For supported primitive types and DateTime, convert to string
                            if (CanTranslateToString(prop.PropertyType))
                            {
                                var toStringMethod = prop.PropertyType.GetMethod("ToString", new Type[0]);
                                if (toStringMethod != null)
                                {
                                    var ce = Expression.Call(me, toStringMethod);
                                    var ceLower = Expression.Call(ce, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
                                    var valueLower = Expression.Constant(cmdValue.ToLower());
                                    containsExpression = Expression.Call(ceLower, containsMethod!, valueLower);
                                }
                                else
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }

                        orExpression = Expression.Or(orExpression, containsExpression);
                    }
                }

                if (!ExpressionEqualityComparer.Instance.Equals(orExpression, Expression.Constant(false)))
                {
                    var predicate = Expression.Lambda<Func<T, bool>>(orExpression, paramExpr);
                    BuiltQuery = BuiltQuery.Where(predicate);
                }
            }
        }

        private void AddWhereCommands()
        {
            var commands = queryBuilder.WhereData;
            if (commands.Count > 0)
            {
                var expressionParameter = Expression.Parameter(typeof(T));
                Expression andExpression = Expression.Constant(true);
                var andExpressionExist = false;
                Expression orExpression = Expression.Constant(false);
                var errorList = new List<QueryException>();

                foreach (var cmds in commands)
                {
                    try
                    {
                        if (cmds.OrOperation)
                        {
                            foreach (var cmd in cmds.Data)
                            {
                                var expression = ParseWhereCommand(expressionParameter, cmd);
                                orExpression = Expression.Or(orExpression, expression);
                            }
                        }
                        else
                        {
                            foreach (var cmd in cmds.Data)
                            {
                                var expression = ParseWhereCommand(expressionParameter, cmd);
                                andExpression = Expression.And(andExpression, expression);
                                andExpressionExist = true;
                            }
                        }
                    }
                    catch (QueryException e)
                    {
                        errorList.Add(e);
                    }
                }

                if (errorList.Any())
                {
                    throw new QueryException(errorList);
                }

                if (!andExpressionExist)
                {
                    andExpression = Expression.Constant(false);
                }

                var resExpression = Expression.Or(andExpression, orExpression);
                BuiltQuery = BuiltQuery.Where(Expression.Lambda<Func<T, bool>>(resExpression, expressionParameter));
            }
        }

        private Expression ParseWhereCommand(ParameterExpression expressionParameter, QueryModelBuilder<T>.WhereUnitData cmd)
        {
            // Add includes for nested properties
            if (cmd.PropertyPath.IsNested)
            {
                var includePath = string.Join(".", cmd.PropertyPath.Properties.Take(cmd.PropertyPath.Properties.Count - 1).Select(p => p.Name));
                if (!string.IsNullOrEmpty(includePath))
                {
                    BuiltQuery = BuiltQuery.Include(includePath);
                }
            }

            if (QueryModelBuilder<T>.AllowsCollectionNavigationFilters && cmd.PropertyPath.ContainsCollectionNavigation)
            {
                return BuildCollectionNavigationWhereExpression(expressionParameter, cmd.PropertyPath.Properties, 0, cmd);
            }

            var parameterPropertyExpression = BuildNestedPropertyExpression(expressionParameter, cmd.PropertyPath);

            return ApplyWhereOperator(cmd, parameterPropertyExpression);
        }

        private Expression ApplyWhereOperator(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameterPropertyExpression)
        {
            Expression outputExpression;

            Expression CreateEqualExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                Expression orExpression = Expression.Constant(false);
                var hasComparableValue = false;
                var stringValues = cmd.ParseStringValues();
                var parsedValues = cmd.ParseValues(stringValues);

                foreach (var value in parsedValues)
                {
                    if (value == null && !cmd.IsNullableProperty())
                    {
                        continue;
                    }
                    else
                    {
                        Expression eqExpression;
                        if (cmd.PropertyPath.LeafProperty.PropertyType == typeof(string) && value != null)
                        {
                            // Use ToLower() for case-insensitive string comparison that EF Core can translate
                            var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes);
                            var parameterToLower = Expression.Call(parameter, toLowerMethod!);
                            var valueToLower = Expression.Constant(value.ToString()!.ToLower(), typeof(string));
                            eqExpression = Expression.Equal(parameterToLower, valueToLower);
                        }
                        else
                        {
                            var valueParameterExpression = Expression.Constant(value, cmd.PropertyPath.LeafProperty.PropertyType);
                            eqExpression = Expression.Equal(parameter, valueParameterExpression);
                        }

                        hasComparableValue = true;
                        orExpression = Expression.Or(orExpression, eqExpression);
                    }
                }

                return hasComparableValue ? orExpression : Expression.Constant(false);
            }

            Expression CreateNEqualExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                var expression = CreateEqualExpression(cmd, parameter);
                return Expression.Not(expression);
            }

            Expression? CreateCompareExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                Expression? res = null;
                var parsedValue = cmd.ParseValues(new string[] { cmd.StringValue })[0];

                Expression value = Expression.Constant(parsedValue, cmd.PropertyPath.LeafProperty.PropertyType);
                var pEx = parameter;
                var vEx = value;

                if (cmd.PropertyPath.LeafProperty.PropertyType == typeof(string))
                {
                    var compareMethod = cmd.PropertyPath.LeafProperty.PropertyType.GetMethod("CompareTo", new[] { typeof(string) });
                    pEx = Expression.Call(parameter, compareMethod!, value);
                    vEx = Expression.Constant(0);
                }

                if (cmd.Operation == WOperand.GreaterThan)
                {
                    res = Expression.GreaterThan(pEx, vEx);
                }
                else if (cmd.Operation == WOperand.GreaterThanOrEqualTo)
                {
                    res = Expression.GreaterThanOrEqual(pEx, vEx);
                }
                else if (cmd.Operation == WOperand.LessThan)
                {
                    res = Expression.LessThan(pEx, vEx);
                }
                else if (cmd.Operation == WOperand.LessThanOrEqualTo)
                {
                    res = Expression.LessThanOrEqual(pEx, vEx);
                }

                return res;
            }

            Expression? CreateLikeExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                var parsedValue = cmd.ParseValues(new string[] { cmd.StringValue })[0];

                Expression value = Expression.Constant(parsedValue, cmd.PropertyPath.LeafProperty.PropertyType);
                Expression? res = null;

                var matchOperation = typeof(Regex).GetMethod("IsMatch", BindingFlags.Static | BindingFlags.Public, new[] { typeof(string), typeof(string), typeof(RegexOptions) });
                var trueConstant = Expression.Constant(true);
                var falseConstant = Expression.Constant(false);
                var regexOptionExpression = Expression.Constant(RegexOptions.IgnoreCase);

                if (cmd.Operation == WOperand.Like)
                {
                    res = Expression.Equal(Expression.Call(matchOperation!, parameter, value, regexOptionExpression), trueConstant);
                }
                else if (cmd.Operation == WOperand.NLike)
                {
                    res = Expression.Equal(Expression.Call(matchOperation!, parameter, value, regexOptionExpression), falseConstant);
                }

                return res;
            }

            Expression? CreateContainExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                var matchOperation = typeof(Regex).GetMethod("IsMatch", BindingFlags.Static | BindingFlags.Public, new[] { typeof(string), typeof(string), typeof(RegexOptions) });
                var regexOptionExpression = Expression.Constant(RegexOptions.IgnoreCase);

                var data = cmd.ParseContainValue(cmd.StringValue);
                var sb = new StringBuilder();

                sb.Append('^');
                foreach (var d in data)
                {
                    if (d.Item1 == QueryModelBuilder<T>.WhereUnitData.ContainsType.MatchAll)
                    {
                        sb.Append("(.*)");
                    }
                    else if (d.Item1 == QueryModelBuilder<T>.WhereUnitData.ContainsType.Substring)
                    {
                        sb.Append(Regex.Escape(d.Item2));
                    }
                }

                sb.Append('$');

                var valueParameterExpression = Expression.Constant(sb.ToString(), typeof(string));

                var propertyType = cmd.PropertyPath.LeafProperty.PropertyType;
                if (propertyType == typeof(string[]) ||
                    (propertyType.IsGenericType && typeof(IEnumerable<string>).IsAssignableFrom(propertyType)))
                {
                    // For array/collection of strings: check whether any element matches the pattern
                    var elementParam = Expression.Parameter(typeof(string), "element");
                    var regexCall = Expression.Call(matchOperation!, elementParam, valueParameterExpression, regexOptionExpression);
                    var elementLambda = Expression.Lambda<Func<string, bool>>(regexCall, elementParam);

                    var anyMethod = typeof(Enumerable)
                        .GetMethods(BindingFlags.Static | BindingFlags.Public)
                        .First(m => m.Name == "Any" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(typeof(string));

                    Expression anyCall = Expression.Call(anyMethod, parameter, elementLambda);

                    return cmd.Operation == WOperand.NContains ? Expression.Not(anyCall) : anyCall;
                }

                var trueConstant = Expression.Constant(true);
                var falseConstant = Expression.Constant(false);
                Expression? res = null;

                if (cmd.Operation == WOperand.Contains)
                {
                    res = Expression.Equal(Expression.Call(matchOperation!, parameter, valueParameterExpression, regexOptionExpression), trueConstant);
                }
                else if (cmd.Operation == WOperand.NContains)
                {
                    res = Expression.Equal(Expression.Call(matchOperation!, parameter, valueParameterExpression, regexOptionExpression), falseConstant);
                }

                return res;
            }

            Expression? CreateStartsWithExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                if (cmd.PropertyPath.LeafProperty.PropertyType != typeof(string))
                {
                    throw new QueryException(cmd.Cmd.Source, "StartsWith operand is only supported for string properties");
                }

                var parsedValue = cmd.ParseValues(new string[] { cmd.StringValue })[0];
                var value = Expression.Constant(parsedValue, typeof(string));
                var comparison = Expression.Constant(StringComparison.OrdinalIgnoreCase);
                var method = typeof(string).GetMethod("StartsWith", new[] { typeof(string), typeof(StringComparison) });

                return Expression.Call(parameter, method!, value, comparison);
            }

            Expression CreateIsEmptyExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                var propertyType = cmd.PropertyPath.LeafProperty.PropertyType;

                if (propertyType == typeof(string))
                {
                    return Expression.OrElse(
                        Expression.Equal(parameter, Expression.Constant(null, typeof(string))),
                        Expression.Equal(parameter, Expression.Constant(string.Empty, typeof(string))));
                }

                if (Nullable.GetUnderlyingType(propertyType) != null)
                {
                    return Expression.Equal(parameter, Expression.Constant(null, propertyType));
                }

                throw new QueryException(cmd.Cmd.Source, "IsEmpty operand is only supported for string or nullable properties");
            }

            Expression CreateIsNotEmptyExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
            {
                var propertyType = cmd.PropertyPath.LeafProperty.PropertyType;

                if (propertyType == typeof(string))
                {
                    return Expression.AndAlso(
                        Expression.NotEqual(parameter, Expression.Constant(null, typeof(string))),
                        Expression.NotEqual(parameter, Expression.Constant(string.Empty, typeof(string))));
                }

                if (Nullable.GetUnderlyingType(propertyType) != null)
                {
                    return Expression.NotEqual(parameter, Expression.Constant(null, propertyType));
                }

                throw new QueryException(cmd.Cmd.Source, "IsNotEmpty operand is only supported for string or nullable properties");
            }

            try
            {
                switch (cmd.Operation)
                {
                    case WOperand.Equal:
                        outputExpression = CreateEqualExpression(cmd, parameterPropertyExpression);
                        break;
                    case WOperand.IsEmpty:
                        outputExpression = CreateIsEmptyExpression(cmd, parameterPropertyExpression);
                        break;
                    case WOperand.IsNotEmpty:
                        outputExpression = CreateIsNotEmptyExpression(cmd, parameterPropertyExpression);
                        break;
                    case WOperand.NotEqual:
                        outputExpression = CreateNEqualExpression(cmd, parameterPropertyExpression);
                        break;
                    case WOperand.InList:
                        outputExpression = CreateInListExpression(cmd, parameterPropertyExpression);
                        break;
                    case WOperand.GreaterThan:
                    case WOperand.GreaterThanOrEqualTo:
                    case WOperand.LessThan:
                    case WOperand.LessThanOrEqualTo:
                        outputExpression = CreateCompareExpression(cmd, parameterPropertyExpression)!;
                        break;
                    case WOperand.Like:
                    case WOperand.NLike:
                        outputExpression = CreateLikeExpression(cmd, parameterPropertyExpression)!;
                        break;
                    case WOperand.Contains:
                    case WOperand.NContains:
                        outputExpression = CreateContainExpression(cmd, parameterPropertyExpression)!;
                        break;
                    case WOperand.StartsWith:
                        outputExpression = CreateStartsWithExpression(cmd, parameterPropertyExpression)!;
                        break;
                    default:
                        throw new QueryException(cmd.Cmd.Source, $"No such operand '{cmd.Operation}'");
                }
            }
            catch (Exception ex)
            {
                throw new QueryException(cmd.Cmd.Source, ex.Message);
            }

            return outputExpression;
        }

        private Expression BuildCollectionNavigationWhereExpression(
            Expression parentExpression,
            IReadOnlyList<PropertyInfo> properties,
            int index,
            QueryModelBuilder<T>.WhereUnitData cmd)
        {
            var property = properties[index];
            var propertyExpression = Expression.Property(parentExpression, property);

            if (TryGetCollectionElementType(property.PropertyType, out var elementType))
            {
                if (index >= properties.Count - 1)
                {
                    throw new QueryException(cmd.Cmd.Source, $"Collection navigation '{property.Name}' must end in a scalar property.");
                }

                var elementParameter = Expression.Parameter(elementType, char.ToLowerInvariant(elementType.Name[0]).ToString());
                var innerExpression = BuildCollectionNavigationWhereExpression(elementParameter, properties, index + 1, cmd);
                var anyMethod = typeof(Enumerable).GetMethods()
                    .First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2)
                    .MakeGenericMethod(elementType);

                return Expression.Call(anyMethod, propertyExpression, Expression.Lambda(innerExpression, elementParameter));
            }

            if (index < properties.Count - 1)
            {
                return BuildCollectionNavigationWhereExpression(parentExpression: propertyExpression, properties, index + 1, cmd);
            }

            return ApplyWhereOperator(cmd, propertyExpression);
        }

        private bool TryGetCollectionElementType(Type propertyType, out Type elementType)
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

        private Expression CreateInListExpression(QueryModelBuilder<T>.WhereUnitData cmd, Expression parameter)
        {
            // Parse comma-separated values, trim whitespace, and convert to property type
            var stringValues = cmd.StringValue.Split(',').Select(s => s.Trim()).ToArray();
            var parsedValues = cmd.ParseValues(stringValues);

            if (cmd.PropertyPath.LeafProperty.PropertyType == typeof(string))
            {
                // For string properties, use case-insensitive comparison with ToLower()
                Expression orExpression = Expression.Constant(false);
                var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes);
                var parameterToLower = Expression.Call(parameter, toLowerMethod!);

                foreach (var value in parsedValues)
                {
                    if (value != null)
                    {
                        var valueToLower = Expression.Constant(value.ToString()!.ToLower(), typeof(string));
                        var eqExpression = Expression.Equal(parameterToLower, valueToLower);
                        orExpression = Expression.Or(orExpression, eqExpression);
                    }
                }

                return orExpression;
            }
            else
            {
                // For non-string properties, use the original Contains logic
                var valuesArray = Array.CreateInstance(cmd.PropertyPath.LeafProperty.PropertyType, parsedValues.Count);
                for (int i = 0; i < parsedValues.Count; i++)
                {
                    valuesArray.SetValue(parsedValues[i], i);
                }

                var containsMethod = typeof(Enumerable).GetMethods()
                    .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                    .MakeGenericMethod(cmd.PropertyPath.LeafProperty.PropertyType);
                var arrayExpr = Expression.Constant(valuesArray);
                return Expression.Call(containsMethod, arrayExpr, parameter);
            }
        }

        private Expression BuildNestedPropertyExpression(ParameterExpression parameter, PropertyPath propertyPath)
        {
            Expression expression = parameter;
            foreach (var property in propertyPath.Properties)
            {
                expression = Expression.Property(expression, property);
            }

            return expression;
        }

        private async Task<IList<T>?> GetSelectResult()
        {
            var hasMainProperties = queryBuilder.SelectData.SelectedProperties.Any();
            var hasNestedProperties = queryBuilder.SelectData.SelectedNestedProperties.Any();

            if (hasMainProperties || hasNestedProperties)
            {
                var expressionParameter = Expression.Parameter(typeof(T));

                // Build property lists and name mappings for the dynamic type
                var propertyInfosForType = new List<(string Name, System.Reflection.PropertyInfo PropertyInfo)>();
                var propertyBindings = new List<MemberBinding>();

                // Add main properties
                foreach (var prop in queryBuilder.SelectData.SelectedProperties)
                {
                    propertyInfosForType.Add((prop.Name, prop));
                }

                // Add nested properties with flattened names (e.g., ContactFullName for contact.fullName)
                foreach (var nestedProp in queryBuilder.SelectData.SelectedNestedProperties)
                {
                    var flattenedName = string.Join(string.Empty, nestedProp.Properties.Select(p => p.Name));
                    propertyInfosForType.Add((flattenedName, nestedProp.LeafProperty));
                }

                // Create dynamic type with property names (using flattened names for nested properties)
                var outputType = TypeHelper.CompileTypeForSelectStatement(
                    propertyInfosForType.Select(p => (p.Name, p.PropertyInfo.PropertyType)).ToArray());
                var delegateType = typeof(Func<,>).MakeGenericType(typeof(T), outputType);
                var createOutputTypeExpression = Expression.New(outputType);

                // Build property bindings for main properties
                foreach (var prop in queryBuilder.SelectData.SelectedProperties)
                {
                    var bindProp = outputType.GetProperty(prop.Name);
                    var exprProp = Expression.Property(expressionParameter, prop);
                    propertyBindings.Add(Expression.Bind(bindProp!, exprProp));
                }

                // Build property bindings for nested properties
#pragma warning disable S3267 // Cannot simplify - building expression tree with side effects
                foreach (var nestedProp in queryBuilder.SelectData.SelectedNestedProperties)
                {
                    var flattenedName = string.Join(string.Empty, nestedProp.Properties.Select(p => p.Name));
                    var bindProp = outputType.GetProperty(flattenedName);

                    // Build nested property access expression
                    Expression propertyExpression = expressionParameter;
                    foreach (var prop in nestedProp.Properties)
                    {
                        propertyExpression = Expression.Property(propertyExpression, prop);
                    }

                    propertyBindings.Add(Expression.Bind(bindProp!, propertyExpression));
                }
#pragma warning restore S3267

                var expressionCreateArray = Expression.MemberInit(createOutputTypeExpression, propertyBindings.ToArray());
                dynamic lambda = Expression.Lambda(delegateType, expressionCreateArray, expressionParameter);

                var queryMethod = typeof(Queryable).GetMethods().FirstOrDefault(m => m.Name == "Select" && m.GetParameters()[1].ParameterType.GetGenericArguments()[0].GetGenericArguments().Length == 2)!.MakeGenericMethod(typeof(T), outputType);

                var toArrayAsyncMethod = typeof(EntityFrameworkQueryableExtensions).GetMethod("ToArrayAsync")!.MakeGenericMethod(outputType);

                var selectQueryable = queryMethod!.Invoke(BuiltQuery, new object[] { BuiltQuery, lambda });

                var outputTypeTaskResultProp = typeof(Task<>).MakeGenericType(outputType.MakeArrayType()).GetProperty("Result");

                var selectResult = (Task)toArrayAsyncMethod.Invoke(selectQueryable, new object?[] { selectQueryable!, null })!;
                await selectResult;
                var taskResult = outputTypeTaskResultProp!.GetValue(selectResult);
                if (taskResult is Array arr)
                {
                    DynamicResults = arr;
                }

                return taskResult as IList<T>;
            }
            else
            {
                return null;
            }
        }
    }
}