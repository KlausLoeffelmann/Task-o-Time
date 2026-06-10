using System;
using System.Data.Entity;

namespace TaskOTime.DataLayer
{
    public static class DisconnectedEntityExtensions
    {
        public static void AttachAsModified<TEntity>(this TaskOTimeContext context, TEntity entity)
            where TEntity : class
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            context.Set<TEntity>().Attach(entity);
            context.Entry(entity).State = EntityState.Modified;
        }
    }
}
