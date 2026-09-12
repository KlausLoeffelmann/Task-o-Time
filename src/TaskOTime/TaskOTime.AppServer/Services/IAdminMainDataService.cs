using System.Collections.Generic;
using TaskOTime.AppServer.Models;

namespace TaskOTime.AppServer.Services
{
    public interface IAdminMainDataService
    {
        ServiceResult<TenantDto> GetTenant(GetTenantRequest request);

        ServiceResult<TenantDto> UpdateTenant(UpdateTenantRequest request);

        ServiceResult<IReadOnlyList<ProjectMainDataDto>> GetProjects(MainDataQueryRequest request);

        ServiceResult<ProjectMainDataDto> GetProject(MainDataItemRequest request);

        ServiceResult<ProjectMainDataDto> CreateProject(SaveProjectRequest request);

        ServiceResult<ProjectMainDataDto> UpdateProject(SaveProjectRequest request);

        ServiceResult<MainDataDeleteResult> DeleteProject(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<CategoryMainDataDto>> GetCategories(MainDataQueryRequest request);

        ServiceResult<CategoryMainDataDto> GetCategory(MainDataItemRequest request);

        ServiceResult<CategoryMainDataDto> CreateCategory(SaveCategoryRequest request);

        ServiceResult<CategoryMainDataDto> UpdateCategory(SaveCategoryRequest request);

        ServiceResult<MainDataDeleteResult> DeleteCategory(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<CategorySymbolMainDataDto>> GetCategorySymbols(MainDataQueryRequest request);

        ServiceResult<CategorySymbolMainDataDto> GetCategorySymbol(MainDataItemRequest request);

        ServiceResult<CategorySymbolMainDataDto> CreateCategorySymbol(SaveCategorySymbolRequest request);

        ServiceResult<CategorySymbolMainDataDto> UpdateCategorySymbol(SaveCategorySymbolRequest request);

        ServiceResult<MainDataDeleteResult> DeleteCategorySymbol(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<TaskListMainDataDto>> GetTaskLists(MainDataQueryRequest request);

        ServiceResult<TaskListMainDataDto> GetTaskList(MainDataItemRequest request);

        ServiceResult<TaskListMainDataDto> CreateTaskList(SaveTaskListRequest request);

        ServiceResult<TaskListMainDataDto> UpdateTaskList(SaveTaskListRequest request);

        ServiceResult<MainDataDeleteResult> DeleteTaskList(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<TaskItemMainDataDto>> GetTaskItems(MainDataQueryRequest request);

        ServiceResult<TaskItemMainDataDto> GetTaskItem(MainDataItemRequest request);

        ServiceResult<TaskItemMainDataDto> CreateTaskItem(SaveTaskItemRequest request);

        ServiceResult<TaskItemMainDataDto> UpdateTaskItem(SaveTaskItemRequest request);

        ServiceResult<MainDataDeleteResult> DeleteTaskItem(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<TagMainDataDto>> GetTags(MainDataQueryRequest request);

        ServiceResult<TagMainDataDto> GetTag(MainDataItemRequest request);

        ServiceResult<TagMainDataDto> CreateTag(SaveTagRequest request);

        ServiceResult<TagMainDataDto> UpdateTag(SaveTagRequest request);

        ServiceResult<MainDataDeleteResult> DeleteTag(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<NoteMainDataDto>> GetNotes(MainDataQueryRequest request);

        ServiceResult<NoteMainDataDto> GetNote(MainDataItemRequest request);

        ServiceResult<NoteMainDataDto> CreateNote(SaveNoteRequest request);

        ServiceResult<NoteMainDataDto> UpdateNote(SaveNoteRequest request);

        ServiceResult<MainDataDeleteResult> DeleteNote(DeleteMainDataRequest request);

        ServiceResult<IReadOnlyList<WebLinkMainDataDto>> GetWebLinks(MainDataQueryRequest request);

        ServiceResult<WebLinkMainDataDto> GetWebLink(MainDataItemRequest request);

        ServiceResult<WebLinkMainDataDto> CreateWebLink(SaveWebLinkRequest request);

        ServiceResult<WebLinkMainDataDto> UpdateWebLink(SaveWebLinkRequest request);

        ServiceResult<MainDataDeleteResult> DeleteWebLink(DeleteMainDataRequest request);
    }
}
