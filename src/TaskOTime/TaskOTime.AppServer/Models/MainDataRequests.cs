using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class MainDataQueryRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid? IdUser { get; set; }

        public Guid? IdProject { get; set; }

        public Guid? IdTaskList { get; set; }

        public Guid? IdTask { get; set; }

        public Guid? IdSymbol { get; set; }

        public bool IncludeInactive { get; set; }

        public bool IncludeDeleted { get; set; }

        public bool IncludeSystemItems { get; set; }
    }

    public sealed class MainDataItemRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdItem { get; set; }
    }

    public sealed class DeleteMainDataRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdItem { get; set; }

        public bool HardDelete { get; set; }
    }

    public abstract class SaveMainDataRequest<TDto>
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public TDto Item { get; set; }
    }

    public sealed class SaveProjectRequest : SaveMainDataRequest<ProjectMainDataDto>
    {
    }

    public sealed class SaveCategoryRequest : SaveMainDataRequest<CategoryMainDataDto>
    {
    }

    public sealed class SaveCategorySymbolRequest : SaveMainDataRequest<CategorySymbolMainDataDto>
    {
    }

    public sealed class SaveTaskListRequest : SaveMainDataRequest<TaskListMainDataDto>
    {
    }

    public sealed class SaveTaskItemRequest : SaveMainDataRequest<TaskItemMainDataDto>
    {
    }

    public sealed class SaveTagRequest : SaveMainDataRequest<TagMainDataDto>
    {
    }

    public sealed class SaveNoteRequest : SaveMainDataRequest<NoteMainDataDto>
    {
    }

    public sealed class SaveWebLinkRequest : SaveMainDataRequest<WebLinkMainDataDto>
    {
    }

    public sealed class MainDataDeleteResult
    {
        public Guid IdTenant { get; set; }

        public Guid IdItem { get; set; }

        public string EntityName { get; set; }

        public bool Deleted { get; set; }

        public bool HardDeleted { get; set; }

        public DateTimeOffset DeletedAt { get; set; }
    }
}
