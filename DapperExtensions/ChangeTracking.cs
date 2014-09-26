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
using System.Threading;

namespace DapperExtensions
{
    public class ChangeTracking
    {
        public interface ISequenceGenerator
        {
            long NextId();
        }

        public class DefaultSequenceGenerator : ISequenceGenerator
        {
            private long lastId;

            public long NextId()
            {
                return Interlocked.Increment(ref lastId);
            }
        }

        public class ReferenceComparer<T> : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer<T> Default = new ReferenceComparer<T>();

            public bool Equals(T x, T y)
            {
                return object.ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return obj.GetHashCode();
            }
        }

        public abstract class TrackedEntity
        {
            public Type EntityType { get; private set; }
            public abstract object EntityRaw { get; }

            public long OperationId { get; private set; }
            public int OperationTypeOrder
            {
                get
                {
                    if (IsDeleted)
                        return 1;
                    if (IsNew)
                        return 2;
                    return 3;
                }
            }
            public bool IsNew { get; protected set; }
            public bool IsDeleted { get; protected set; }
            public bool TakeSnapshot { get; protected set; }
            public abstract bool HasSnapshot { get; }
            public int TrackedRefCount { get; private set; }
            public string KeyName { get; protected set; }

            private readonly ISequenceGenerator sequenceGenerator;

            internal TrackedEntity(Type entityType, ISequenceGenerator sequenceGenerator, string keyName)
            {
                this.EntityType = entityType;
                this.sequenceGenerator = sequenceGenerator;
                this.KeyName = keyName;
            }

            protected void TrackedRefAdded()
            {
                TrackedRefCount++;
            }

            protected void TrackedRefRemoved()
            {
                if (TrackedRefCount <= 0)
                    throw new NotSupportedException("Entity is untracked more times then Tracked");
                TrackedRefCount--;
            }

            protected void NewOperationId()
            {
                this.OperationId = sequenceGenerator.NextId();
            }

            public abstract bool Delete(string keyName = null);
            public abstract bool Reset();
            public abstract bool Revert();
            public abstract void ReAdd(bool isNew);

            public abstract void SaveChanges(IDbConnection connection, IDbTransaction transaction = null, int? commandTimeout = null);
            public abstract Task SaveChangesAsync(IDbConnection connection, IDbTransaction transaction = null, int? commandTimeout = null);
        }

        public class TrackedEntity<T> : TrackedEntity where T : class
        {
            private readonly ChangeTracking owner;
            public ChangeTracking Owner { get { return owner; } }

            public T Entity { get; private set; }
            public override object EntityRaw
            {
                get { return Entity; }
            }
            public override bool HasSnapshot { get { return Snapshot != null; } }
            public Snapshotter.Snapshot Snapshot { get; private set; }

            public bool IsPartialUpdateEnabled { get; private set; }

            private bool useKeyName;

            public TrackedEntity(ChangeTracking owner, T entity, bool isNew, bool takeSnapshot, string keyName)
                : base(typeof(T), owner.SequenceGenerator, keyName)
            {
                this.owner = owner;
                IClassMapper classMap = DapperExtensions.GetMap<T>();
                this.IsPartialUpdateEnabled = !classMap.IsPartialUpdateDisabled;

                this.Entity = entity;
                NewOperationId();
                this.IsNew = isNew;
                this.TakeSnapshot = takeSnapshot;
                if (!isNew && takeSnapshot && IsPartialUpdateEnabled)
                    Snapshot = Snapshotter.Start(entity);
                TrackedRefAdded();
            }

            public override bool Delete(string keyName = null)
            {
                IsDeleted = true;
                NewOperationId();
                this.KeyName = keyName;

                if (IsNew) {
                    TrackedRefRemoved();
                    return TrackedRefCount == 0;
                }
                return false;
            }

            public override void ReAdd(bool isNew)
            {
                if (IsNew != isNew)
                    throw new NotSupportedException("Entity is already tracked in conflicting (New/Attach) status");
                if (IsDeleted) {
                    IsDeleted = false;
                }
                NewOperationId();
                TrackedRefAdded();
            }

            public override void SaveChanges(IDbConnection connection, IDbTransaction transaction = null, int? commandTimeout = null)
            {
                if (IsNew) {
                    if (IsDeleted)
                        return;
                    connection.Insert<T>(Entity, transaction: transaction, commandTimeout: commandTimeout);
                }
                else if (IsDeleted)
                    connection.Delete<T>(Entity, transaction: transaction, commandTimeout: commandTimeout, keyName: KeyName);
                else
                    connection.Update<T>(Entity, transaction: transaction, commandTimeout: commandTimeout, keyName: KeyName, snapshot: Snapshot);
            }

            public override async Task SaveChangesAsync(IDbConnection connection, IDbTransaction transaction = null, int? commandTimeout = null)
            {
                if (IsNew) {
                    if (IsDeleted)
                        return;
                    await connection.InsertAsync<T>(Entity, transaction: transaction, commandTimeout: commandTimeout);
                }
                else if (IsDeleted)
                    await connection.DeleteAsync<T>(Entity, transaction: transaction, commandTimeout: commandTimeout, keyName: KeyName);
                else
                    await connection.UpdateAsync<T>(Entity, transaction: transaction, commandTimeout: commandTimeout, keyName: KeyName, snapshot: Snapshot);
            }

            public override bool Reset()
            {
                if (IsDeleted)
                    return false;
                IsNew = false;
                Snapshot = null;
                if (TakeSnapshot && IsPartialUpdateEnabled)
                    Snapshot = Snapshotter.Start(Entity);
                return true;
            }

            public override bool Revert()
            {
                if (!HasSnapshot)
                    return false;
                Snapshot.Revert();
                return true;
            }
        }

        public ISequenceGenerator SequenceGenerator { get; private set; }
        private readonly Dictionary<object, TrackedEntity> trackedEntities = new Dictionary<object, TrackedEntity>(ReferenceComparer<object>.Default);

        public ChangeTracking(ISequenceGenerator sequenceGenerator = null)
        {
            this.SequenceGenerator = sequenceGenerator ?? new DefaultSequenceGenerator();
        }

        public ChangeTrackingCollection<T> CreateTrackingCollection<T>() where T : class
        {
            var chtc = new ChangeTrackingCollection<T>(this);
            return chtc;
        }

        public TrackedEntity<T> Get<T>(T entity) where T : class
        {
            if (entity == null)
                return null;
            TrackedEntity t;
            if (trackedEntities.TryGetValue(entity, out t))
                return t as TrackedEntity<T>;
            return null;
        }

        public ChangeTrackingCollection<T> AttachCollection<T>(IEnumerable<T> entities, bool takeSnapshot = true) where T : class
        {
            var chtc = new ChangeTrackingCollection<T>(this);
            if (entities != null) {
                foreach (var obj in entities)
                    chtc.Attach(obj, takeSnapshot);
            }
            return chtc;
        }

        public ChangeTrackingCollection<T> AddNewCollection<T>(IEnumerable<T> entities) where T : class
        {
            if (entities == null)
                return null;
            var chtc = new ChangeTrackingCollection<T>(this);
            if (entities != null) {
                foreach (var obj in entities)
                    chtc.Add(obj);
            }
            return chtc;
        }

        public void AttachRange<T>(IEnumerable<T> entities, bool takeSnapshot = true, string keyName = null) where T : class
        {
            if (entities == null)
                return;
            foreach (var obj in entities)
                Add<T>(obj, false, takeSnapshot, keyName);
        }

        public TrackedEntity<T> Attach<T>(T entity, bool takeSnapshot = true, string keyName = null) where T : class
        {
            return Add<T>(entity, false, takeSnapshot, keyName);
        }

        public void AddNewRange<T>(IEnumerable<T> entities) where T : class
        {
            if (entities == null)
                return;
            foreach (var obj in entities)
                Add<T>(obj, true);
        }

        public TrackedEntity<T> AddNew<T>(T entity) where T : class
        {
            return Add<T>(entity, true);
        }

        public void AddRange<T>(IEnumerable<T> entities, bool isNew, bool takeSnapshot = true, string keyName = null) where T : class
        {
            if (entities == null)
                return;
            foreach (var obj in entities)
                Add<T>(obj, isNew, takeSnapshot, keyName);
        }

        public TrackedEntity<T> Add<T>(T entity, bool isNew, bool takeSnapshot = true, string keyName = null) where T : class
        {
            if (entity == null)
                return null;
            TrackedEntity<T> t = Get<T>(entity);
            if (t == null) {
                t = new TrackedEntity<T>(this, entity, isNew, takeSnapshot, keyName);
                trackedEntities.Add(entity, t);
            }
            else {
                t.ReAdd(isNew);
            }
            return t;
        }

        public void DeleteRange<T>(IEnumerable<T> entities, string keyName = null) where T : class
        {
            if (entities == null)
                return;
            foreach (var obj in entities)
                Delete<T>(obj, keyName);
        }

        public void Delete<T>(T entity, string keyName = null) where T : class
        {
            if (entity == null)
                return;
            TrackedEntity<T> t = Get<T>(entity);
            if (t == null)
                t = Add<T>(entity, false, keyName: keyName);
            if (t.Delete(keyName))
                DetachTracked(t);
        }

        public void DeleteTracked(TrackedEntity tracked)
        {
            if (tracked == null)
                return;
            if (tracked.Delete())
                DetachTracked(tracked);
        }

        public void DeleteAll()
        {
            foreach (var t in trackedEntities.Values.ToList()) {
                if (t.Delete())
                    DetachTracked(t);
            }
        }

        public bool Detach<T>(T entity) where T : class
        {
            if (entity == null)
                return true;
            var t = Get(entity);
            if (t != null)
                return DetachTracked(t);
            return false;
        }

        public bool DetachTracked(TrackedEntity tracked)
        {
            if (tracked == null)
                return true;
            return trackedEntities.Remove(tracked.EntityRaw);
        }

        public void Reset()
        {
            foreach (var t in trackedEntities.Values.ToList())
                ResetTracked(t);
        }

        public void Reset<T>(T entity) where T : class
        {
            if (entity == null)
                return;
            var t = Get(entity);
            if (t != null)
                ResetTracked(t);
        }


        public void ResetTracked(TrackedEntity tracked)
        {
            if (tracked == null)
                return;
            if (!tracked.Reset())
                DetachTracked(tracked);
        }


        public void Revert()
        {
            foreach (var t in trackedEntities.Values.ToList())
                t.Revert();
        }

        public void Revert(object entity)
        {
            TrackedEntity ent = null;
            if (trackedEntities.TryGetValue(entity, out ent)) {
                ent.Revert();
            }
        }

        public void Clear()
        {
            trackedEntities.Clear();
        }

        public void SaveChanges(IDbConnection connection, IDbTransaction transaction = null, int? commandTimeout = null, bool resetAfter = true)
        {
            var entities = trackedEntities.Values.OrderBy(i => i.OperationTypeOrder).ThenBy(i => i.OperationId).ToList();

            for (int i = 0; i < entities.Count; i++) {
                var t = entities[i];
                t.SaveChanges(connection, transaction: transaction, commandTimeout: commandTimeout);
            }
            if (resetAfter)
                Reset();
        }

        public async Task SaveChangesAsync(IDbConnection connection, IDbTransaction transaction = null, int? commandTimeout = null, bool resetAfter = true)
        {
            var entities = trackedEntities.Values.OrderBy(i => i.OperationTypeOrder).ThenBy(i => i.OperationId).ToList();

            for (int i = 0; i < entities.Count; i++) {
                var t = entities[i];
                await t.SaveChangesAsync(connection, transaction: transaction, commandTimeout: commandTimeout);
            }
            if (resetAfter)
                Reset();
        }

        public IEnumerable<TrackedEntity> TrackedEntities
        {
            get { return trackedEntities.Values; }
        }
    }
}
