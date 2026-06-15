using System.Collections.Generic;
using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface IAdminMasterDataService
    {
        ServiceResult<IReadOnlyList<ProjectMasterDataDto>> GetProjects(MasterDataQueryRequest request);

        ServiceResult<ProjectMasterDataDto> GetProject(MasterDataItemRequest request);

        ServiceResult<ProjectMasterDataDto> CreateProject(SaveProjectRequest request);

        ServiceResult<ProjectMasterDataDto> UpdateProject(SaveProjectRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteProject(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<CategoryMasterDataDto>> GetCategories(MasterDataQueryRequest request);

        ServiceResult<CategoryMasterDataDto> GetCategory(MasterDataItemRequest request);

        ServiceResult<CategoryMasterDataDto> CreateCategory(SaveCategoryRequest request);

        ServiceResult<CategoryMasterDataDto> UpdateCategory(SaveCategoryRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteCategory(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<CategorySymbolMasterDataDto>> GetCategorySymbols(MasterDataQueryRequest request);

        ServiceResult<CategorySymbolMasterDataDto> GetCategorySymbol(MasterDataItemRequest request);

        ServiceResult<CategorySymbolMasterDataDto> CreateCategorySymbol(SaveCategorySymbolRequest request);

        ServiceResult<CategorySymbolMasterDataDto> UpdateCategorySymbol(SaveCategorySymbolRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteCategorySymbol(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<TaskListMasterDataDto>> GetTaskLists(MasterDataQueryRequest request);

        ServiceResult<TaskListMasterDataDto> GetTaskList(MasterDataItemRequest request);

        ServiceResult<TaskListMasterDataDto> CreateTaskList(SaveTaskListRequest request);

        ServiceResult<TaskListMasterDataDto> UpdateTaskList(SaveTaskListRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteTaskList(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<TaskItemMasterDataDto>> GetTaskItems(MasterDataQueryRequest request);

        ServiceResult<TaskItemMasterDataDto> GetTaskItem(MasterDataItemRequest request);

        ServiceResult<TaskItemMasterDataDto> CreateTaskItem(SaveTaskItemRequest request);

        ServiceResult<TaskItemMasterDataDto> UpdateTaskItem(SaveTaskItemRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteTaskItem(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<TagMasterDataDto>> GetTags(MasterDataQueryRequest request);

        ServiceResult<TagMasterDataDto> GetTag(MasterDataItemRequest request);

        ServiceResult<TagMasterDataDto> CreateTag(SaveTagRequest request);

        ServiceResult<TagMasterDataDto> UpdateTag(SaveTagRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteTag(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<NoteMasterDataDto>> GetNotes(MasterDataQueryRequest request);

        ServiceResult<NoteMasterDataDto> GetNote(MasterDataItemRequest request);

        ServiceResult<NoteMasterDataDto> CreateNote(SaveNoteRequest request);

        ServiceResult<NoteMasterDataDto> UpdateNote(SaveNoteRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteNote(DeleteMasterDataRequest request);

        ServiceResult<IReadOnlyList<WebLinkMasterDataDto>> GetWebLinks(MasterDataQueryRequest request);

        ServiceResult<WebLinkMasterDataDto> GetWebLink(MasterDataItemRequest request);

        ServiceResult<WebLinkMasterDataDto> CreateWebLink(SaveWebLinkRequest request);

        ServiceResult<WebLinkMasterDataDto> UpdateWebLink(SaveWebLinkRequest request);

        ServiceResult<MasterDataDeleteResult> DeleteWebLink(DeleteMasterDataRequest request);
    }
}
