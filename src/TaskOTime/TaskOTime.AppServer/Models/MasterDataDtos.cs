using System;
using System.Collections.Generic;

namespace TaskOTime.AppServer.Models
{
    public sealed class ProjectMasterDataDto
    {
        public Guid IdProject { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdSymbol { get; set; }

        public string ProjectName { get; set; }

        public int ProjectType { get; set; }

        public int? ProjectNumber { get; set; }

        public string ProjectDescription { get; set; }

        public string ProjectIdentifier { get; set; }

        public string ProjectSymbolChar { get; set; }

        public int ProjectSymbolColor { get; set; }

        public string ProjectSymbolUrl { get; set; }

        public DateTimeOffset? StartOfProject { get; set; }

        public DateTimeOffset? EndOfProject { get; set; }

        public bool IsActive { get; set; }

        public bool IsDeleted { get; set; }

        public int Scope { get; set; }

        public bool IsSystem { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public string ExternalId { get; set; }
    }

    public sealed class CategoryMasterDataDto
    {
        public Guid IdCategory { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdSymbol { get; set; }

        public string CategoryName { get; set; }

        public string CategoryDescription { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsPublic { get; set; }
    }

    public sealed class CategorySymbolMasterDataDto
    {
        public Guid IdCategorySymbol { get; set; }

        public Guid IdTenant { get; set; }

        public Guid? IdProject { get; set; }

        public string SymbolName { get; set; }

        public string SymbolDescription { get; set; }

        public string SymbolChar { get; set; }

        public string FontName { get; set; }

        public float? FontSize { get; set; }

        public float? SymbolOffsetX { get; set; }

        public float? SymbolOffsetY { get; set; }

        public int SymbolColor { get; set; }
    }

    public sealed class TaskListMasterDataDto
    {
        public Guid IdTaskList { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdProject { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdSymbol { get; set; }

        public string TaskListName { get; set; }

        public string TaskListDescription { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsPublic { get; set; }
    }

    public sealed class TaskItemMasterDataDto
    {
        public Guid IdTaskItem { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid IdProject { get; set; }

        public Guid? IdTaskList { get; set; }

        public Guid? IdSymbol { get; set; }

        public string TaskItemName { get; set; }

        public string TaskItemDescription { get; set; }

        public string QuickInfo { get; set; }

        public DateTimeOffset? DueDate { get; set; }

        public int? TaskHoursBudget { get; set; }

        public int Priority { get; set; }

        public bool IsPrivateTask { get; set; }

        public int Scope { get; set; }

        public bool IsActive { get; set; }

        public bool IsCompleted { get; set; }

        public DateTimeOffset? DateCompleted { get; set; }

        public bool IsDeleted { get; set; }

        public bool IsForeign { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public string ExternalId { get; set; }

        public IReadOnlyList<Guid> IdTagList { get; set; }
    }

    public sealed class TagMasterDataDto
    {
        public Guid IdTag { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public string Tag { get; set; }

        public string Description { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }
    }

    public sealed class NoteMasterDataDto
    {
        public Guid IdNote { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdProject { get; set; }

        public Guid? IdTask { get; set; }

        public Guid? IdSymbol { get; set; }

        public string NoteMnemonic { get; set; }

        public string NoteText { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public string ExternalId { get; set; }

        public IReadOnlyList<Guid> IdTagList { get; set; }
    }

    public sealed class WebLinkMasterDataDto
    {
        public Guid IdWebLink { get; set; }

        public Guid IdTenant { get; set; }

        public Guid IdUser { get; set; }

        public Guid? IdProject { get; set; }

        public Guid? IdSymbol { get; set; }

        public string Link { get; set; }

        public string Domain { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public DateTimeOffset DateCreated { get; set; }

        public DateTimeOffset DateModified { get; set; }

        public string ExternalId { get; set; }

        public IReadOnlyList<Guid> IdTagList { get; set; }
    }
}
