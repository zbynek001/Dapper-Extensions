using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Data.SqlClient;
using System.Data;
using Dapper;
using DapperExtensions.Mapper;

namespace DapperExtensions
{
    public class ChangeTrackingCollection<T> : Collection<T>
        where T : class
    {
        private readonly ChangeTracking changeTracking;
        private bool internalActivity;

        internal ChangeTrackingCollection(ChangeTracking changeTracking)
        {
            this.changeTracking = changeTracking;
        }

        public void Attach(T item, bool takeSnapshot = true)
        {
            internalActivity = true;
            try {
                changeTracking.Attach(item, takeSnapshot);
                this.Add(item);
            }
            finally {
                internalActivity = false;
            }
        }

        public void Attach(IEnumerable<T> entities, bool takeSnapshot = true)
        {
            if (entities == null)
                return;
            foreach (var obj in entities)
                Attach(obj, takeSnapshot);
        }

        protected override void InsertItem(int index, T item)
        {
            if (!internalActivity)
                changeTracking.AddNew(item);
            base.InsertItem(index, item);
        }

        protected override void RemoveItem(int index)
        {
            if (!internalActivity) {
                var entity = this[index];
                changeTracking.Delete(entity);
            }
            base.RemoveItem(index);
        }

        protected override void SetItem(int index, T item)
        {
            if (!internalActivity) {
                var entityOld = this[index];
                changeTracking.Delete(entityOld);
                changeTracking.AddNew(item);
            }
            base.SetItem(index, item);
        }

        protected override void ClearItems()
        {
            if (!internalActivity)
                changeTracking.DeleteAll();
            base.ClearItems();
        }
    }
}
