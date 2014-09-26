using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using DapperExtensions.Mapper;
using DapperExtensions.Sql;

namespace DapperExtensions
{
    public partial interface IDapperImplementor
    {
        Task<T> GetAsync<T>(IDbConnection connection, object predicate, IDbTransaction transaction, int? commandTimeout, string keyName, string hints) where T : class;
        Task InsertAsync<T>(IDbConnection connection, IEnumerable<T> entities, IDbTransaction transaction, int? commandTimeout) where T : class;
        Task<dynamic> InsertAsync<T>(IDbConnection connection, T entity, IDbTransaction transaction, int? commandTimeout) where T : class;
        Task<bool> UpdateAsync<T>(IDbConnection connection, T entity, IDbTransaction transaction, int? commandTimeout, string keyName, Snapshotter.Snapshot snapshot, IEnumerable<string> properties) where T : class;
        Task<bool> DeleteAsync<T>(IDbConnection connection, T entity, IDbTransaction transaction, int? commandTimeout, string keyName) where T : class;
        Task<bool> DeleteAsync<T>(IDbConnection connection, object predicate, IDbTransaction transaction, int? commandTimeout) where T : class;
        Task<IEnumerable<T>> GetListAsync<T>(IDbConnection connection, object predicate, IList<ISort> sort, IDbTransaction transaction, int? commandTimeout, string hints, long? top) where T : class;
        Task<IEnumerable<T>> GetPageAsync<T>(IDbConnection connection, object predicate, IList<ISort> sort, int page, int resultsPerPage, IDbTransaction transaction, int? commandTimeout) where T : class;
        Task<IEnumerable<T>> GetSetAsync<T>(IDbConnection connection, object predicate, IList<ISort> sort, int firstResult, int maxResults, IDbTransaction transaction, int? commandTimeout) where T : class;
        Task<long> CountAsync<T>(IDbConnection connection, object predicate, IDbTransaction transaction, int? commandTimeout) where T : class;
        Task<IMultipleResultReader> GetMultipleAsync(IDbConnection connection, GetMultiplePredicate predicate, IDbTransaction transaction, int? commandTimeout);
    }

    public partial class DapperImplementor : IDapperImplementor
    {
        public async Task<T> GetAsync<T>(IDbConnection connection, object predicate, IDbTransaction transaction, int? commandTimeout, string keyName, string hints) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            //IPredicate predicate = GetIdPredicate(classMap, id, keyName);
            IPredicate wherePredicate = predicate as IPredicate;
            if (wherePredicate == null && predicate != null)
                wherePredicate = GetIdPredicate(classMap, predicate, keyName);
            var result = await GetListAsync<T>(connection, classMap, wherePredicate, null, transaction, commandTimeout, hints, 2);
            return result.SingleOrDefault();
        }

        public Task InsertAsync<T>(IDbConnection connection, IEnumerable<T> entities, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            var properties = classMap.Properties.Where(p => !p.IsKeyType(KeyType.NotAKey));

            foreach (var e in entities)
            {
                classMap.OnInsert(e);

                foreach (var column in properties)
                {
                    if (column.IsKeyType(KeyType.Guid))
                    {
                        Guid comb = SqlGenerator.Configuration.GetNextGuid();
                        column.PropertyInfo.SetValue(e, comb, null);
                    }
                }
            }

            string sql = SqlGenerator.Insert(classMap);

            return connection.ExecuteAsync(sql, entities, transaction, commandTimeout, CommandType.Text);
        }

        public async Task<dynamic> InsertAsync<T>(IDbConnection connection, T entity, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            classMap.OnInsert(entity);
            List<IPropertyMap> nonIdentityKeyProperties = classMap.Properties.Where(p => p.IsAnyKeyType(KeyType.Guid) || p.IsAnyKeyType(KeyType.Assigned)).ToList();
            var identityColumn = classMap.Properties.SingleOrDefault(p => p.IsAnyKeyType(KeyType.Identity));
            foreach (var column in nonIdentityKeyProperties)
            {
                if (column.IsKeyType(KeyType.Guid))
                {
                    Guid comb = SqlGenerator.Configuration.GetNextGuid();
                    column.PropertyInfo.SetValue(entity, comb, null);
                }
            }

            IDictionary<string, object> keyValues = new ExpandoObject();
            string sql = SqlGenerator.Insert(classMap);
            if (identityColumn != null)
            {
                IEnumerable<long> result;
                if (SqlGenerator.SupportsMultipleStatements())
                {
                    sql += SqlGenerator.Configuration.Dialect.BatchSeperator + SqlGenerator.IdentitySql(classMap);
                    result = await connection.QueryAsync<long>(sql, entity, transaction, commandTimeout, CommandType.Text);
                }
                else
                {
                    await connection.ExecuteAsync(sql, entity, transaction, commandTimeout, CommandType.Text);
                    sql = SqlGenerator.IdentitySql(classMap);
                    result = await connection.QueryAsync<long>(sql, entity, transaction, commandTimeout, CommandType.Text);
                }

                long identityValue = result.First();
                if (identityColumn.PropertyInfo.PropertyType.Equals(typeof(long))) {
                    keyValues.Add(identityColumn.Name, identityValue);
                    identityColumn.PropertyInfo.SetValue(entity, identityValue, null);
                }
                else {
                    int identityInt = Convert.ToInt32(identityValue);
                    keyValues.Add(identityColumn.Name, identityInt);
                    identityColumn.PropertyInfo.SetValue(entity, identityInt, null);
                }
            }
            else
            {
                await connection.ExecuteAsync(sql, entity, transaction, commandTimeout, CommandType.Text);
            }

            foreach (var column in nonIdentityKeyProperties)
            {
                keyValues.Add(column.Name, column.PropertyInfo.GetValue(entity, null));
            }

            if (keyValues.Count == 1)
            {
                return keyValues.First().Value;
            }

            return keyValues;
        }

        public async Task<bool> UpdateAsync<T>(IDbConnection connection, T entity, IDbTransaction transaction, int? commandTimeout, string keyName, Snapshotter.Snapshot snapshot, IEnumerable<string> properties) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            keyName = keyName ?? classMap.DefaultUpdateKeyName;

            DynamicParameters diff = null;
            if (snapshot != null && !classMap.IsPartialUpdateDisabled) {
                diff = snapshot.Diff();
                var upl = diff.ParameterNames.ToList<string>();
                if (upl.Count == 0)
                    return true;
                properties = upl;
            }
            
            bool updated = classMap.OnUpdate(entity);

            if (updated && snapshot != null && !classMap.IsPartialUpdateDisabled) {
                diff = snapshot.Diff();
                var upl = diff.ParameterNames.ToList<string>();
                if (upl.Count == 0)
                    return true;
                properties = upl;
            }

            IPredicate predicate = GetKeyPredicate<T>(classMap, entity, keyName);
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string sql = SqlGenerator.Update(classMap, predicate, parameters, keyName, properties);
            DynamicParameters dynamicParameters = new DynamicParameters();

            var columns = classMap.Properties.Where(p => !(p.Ignored || p.IsReadOnly || p.IsInsertOnly || p.IsAnyKeyType(KeyType.Identity)) && (properties == null || properties.Contains(p.Name)));
            if (diff != null) {
                foreach (var property in diff.ParameterNames.Where(property => columns.Any(c => c.Name == property))) {
                    var val = ((SqlMapper.IParameterLookup)diff)[property];
                    dynamicParameters.Add(property, val);
                }
            }
            else {
                foreach (var property in ReflectionHelper.GetObjectValues(entity).Where(property => columns.Any(c => c.Name == property.Key))) {
                    dynamicParameters.Add(property.Key, property.Value);
                }
            }

            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            return await connection.ExecuteAsync(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text) > 0;
        }

        public Task<bool> DeleteAsync<T>(IDbConnection connection, T entity, IDbTransaction transaction, int? commandTimeout, string keyName) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            keyName = keyName ?? classMap.DefaultDeleteKeyName;
            IPredicate predicate = GetKeyPredicate<T>(classMap, entity, keyName);
            return DeleteAsync<T>(connection, classMap, predicate, transaction, commandTimeout);
        }

        public Task<bool> DeleteAsync<T>(IDbConnection connection, object predicate, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            IPredicate wherePredicate = GetPredicate(classMap, predicate);
            return DeleteAsync<T>(connection, classMap, wherePredicate, transaction, commandTimeout);
        }

        public Task<IEnumerable<T>> GetListAsync<T>(IDbConnection connection, object predicate, IList<ISort> sort, IDbTransaction transaction, int? commandTimeout, string hints, long? top) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            IPredicate wherePredicate = GetPredicate(classMap, predicate);
            return GetListAsync<T>(connection, classMap, wherePredicate, sort, transaction, commandTimeout, hints, top);
        }

        public Task<IEnumerable<T>> GetPageAsync<T>(IDbConnection connection, object predicate, IList<ISort> sort, int page, int resultsPerPage, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            IPredicate wherePredicate = GetPredicate(classMap, predicate);
            return GetPageAsync<T>(connection, classMap, wherePredicate, sort, page, resultsPerPage, transaction, commandTimeout);
        }

        public Task<IEnumerable<T>> GetSetAsync<T>(IDbConnection connection, object predicate, IList<ISort> sort, int firstResult, int maxResults, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            IPredicate wherePredicate = GetPredicate(classMap, predicate);
            return GetSetAsync<T>(connection, classMap, wherePredicate, sort, firstResult, maxResults, transaction, commandTimeout);
        }

        public async Task<long> CountAsync<T>(IDbConnection connection, object predicate, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            IClassMapper classMap = SqlGenerator.Configuration.GetMap<T>();
            IPredicate wherePredicate = GetPredicate(classMap, predicate);
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string sql = SqlGenerator.Count(classMap, wherePredicate, parameters);
            DynamicParameters dynamicParameters = new DynamicParameters();
            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            var res = await connection.QueryAsync<long>(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text);
            return res.Single();
        }

        public async Task<IMultipleResultReader> GetMultipleAsync(IDbConnection connection, GetMultiplePredicate predicate, IDbTransaction transaction, int? commandTimeout)
        {
            if (SqlGenerator.SupportsMultipleStatements())
            {
                return await GetMultipleByBatchAsync(connection, predicate, transaction, commandTimeout);
            }

            return await GetMultipleBySequenceAsync(connection, predicate, transaction, commandTimeout);
        }

        protected Task<IEnumerable<T>> GetListAsync<T>(IDbConnection connection, IClassMapper classMap, IPredicate predicate, IList<ISort> sort, IDbTransaction transaction, int? commandTimeout, string hints, long? top) where T : class
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string sql = SqlGenerator.Select(classMap, predicate, sort, parameters, hints, top);
            DynamicParameters dynamicParameters = new DynamicParameters();
            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            return connection.QueryAsync<T>(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text);
        }

        protected Task<IEnumerable<T>> GetPageAsync<T>(IDbConnection connection, IClassMapper classMap, IPredicate predicate, IList<ISort> sort, int page, int resultsPerPage, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string sql = SqlGenerator.SelectPaged(classMap, predicate, sort, page, resultsPerPage, parameters);
            DynamicParameters dynamicParameters = new DynamicParameters();
            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            return connection.QueryAsync<T>(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text);
        }

        protected Task<IEnumerable<T>> GetSetAsync<T>(IDbConnection connection, IClassMapper classMap, IPredicate predicate, IList<ISort> sort, int firstResult, int maxResults, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string sql = SqlGenerator.SelectSet(classMap, predicate, sort, firstResult, maxResults, parameters);
            DynamicParameters dynamicParameters = new DynamicParameters();
            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            return connection.QueryAsync<T>(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text);
        }

        protected async Task<bool> DeleteAsync<T>(IDbConnection connection, IClassMapper classMap, IPredicate predicate, IDbTransaction transaction, int? commandTimeout) where T : class
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            string sql = SqlGenerator.Delete(classMap, predicate, parameters);
            DynamicParameters dynamicParameters = new DynamicParameters();
            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            return await connection.ExecuteAsync(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text) > 0;
        }

        protected async Task<GridReaderResultReader> GetMultipleByBatchAsync(IDbConnection connection, GetMultiplePredicate predicate, IDbTransaction transaction, int? commandTimeout)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            StringBuilder sql = new StringBuilder();
            foreach (var item in predicate.Items)
            {
                IClassMapper classMap = SqlGenerator.Configuration.GetMap(item.Type);
                IPredicate itemPredicate = item.Value as IPredicate;
                if (itemPredicate == null && item.Value != null)
                {
                    itemPredicate = GetPredicate(classMap, item.Value);
                }

                sql.AppendLine(SqlGenerator.Select(classMap, itemPredicate, item.Sort, parameters) + SqlGenerator.Configuration.Dialect.BatchSeperator);
            }

            DynamicParameters dynamicParameters = new DynamicParameters();
            foreach (var parameter in parameters)
            {
                dynamicParameters.Add(parameter.Key, parameter.Value);
            }

            SqlMapper.GridReader grid = await connection.QueryMultipleAsync(sql.ToString(), dynamicParameters, transaction, commandTimeout, CommandType.Text);
            return new GridReaderResultReader(grid);
        }

        protected async Task<SequenceReaderResultReader> GetMultipleBySequenceAsync(IDbConnection connection, GetMultiplePredicate predicate, IDbTransaction transaction, int? commandTimeout)
        {
            IList<SqlMapper.GridReader> items = new List<SqlMapper.GridReader>();
            foreach (var item in predicate.Items)
            {
                Dictionary<string, object> parameters = new Dictionary<string, object>();
                IClassMapper classMap = SqlGenerator.Configuration.GetMap(item.Type);
                IPredicate itemPredicate = item.Value as IPredicate;
                if (itemPredicate == null && item.Value != null)
                {
                    itemPredicate = GetPredicate(classMap, item.Value);
                }

                string sql = SqlGenerator.Select(classMap, itemPredicate, item.Sort, parameters);
                DynamicParameters dynamicParameters = new DynamicParameters();
                foreach (var parameter in parameters)
                {
                    dynamicParameters.Add(parameter.Key, parameter.Value);
                }

                SqlMapper.GridReader queryResult = await connection.QueryMultipleAsync(sql, dynamicParameters, transaction, commandTimeout, CommandType.Text);
                items.Add(queryResult);
            }

            return new SequenceReaderResultReader(items);
        }
    }
}
