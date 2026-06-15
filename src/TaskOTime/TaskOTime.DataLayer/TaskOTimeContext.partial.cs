using System.Data.Entity;

namespace TaskOTime.DataLayer
{
    public partial class TaskOTimeContext
    {
        public TaskOTimeContext(string nameOrConnectionString)
            : base(nameOrConnectionString)
        {
            ConfigureContext();
        }

        partial void OnContextCreated();

        private void ConfigureContext()
        {
            Configuration.LazyLoadingEnabled = false;
            Configuration.ProxyCreationEnabled = false;
            OnContextCreated();
        }
    }
}
