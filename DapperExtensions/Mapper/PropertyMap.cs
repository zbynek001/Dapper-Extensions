using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DapperExtensions.Mapper
{
    /// <summary>
    /// Maps an entity property to its corresponding column in the database.
    /// </summary>
    public interface IPropertyMap
    {
        string Name { get; }
        string ColumnName { get; }
        bool Ignored { get; }
        bool IsReadOnly { get; }
        bool IsInsertOnly { get; }
        KeyType KeyType { get; }
        KeyType GetKeyType(string name);
        bool IsAnyKeyType(KeyType keyType);
        bool IsKeyType(KeyType keyType);
        PropertyInfo PropertyInfo { get; }
    }

    /// <summary>
    /// Maps an entity property to its corresponding column in the database.
    /// </summary>
    public class PropertyMap : IPropertyMap
    {
        public PropertyMap(PropertyInfo propertyInfo)
        {
            PropertyInfo = propertyInfo;
            ColumnName = PropertyInfo.Name;
        }

        /// <summary>
        /// Gets the name of the property by using the specified propertyInfo.
        /// </summary>
        public string Name
        {
            get { return PropertyInfo.Name; }
        }

        /// <summary>
        /// Gets the column name for the current property.
        /// </summary>
        public string ColumnName { get; private set; }

        /// <summary>
        /// Gets the key type for the current property.
        /// </summary>
        public KeyType KeyType { get; private set; }

        private Dictionary<string, KeyType> keyTypes = new Dictionary<string, KeyType>();

        public KeyType GetKeyType(string name)
        {
            if (name == null)
                return KeyType;
            KeyType r;
            if (keyTypes.TryGetValue(name, out r))
                return r;
            return Mapper.KeyType.NotAKey;
        }

        public bool IsAnyKeyType(KeyType keyType)
        {
            return KeyType == keyType || keyTypes.Values.Any(i => i == keyType);
        }

        public bool IsKeyType(KeyType keyType)
        {
            if (keyType == Mapper.KeyType.NotAKey)
                return KeyType == keyType && keyTypes.Values.All(i => i == keyType);
            if(KeyType != Mapper.KeyType.NotAKey)
                return KeyType == keyType && keyTypes.Values.All(i => i == keyType || i == Mapper.KeyType.NotAKey);
            else
                return keyTypes.Count > 0 && keyTypes.Values.All(i => i == keyType || i == Mapper.KeyType.NotAKey);
        }

        /// <summary>
        /// Gets the ignore status of the current property. If ignored, the current property will not be included in queries.
        /// </summary>
        public bool Ignored { get; private set; }

        /// <summary>
        /// Gets the read-only status of the current property. If read-only, the current property will not be included in INSERT and UPDATE queries.
        /// </summary>
        public bool IsReadOnly { get; private set; }

        public bool IsInsertOnly { get; private set; }

        /// <summary>
        /// Gets the property info for the current property.
        /// </summary>
        public PropertyInfo PropertyInfo { get; private set; }

        /// <summary>
        /// Fluently sets the column name for the property.
        /// </summary>
        /// <param name="columnName">The column name as it exists in the database.</param>
        public PropertyMap Column(string columnName)
        {
            ColumnName = columnName;
            return this;
        }

        /// <summary>
        /// Fluently sets the key type of the property.
        /// </summary>
        /// <param name="columnName">The column name as it exists in the database.</param>
        public PropertyMap Key(KeyType keyType)
        {
            if (Ignored) {
                throw new ArgumentException(string.Format("'{0}' is ignored and cannot be made a key field. ", Name));
            }

            if (IsReadOnly) {
                throw new ArgumentException(string.Format("'{0}' is readonly and cannot be made a key field. ", Name));
            }

            KeyType = keyType;
            return this;
        }

        public PropertyMap Key(string name, KeyType keyType)
        {
            if (Ignored) {
                throw new ArgumentException(string.Format("'{0}' is ignored and cannot be made a key field. ", Name));
            }

            //if (IsReadOnly) {
            //    throw new ArgumentException(string.Format("'{0}' is readonly and cannot be made a key field. ", Name));
            //}

            keyTypes[name] = keyType;
            //UpdateKeyType = keyType;
            return this;
        }

        /// <summary>
        /// Fluently sets the ignore status of the property.
        /// </summary>
        public PropertyMap Ignore()
        {
            if (KeyType != KeyType.NotAKey) {
                throw new ArgumentException(string.Format("'{0}' is a key field and cannot be ignored.", Name));
            }

            Ignored = true;
            return this;
        }

        /// <summary>
        /// Fluently sets the read-only status of the property.
        /// </summary>
        public PropertyMap ReadOnly()
        {
            if (KeyType != KeyType.NotAKey) {
                throw new ArgumentException(string.Format("'{0}' is a key field and cannot be marked readonly.", Name));
            }

            IsReadOnly = true;
            return this;
        }

        public PropertyMap InsertOnly()
        {
            IsInsertOnly = true;
            return this;
        }
    }

    /// <summary>
    /// Used by ClassMapper to determine which entity property represents the key.
    /// </summary>
    public enum KeyType
    {
        /// <summary>
        /// The property is not a key and is not automatically managed.
        /// </summary>
        NotAKey,

        /// <summary>
        /// The property is an integery-based identity generated from the database.
        /// </summary>
        Identity,

        /// <summary>
        /// The property is a Guid identity which is automatically managed.
        /// </summary>
        Guid,

        /// <summary>
        /// The property is a key that is not automatically managed.
        /// </summary>
        Assigned
    }
}