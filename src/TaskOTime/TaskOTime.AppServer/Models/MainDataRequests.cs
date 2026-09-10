using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class MasterDataQueryRequest
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

    public sealed class MasterDataItemRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdItem { get; set; }
    }

    public sealed class DeleteMasterDataRequest
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public Guid IdItem { get; set; }

        public bool HardDelete { get; set; }
    }

    public abstract class SaveMasterDataRequest<TDto>
    {
        public Guid IdTenant { get; set; }

        public Guid IdActingUser { get; set; }

        public TDto Item { get; set; }
    }

    public sealed class SaveProjectRequest : SaveMasterDataRequest<ProjectMainDataDto>
    {
    }

    public sealed class SaveCategoryRequest : SaveMasterDataRequest<CategoryMasterDataDto>
    {
    }

    public sealed class SaveCategorySymbolRequest : SaveMasterDataRequest<CategorySymbolMasterDataDto>
    {
    }

    public sealed class SaveTaskListRequest : SaveMasterDataRequest<TaskListMasterDataDto>
    {
    }

    public sealed class SaveTaskItemRequest : SaveMasterDataRequest<TaskItemMasterDataDto>
    {
    }

    public sealed class SaveTagRequest : SaveMasterDataRequest<TagMasterDataDto>
    {
    }

    public sealed class SaveNoteRequest : SaveMasterDataRequest<NoteMasterDataDto>
    {
    }

    public sealed class SaveWebLinkRequest : SaveMasterDataRequest<WebLinkMasterDataDto>
    {
    }

    public sealed class MasterDataDeleteResult
    {
        public Guid IdTenant { get; set; }

        public Guid IdItem { get; set; }

        public string EntityName { get; set; }

        public bool Deleted { get; set; }

        public bool HardDeleted { get; set; }

        public DateTimeOffset DeletedAt { get; set; }
    }
}
