using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DapperExtensions.Annotations
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class TableAttribute : Attribute
    {
        public TableAttribute(string name, string schema = null)
        {
            this.Name = name;
            this.Schema = schema;
        }

        public string Name { get; private set; }
        public string Schema { get; private set; }
    }
}
