using System.Text;
using System.Linq;
using System.Collections.Generic;
using System;
using DapperExtensions.Annotations;

namespace DapperExtensions.Mapper
{
    /// <summary>
    /// Automatically maps an entity to a table using a combination of reflection and naming conventions for keys.
    /// </summary>
    public class AutoClassMapper<T> : ClassMapper<T> where T : class
    {
        public AutoClassMapper()
        {
            Type type = typeof(T);

            object[] customAttributes = type.GetCustomAttributes(typeof(TableAttribute), false);
            if (customAttributes != null && customAttributes.Length == 1) {
                TableAttribute att = (TableAttribute)customAttributes[0];
                //return (Attribute)customAttributes[0];

                if (!string.IsNullOrEmpty(att.Schema))
                    Schema(att.Schema);
                if (!string.IsNullOrEmpty(att.Name))
                    Table(att.Name);
            }
            if (string.IsNullOrEmpty(TableName))
                Table(type.Name);
            AutoMap();
        }
    }
}