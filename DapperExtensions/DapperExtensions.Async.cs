using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using DapperExtensions.Sql;
using DapperExtensions.Mapper;
using Dapper;
using System.Threading.Tasks;

namespace DapperExtensions
{
    public static partial class DapperExtensions
    {
        /// <summary>
        /// Executes a query for the specified id, returning the data typed as per T
        /// </summary>
        public static Task<T> GetAsync<T>(this IDbConnection connection, dynamic id, IDbTransaction transaction = null, int? commandTimeout = null, string keyName = null, string hints = null) where T : class
        {
            return Instance.GetAsync<T>(connection, id, transaction, commandTimeout, keyName, hints);
        }

        /// <summary>
        /// Executes an insert query for the specified entity.
        /// </summary>
        public static Task InsertAsync<T>(this IDbConnection connection, IEnumerable<T> entities, IDbTransaction transaction = null, int? commandTimeout = null) where T : class
        {
            return Instance.InsertAsync<T>(connection, entities, transaction, commandTimeout);
        }

        /// <summary>
        /// Executes an insert query for the specified entity, returning the primary key.  
        /// If the entity has a single key, just the value is returned.  
        /// If the entity has a composite key, an IDictionary&lt;string, object&gt; is returned with the key values.
        /// The key value for the entity will also be updated if the KeyType is a Guid or Identity.
        /// </summary>
        public static Task<dynamic> InsertAsync<T>(this IDbConnection connection, T entity, IDbTransaction transaction = null, int? commandTimeout = null) where T : class
        {
            return Instance.InsertAsync<T>(connection, entity, transaction, commandTimeout);
        }

        /// <summary>
        /// Executes an update query for the specified entity.
        /// </summary>
        public static Task<bool> UpdateAsync<T>(this IDbConnection connection, T entity, IDbTransaction transaction = null, int? commandTimeout = null, string keyName = null, Snapshotter.Snapshot snapshot = null) where T : class
        {
            return Instance.UpdateAsync<T>(connection, entity, transaction, commandTimeout, keyName, snapshot, null);
        }

        /// <summary>
        /// Executes an update query for the specified entity.
        /// </summary>
        public static Task<bool> UpdatePartialAsync<T>(this IDbConnection connection, T entity, IEnumerable<string> properties, IDbTransaction transaction = null, int? commandTimeout = null, string keyName = null) where T : class
        {
            return Instance.UpdateAsync<T>(connection, entity, transaction, commandTimeout, keyName, null, properties);
        }

        /// <summary>
        /// Executes a delete query for the specified entity.
        /// </summary>
        public static Task<bool> DeleteAsync<T>(this IDbConnection connection, T entity, IDbTransaction transaction = null, int? commandTimeout = null, string keyName = null) where T : class
        {
            return Instance.DeleteAsync<T>(connection, entity, transaction, commandTimeout, keyName);
        }

        /// <summary>
        /// Executes a delete query using the specified predicate.
        /// </summary>
        public static Task<bool> DeleteAsync<T>(this IDbConnection connection, object predicate, IDbTransaction transaction = null, int? commandTimeout = null) where T : class
        {
            return Instance.DeleteAsync<T>(connection, predicate, transaction, commandTimeout);
        }

        /// <summary>
        /// Executes a select query using the specified predicate, returning an IEnumerable data typed as per T.
        /// </summary>
        public static Task<IEnumerable<T>> GetListAsync<T>(this IDbConnection connection, object predicate = null, IList<ISort> sort = null, IDbTransaction transaction = null, int? commandTimeout = null, string hints = null) where T : class
        {
            return Instance.GetListAsync<T>(connection, predicate, sort, transaction, commandTimeout, hints);
        }

        /// <summary>
        /// Executes a select query using the specified predicate, returning an IEnumerable data typed as per T.
        /// Data returned is dependent upon the specified page and resultsPerPage.
        /// </summary>
        public static Task<IEnumerable<T>> GetPageAsync<T>(this IDbConnection connection, object predicate, IList<ISort> sort, int page, int resultsPerPage, IDbTransaction transaction = null, int? commandTimeout = null) where T : class
        {
            return Instance.GetPageAsync<T>(connection, predicate, sort, page, resultsPerPage, transaction, commandTimeout);
        }

        /// <summary>
        /// Executes a select query using the specified predicate, returning an IEnumerable data typed as per T.
        /// Data returned is dependent upon the specified firstResult and maxResults.
        /// </summary>
        public static Task<IEnumerable<T>> GetSetAsync<T>(this IDbConnection connection, object predicate, IList<ISort> sort, int firstResult, int maxResults, IDbTransaction transaction = null, int? commandTimeout = null) where T : class
        {
            return Instance.GetSetAsync<T>(connection, predicate, sort, firstResult, maxResults, transaction, commandTimeout);
        }

        /// <summary>
        /// Executes a query using the specified predicate, returning an integer that represents the number of rows that match the query.
        /// </summary>
        public static Task<int> CountAsync<T>(this IDbConnection connection, object predicate, IDbTransaction transaction = null, int? commandTimeout = null) where T : class
        {
            return Instance.CountAsync<T>(connection, predicate, transaction, commandTimeout);
        }

        /// <summary>
        /// Executes a select query for multiple objects, returning IMultipleResultReader for each predicate.
        /// </summary>
        public static Task<IMultipleResultReader> GetMultipleAsync(this IDbConnection connection, GetMultiplePredicate predicate, IDbTransaction transaction = null, int? commandTimeout = null)
        {
            return Instance.GetMultipleAsync(connection, predicate, transaction, commandTimeout);
        }
    }
}
