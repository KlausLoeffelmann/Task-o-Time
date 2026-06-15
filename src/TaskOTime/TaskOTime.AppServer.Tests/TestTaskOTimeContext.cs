using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using TaskOTime.DTOs;

namespace TaskOTime.DataLayer
{
    public static class TaskOTimeContextFactory
    {
        public static TaskOTimeContext Create()
        {
            return new TaskOTimeContext();
        }
    }

    public sealed class TaskOTimeContext : IDisposable
    {
        private readonly List<Func<object, bool>> entitySetTrackers;

        public TaskOTimeContext()
        {
            Category = new TestDbSet<Category>();
            CategorySymbol = new TestDbSet<CategorySymbol>();
            GuidReservation = new TestDbSet<GuidReservation>();
            LogItem = new TestDbSet<LogItem>();
            LookupItem = new TestDbSet<LookupItem>();
            Note = new TestDbSet<Note>();
            Project = new TestDbSet<Project>();
            ProjectUserAssignment = new TestDbSet<ProjectUserAssignment>();
            SharableProject = new TestDbSet<SharableProject>();
            Tag = new TestDbSet<Tag>();
            TaskItem = new TestDbSet<TaskItem>();
            TaskList = new TestDbSet<TaskList>();
            Tenant = new TestDbSet<Tenant>();
            TenantLead = new TestDbSet<TenantLead>();
            TimeItem = new TestDbSet<TimeItem>();
            User = new TestDbSet<User>();
            WebLink = new TestDbSet<WebLink>();

            entitySetTrackers = new List<Func<object, bool>>
            {
                Category.ContainsEntity,
                CategorySymbol.ContainsEntity,
                GuidReservation.ContainsEntity,
                LogItem.ContainsEntity,
                LookupItem.ContainsEntity,
                Note.ContainsEntity,
                Project.ContainsEntity,
                ProjectUserAssignment.ContainsEntity,
                SharableProject.ContainsEntity,
                Tag.ContainsEntity,
                TaskItem.ContainsEntity,
                TaskList.ContainsEntity,
                Tenant.ContainsEntity,
                TenantLead.ContainsEntity,
                TimeItem.ContainsEntity,
                User.ContainsEntity,
                WebLink.ContainsEntity
            };
        }

        public TestDatabase Database { get; } = new TestDatabase();

        public TestDbSet<Category> Category { get; }

        public TestDbSet<CategorySymbol> CategorySymbol { get; }

        public TestDbSet<GuidReservation> GuidReservation { get; }

        public TestDbSet<LogItem> LogItem { get; }

        public TestDbSet<LookupItem> LookupItem { get; }

        public TestDbSet<Note> Note { get; }

        public TestDbSet<Project> Project { get; }

        public TestDbSet<ProjectUserAssignment> ProjectUserAssignment { get; }

        public TestDbSet<SharableProject> SharableProject { get; }

        public TestDbSet<Tag> Tag { get; }

        public TestDbSet<TaskItem> TaskItem { get; }

        public TestDbSet<TaskList> TaskList { get; }

        public TestDbSet<Tenant> Tenant { get; }

        public TestDbSet<TenantLead> TenantLead { get; }

        public TestDbSet<TimeItem> TimeItem { get; }

        public TestDbSet<User> User { get; }

        public TestDbSet<WebLink> WebLink { get; }

        public TestEntityEntry<TEntity> Entry<TEntity>(TEntity entity)
            where TEntity : class
        {
            var isTracked = entitySetTrackers.Any(tracker => tracker(entity));
            return new TestEntityEntry<TEntity>(isTracked ? EntityState.Unchanged : EntityState.Detached);
        }

        public int SaveChanges()
        {
            return 0;
        }

        public void Dispose()
        {
        }
    }

    public sealed class TestDatabase
    {
        public TestDbContextTransaction BeginTransaction()
        {
            return new TestDbContextTransaction();
        }
    }

    public sealed class TestDbContextTransaction : IDisposable
    {
        public bool WasCommitted { get; private set; }

        public void Commit()
        {
            WasCommitted = true;
        }

        public void Dispose()
        {
        }
    }

    public sealed class TestEntityEntry<TEntity>
        where TEntity : class
    {
        public TestEntityEntry(EntityState state)
        {
            State = state;
        }

        public EntityState State { get; set; }

        public TestCollectionEntry<TEntity, TElement> Collection<TElement>(Expression<Func<TEntity, ICollection<TElement>>> navigationProperty)
        {
            return new TestCollectionEntry<TEntity, TElement>();
        }
    }

    public sealed class TestCollectionEntry<TEntity, TElement>
        where TEntity : class
    {
        public void Load()
        {
        }
    }

    public sealed class TestDbSet<TEntity> : IQueryable<TEntity>, IEnumerable<TEntity>
        where TEntity : class
    {
        private readonly List<TEntity> items = new List<TEntity>();

        public Type ElementType => Query.ElementType;

        public Expression Expression => Query.Expression;

        public IQueryProvider Provider => Query.Provider;

        private IQueryable<TEntity> Query => items.AsQueryable();

        public TEntity Add(TEntity entity)
        {
            if (!ContainsEntity(entity))
            {
                items.Add(entity);
            }

            return entity;
        }

        public void AddRange(IEnumerable<TEntity> entities)
        {
            foreach (var entity in entities)
            {
                Add(entity);
            }
        }

        public TEntity Attach(TEntity entity)
        {
            return Add(entity);
        }

        public TEntity Remove(TEntity entity)
        {
            items.Remove(entity);
            return entity;
        }

        public bool ContainsEntity(object entity)
        {
            return items.Any(item => ReferenceEquals(item, entity));
        }

        public IEnumerator<TEntity> GetEnumerator()
        {
            return items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
