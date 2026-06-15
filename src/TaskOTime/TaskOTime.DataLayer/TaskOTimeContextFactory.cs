using System;

namespace TaskOTime.DataLayer
{
    public static class TaskOTimeContextFactory
    {
        public static TaskOTimeContext Create()
        {
            return new TaskOTimeContext();
        }

        public static TaskOTimeContext Create(string nameOrConnectionString)
        {
            if (string.IsNullOrWhiteSpace(nameOrConnectionString))
            {
                throw new ArgumentException("A context connection name or connection string is required.", nameof(nameOrConnectionString));
            }

            return new TaskOTimeContext(nameOrConnectionString);
        }
    }
}
