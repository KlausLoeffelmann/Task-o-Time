using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public sealed class AdminMasterDataService : ApplicationServiceBase, IAdminMasterDataService
    {
        public AdminMasterDataService()
        {
        }

        public AdminMasterDataService(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
            : base(contextFactory, passwordHasher)
        {
        }

        public ServiceResult<IReadOnlyList<ProjectMainDataDto>> GetProjects(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<ProjectMainDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<ProjectMainDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.Project.Where(p => p.IdTenant == request.IdTenant);
                if (!request.IncludeInactive)
                {
                    query = query.Where(p => p.IsActive);
                }

                if (!request.IncludeDeleted)
                {
                    query = query.Where(p => !p.IsDeleted);
                }

                if (!request.IncludeSystemItems)
                {
                    query = query.Where(p => !p.IsSystem);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(p => p.IdUser == request.IdUser.Value);
                }

                var projects = query
                    .OrderBy(p => p.ProjectName)
                    .ThenBy(p => p.ProjectIdentifier)
                    .ToList()
                    .Select(ToProjectMasterDataDto)
                    .ToList();

                return ServiceResult<IReadOnlyList<ProjectMainDataDto>>.Ok(projects);
            }
        }

        public ServiceResult<ProjectMainDataDto> GetProject(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<ProjectMainDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<ProjectMainDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var project = context.Project.SingleOrDefault(p => p.IdTenant == request.IdTenant && p.IdProject == request.IdItem);
                if (project == null)
                {
                    return ServiceResult<ProjectMainDataDto>.Fail("ProjectNotFound", "The tenant project was not found.");
                }

                return ServiceResult<ProjectMainDataDto>.Ok(ToProjectMasterDataDto(project));
            }
        }

        public ServiceResult<ProjectMainDataDto> CreateProject(SaveProjectRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<ProjectMainDataDto>.Fail("InvalidRequest", "A project item is required.");
            }

            try
            {
                var projectName = NormalizeRequired(request.Item.ProjectName, nameof(request.Item.ProjectName));
                using (var context = CreateContext())
                using (var transaction = context.Database.BeginTransaction())
                {
                    var authorization = Authorize<ProjectMainDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<ProjectMainDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var now = DateTimeOffset.UtcNow;
                    var project = new Project
                    {
                        IdProject = request.Item.IdProject == Guid.Empty ? Guid.NewGuid() : request.Item.IdProject,
                        IdTenant = request.IdTenant,
                        IdUser = request.IdActingUser,
                        IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol),
                        ProjectName = projectName,
                        ProjectType = request.Item.ProjectType,
                        ProjectNumber = request.Item.ProjectNumber,
                        ProjectDescription = TrimOrNull(request.Item.ProjectDescription),
                        ProjectIdentifier = TrimOrNull(request.Item.ProjectIdentifier),
                        ProjectSymbolChar = TrimOrNull(request.Item.ProjectSymbolChar),
                        ProjectSymbolColor = request.Item.ProjectSymbolColor,
                        ProjectSymbolUrl = TrimOrNull(request.Item.ProjectSymbolUrl),
                        StartOfProject = request.Item.StartOfProject,
                        EndOfProject = request.Item.EndOfProject,
                        IsActive = true,
                        IsDeleted = false,
                        Scope = request.Item.Scope,
                        IsSystem = request.Item.IsSystem,
                        DateCreated = now,
                        DateModified = now,
                        SyncId = Guid.NewGuid(),
                        SyncStatus = 0,
                        ExternalId = TrimOrNull(request.Item.ExternalId)
                    };

                    context.Project.Add(project);
                    EnsureOwnerAssignment(context, project, request.IdActingUser, now);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<ProjectMainDataDto>.Ok(ToProjectMasterDataDto(project));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<ProjectMainDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<ProjectMainDataDto> UpdateProject(SaveProjectRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<ProjectMainDataDto>.Fail("InvalidRequest", "A project item is required.");
            }

            try
            {
                var projectName = NormalizeRequired(request.Item.ProjectName, nameof(request.Item.ProjectName));
                using (var context = CreateContext())
                using (var transaction = context.Database.BeginTransaction())
                {
                    var authorization = Authorize<ProjectMainDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    if (request.Item.IdProject == Guid.Empty)
                    {
                        return ServiceResult<ProjectMainDataDto>.Fail("InvalidRequest", "A project id is required.");
                    }

                    var project = context.Project.SingleOrDefault(p => p.IdTenant == request.IdTenant && p.IdProject == request.Item.IdProject);
                    if (project == null)
                    {
                        return ServiceResult<ProjectMainDataDto>.Fail("ProjectNotFound", "The tenant project was not found.");
                    }

                    var ownerResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!ownerResult.Success)
                    {
                        return ServiceResult<ProjectMainDataDto>.Fail(ownerResult.ErrorCode, ownerResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<ProjectMainDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var now = DateTimeOffset.UtcNow;
                    project.IdUser = ownerResult.Value;
                    project.IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol);
                    project.ProjectName = projectName;
                    project.ProjectType = request.Item.ProjectType;
                    project.ProjectNumber = request.Item.ProjectNumber;
                    project.ProjectDescription = TrimOrNull(request.Item.ProjectDescription);
                    project.ProjectIdentifier = TrimOrNull(request.Item.ProjectIdentifier);
                    project.ProjectSymbolChar = TrimOrNull(request.Item.ProjectSymbolChar);
                    project.ProjectSymbolColor = request.Item.ProjectSymbolColor;
                    project.ProjectSymbolUrl = TrimOrNull(request.Item.ProjectSymbolUrl);
                    project.StartOfProject = request.Item.StartOfProject;
                    project.EndOfProject = request.Item.EndOfProject;
                    project.IsDeleted = request.Item.IsDeleted;
                    project.IsActive = request.Item.IsDeleted ? false : request.Item.IsActive;
                    project.Scope = request.Item.Scope;
                    project.IsSystem = request.Item.IsSystem;
                    project.DateModified = now;
                    project.ExternalId = TrimOrNull(request.Item.ExternalId);

                    EnsureOwnerAssignment(context, project, request.IdActingUser, now);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<ProjectMainDataDto>.Ok(ToProjectMasterDataDto(project));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<ProjectMainDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteProject(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var project = context.Project.SingleOrDefault(p => p.IdTenant == request.IdTenant && p.IdProject == request.IdItem);
                if (project == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("ProjectNotFound", "The tenant project was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                if (request.HardDelete)
                {
                    var dependency = GetProjectDependency(context, project.IdProject);
                    if (dependency != null)
                    {
                        return ServiceResult<MasterDataDeleteResult>.Fail("DeleteWouldBreakRelationships", dependency);
                    }

                    context.Project.Remove(project);
                    context.SaveChanges();
                    return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, project.IdProject, "Project", true, true, now));
                }

                project.IsDeleted = true;
                project.IsActive = false;
                project.DateModified = now;
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, project.IdProject, "Project", true, false, now));
            }
        }

        public ServiceResult<IReadOnlyList<CategoryMasterDataDto>> GetCategories(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<CategoryMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<CategoryMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.Category.Where(c => c.User.IdTenant == request.IdTenant);
                if (!request.IncludeInactive)
                {
                    query = query.Where(c => c.User.IsActive);
                }

                if (!request.IncludeDeleted)
                {
                    query = query.Where(c => !c.User.IsDeleted);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(c => c.IdUser == request.IdUser.Value);
                }

                var categories = query
                    .OrderBy(c => c.DisplayOrder)
                    .ThenBy(c => c.CategoryName)
                    .ToList()
                    .Select(c => ToCategoryDto(c, request.IdTenant))
                    .ToList();

                return ServiceResult<IReadOnlyList<CategoryMasterDataDto>>.Ok(categories);
            }
        }

        public ServiceResult<CategoryMasterDataDto> GetCategory(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<CategoryMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<CategoryMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var category = context.Category.SingleOrDefault(c => c.IdCategory == request.IdItem && c.User.IdTenant == request.IdTenant);
                if (category == null)
                {
                    return ServiceResult<CategoryMasterDataDto>.Fail("CategoryNotFound", "The tenant category was not found.");
                }

                return ServiceResult<CategoryMasterDataDto>.Ok(ToCategoryDto(category, request.IdTenant));
            }
        }

        public ServiceResult<CategoryMasterDataDto> CreateCategory(SaveCategoryRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<CategoryMasterDataDto>.Fail("InvalidRequest", "A category item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.CategoryName, nameof(request.Item.CategoryName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<CategoryMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<CategoryMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<CategoryMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var category = new Category
                    {
                        IdCategory = request.Item.IdCategory == Guid.Empty ? Guid.NewGuid() : request.Item.IdCategory,
                        IdUser = userResult.Value,
                        IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol),
                        CategoryName = name,
                        CategoryDescription = TrimOrNull(request.Item.CategoryDescription),
                        DisplayOrder = request.Item.DisplayOrder,
                        IsPublic = request.Item.IsPublic
                    };

                    context.Category.Add(category);
                    context.SaveChanges();
                    return ServiceResult<CategoryMasterDataDto>.Ok(ToCategoryDto(category, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<CategoryMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<CategoryMasterDataDto> UpdateCategory(SaveCategoryRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<CategoryMasterDataDto>.Fail("InvalidRequest", "A category item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.CategoryName, nameof(request.Item.CategoryName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<CategoryMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var category = context.Category.SingleOrDefault(c => c.IdCategory == request.Item.IdCategory && c.User.IdTenant == request.IdTenant);
                    if (category == null)
                    {
                        return ServiceResult<CategoryMasterDataDto>.Fail("CategoryNotFound", "The tenant category was not found.");
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<CategoryMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<CategoryMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    category.IdUser = userResult.Value;
                    category.IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol);
                    category.CategoryName = name;
                    category.CategoryDescription = TrimOrNull(request.Item.CategoryDescription);
                    category.DisplayOrder = request.Item.DisplayOrder;
                    category.IsPublic = request.Item.IsPublic;
                    context.SaveChanges();

                    return ServiceResult<CategoryMasterDataDto>.Ok(ToCategoryDto(category, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<CategoryMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteCategory(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var category = context.Category.SingleOrDefault(c => c.IdCategory == request.IdItem && c.User.IdTenant == request.IdTenant);
                if (category == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("CategoryNotFound", "The tenant category was not found.");
                }

                if (context.TimeItem.Any(t => t.IdCategory == category.IdCategory) ||
                    context.SharableProject.Any(s => s.IdDefaultCategory == category.IdCategory))
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("DeleteWouldBreakRelationships", "The category is used by time items or shared projects.");
                }

                var now = DateTimeOffset.UtcNow;
                context.Category.Remove(category);
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, category.IdCategory, "Category", true, true, now));
            }
        }

        public ServiceResult<IReadOnlyList<CategorySymbolMasterDataDto>> GetCategorySymbols(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<CategorySymbolMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<CategorySymbolMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.CategorySymbol.Where(s =>
                    (s.IdProject.HasValue && s.Project.IdTenant == request.IdTenant) ||
                    (!s.IdProject.HasValue && request.IncludeSystemItems));

                if (!request.IncludeInactive)
                {
                    query = query.Where(s => !s.IdProject.HasValue || s.Project.IsActive);
                }

                if (!request.IncludeDeleted)
                {
                    query = query.Where(s => !s.IdProject.HasValue || !s.Project.IsDeleted);
                }

                if (request.IdProject.HasValue)
                {
                    query = query.Where(s => s.IdProject == request.IdProject.Value);
                }

                var symbols = query
                    .OrderBy(s => s.SymbolName)
                    .ThenBy(s => s.SymbolChar)
                    .ToList()
                    .Select(s => ToCategorySymbolDto(s, request.IdTenant))
                    .ToList();

                return ServiceResult<IReadOnlyList<CategorySymbolMasterDataDto>>.Ok(symbols);
            }
        }

        public ServiceResult<CategorySymbolMasterDataDto> GetCategorySymbol(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<CategorySymbolMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<CategorySymbolMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var symbol = context.CategorySymbol.SingleOrDefault(s =>
                    s.IdCategorySymbol == request.IdItem &&
                    (!s.IdProject.HasValue || s.Project.IdTenant == request.IdTenant));
                if (symbol == null)
                {
                    return ServiceResult<CategorySymbolMasterDataDto>.Fail("CategorySymbolNotFound", "The tenant category symbol was not found.");
                }

                return ServiceResult<CategorySymbolMasterDataDto>.Ok(ToCategorySymbolDto(symbol, request.IdTenant));
            }
        }

        public ServiceResult<CategorySymbolMasterDataDto> CreateCategorySymbol(SaveCategorySymbolRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<CategorySymbolMasterDataDto>.Fail("InvalidRequest", "A category symbol item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.SymbolName, nameof(request.Item.SymbolName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<CategorySymbolMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    if (!request.Item.IdProject.HasValue || request.Item.IdProject.Value == Guid.Empty)
                    {
                        return ServiceResult<CategorySymbolMasterDataDto>.Fail("ProjectRequired", "Category symbols created by tenant admins must belong to a tenant project.");
                    }

                    var projectResult = GetTenantProject(context, request.IdTenant, request.Item.IdProject.Value);
                    if (!projectResult.Success)
                    {
                        return ServiceResult<CategorySymbolMasterDataDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                    }

                    var symbol = new CategorySymbol
                    {
                        IdCategorySymbol = request.Item.IdCategorySymbol == Guid.Empty ? Guid.NewGuid() : request.Item.IdCategorySymbol,
                        IdProject = projectResult.Value.IdProject,
                        SymbolName = name,
                        SymbolDescription = TrimOrNull(request.Item.SymbolDescription),
                        SymbolChar = TrimOrNull(request.Item.SymbolChar),
                        FontName = TrimOrNull(request.Item.FontName),
                        FontSize = request.Item.FontSize,
                        SymbolOffsetX = request.Item.SymbolOffsetX,
                        SymbolOffsetY = request.Item.SymbolOffsetY,
                        SymbolColor = request.Item.SymbolColor
                    };

                    context.CategorySymbol.Add(symbol);
                    context.SaveChanges();
                    return ServiceResult<CategorySymbolMasterDataDto>.Ok(ToCategorySymbolDto(symbol, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<CategorySymbolMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<CategorySymbolMasterDataDto> UpdateCategorySymbol(SaveCategorySymbolRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<CategorySymbolMasterDataDto>.Fail("InvalidRequest", "A category symbol item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.SymbolName, nameof(request.Item.SymbolName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<CategorySymbolMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var symbol = context.CategorySymbol.SingleOrDefault(s =>
                        s.IdCategorySymbol == request.Item.IdCategorySymbol &&
                        s.IdProject.HasValue &&
                        s.Project.IdTenant == request.IdTenant);
                    if (symbol == null)
                    {
                        return ServiceResult<CategorySymbolMasterDataDto>.Fail("CategorySymbolNotFound", "The tenant category symbol was not found or is not tenant-scoped.");
                    }

                    if (!request.Item.IdProject.HasValue || request.Item.IdProject.Value == Guid.Empty)
                    {
                        return ServiceResult<CategorySymbolMasterDataDto>.Fail("ProjectRequired", "Category symbols updated by tenant admins must belong to a tenant project.");
                    }

                    var projectResult = GetTenantProject(context, request.IdTenant, request.Item.IdProject.Value);
                    if (!projectResult.Success)
                    {
                        return ServiceResult<CategorySymbolMasterDataDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                    }

                    symbol.IdProject = projectResult.Value.IdProject;
                    symbol.SymbolName = name;
                    symbol.SymbolDescription = TrimOrNull(request.Item.SymbolDescription);
                    symbol.SymbolChar = TrimOrNull(request.Item.SymbolChar);
                    symbol.FontName = TrimOrNull(request.Item.FontName);
                    symbol.FontSize = request.Item.FontSize;
                    symbol.SymbolOffsetX = request.Item.SymbolOffsetX;
                    symbol.SymbolOffsetY = request.Item.SymbolOffsetY;
                    symbol.SymbolColor = request.Item.SymbolColor;
                    context.SaveChanges();

                    return ServiceResult<CategorySymbolMasterDataDto>.Ok(ToCategorySymbolDto(symbol, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<CategorySymbolMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteCategorySymbol(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var symbol = context.CategorySymbol.SingleOrDefault(s =>
                    s.IdCategorySymbol == request.IdItem &&
                    s.IdProject.HasValue &&
                    s.Project.IdTenant == request.IdTenant);
                if (symbol == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("CategorySymbolNotFound", "The tenant category symbol was not found or is not tenant-scoped.");
                }

                if (HasCategorySymbolDependencies(context, symbol.IdCategorySymbol))
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("DeleteWouldBreakRelationships", "The category symbol is still referenced by tenant data.");
                }

                var now = DateTimeOffset.UtcNow;
                context.CategorySymbol.Remove(symbol);
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, symbol.IdCategorySymbol, "CategorySymbol", true, true, now));
            }
        }

        public ServiceResult<IReadOnlyList<TaskListMasterDataDto>> GetTaskLists(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<TaskListMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<TaskListMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.TaskList.Where(l => l.Project.IdTenant == request.IdTenant);
                if (!request.IncludeInactive)
                {
                    query = query.Where(l => l.Project.IsActive);
                }

                if (!request.IncludeDeleted)
                {
                    query = query.Where(l => !l.Project.IsDeleted);
                }

                if (request.IdProject.HasValue)
                {
                    query = query.Where(l => l.IdProject == request.IdProject.Value);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(l => l.IdUser == request.IdUser.Value);
                }

                var taskLists = query
                    .OrderBy(l => l.IdProject)
                    .ThenBy(l => l.DisplayOrder)
                    .ThenBy(l => l.TaskListName)
                    .ToList()
                    .Select(l => ToTaskListDto(l, request.IdTenant))
                    .ToList();

                return ServiceResult<IReadOnlyList<TaskListMasterDataDto>>.Ok(taskLists);
            }
        }

        public ServiceResult<TaskListMasterDataDto> GetTaskList(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TaskListMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<TaskListMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var taskList = context.TaskList.SingleOrDefault(l => l.IdTaskList == request.IdItem && l.Project.IdTenant == request.IdTenant);
                if (taskList == null)
                {
                    return ServiceResult<TaskListMasterDataDto>.Fail("TaskListNotFound", "The tenant task list was not found.");
                }

                return ServiceResult<TaskListMasterDataDto>.Ok(ToTaskListDto(taskList, request.IdTenant));
            }
        }

        public ServiceResult<TaskListMasterDataDto> CreateTaskList(SaveTaskListRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TaskListMasterDataDto>.Fail("InvalidRequest", "A task list item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.TaskListName, nameof(request.Item.TaskListName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<TaskListMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var projectResult = GetTenantProject(context, request.IdTenant, request.Item.IdProject);
                    if (!projectResult.Success)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var taskList = new TaskList
                    {
                        IdTaskList = request.Item.IdTaskList == Guid.Empty ? Guid.NewGuid() : request.Item.IdTaskList,
                        IdProject = projectResult.Value.IdProject,
                        IdUser = userResult.Value,
                        IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol),
                        TaskListName = name,
                        TaskListDescription = TrimOrNull(request.Item.TaskListDescription),
                        DisplayOrder = request.Item.DisplayOrder,
                        IsPublic = request.Item.IsPublic
                    };

                    context.TaskList.Add(taskList);
                    context.SaveChanges();
                    return ServiceResult<TaskListMasterDataDto>.Ok(ToTaskListDto(taskList, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TaskListMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<TaskListMasterDataDto> UpdateTaskList(SaveTaskListRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TaskListMasterDataDto>.Fail("InvalidRequest", "A task list item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.TaskListName, nameof(request.Item.TaskListName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<TaskListMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var taskList = context.TaskList.SingleOrDefault(l => l.IdTaskList == request.Item.IdTaskList && l.Project.IdTenant == request.IdTenant);
                    if (taskList == null)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail("TaskListNotFound", "The tenant task list was not found.");
                    }

                    var projectResult = GetTenantProject(context, request.IdTenant, request.Item.IdProject);
                    if (!projectResult.Success)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<TaskListMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    taskList.IdProject = projectResult.Value.IdProject;
                    taskList.IdUser = userResult.Value;
                    taskList.IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol);
                    taskList.TaskListName = name;
                    taskList.TaskListDescription = TrimOrNull(request.Item.TaskListDescription);
                    taskList.DisplayOrder = request.Item.DisplayOrder;
                    taskList.IsPublic = request.Item.IsPublic;
                    context.SaveChanges();

                    return ServiceResult<TaskListMasterDataDto>.Ok(ToTaskListDto(taskList, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TaskListMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteTaskList(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var taskList = context.TaskList.SingleOrDefault(l => l.IdTaskList == request.IdItem && l.Project.IdTenant == request.IdTenant);
                if (taskList == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("TaskListNotFound", "The tenant task list was not found.");
                }

                if (context.TaskItem.Any(t => t.IdTaskList == taskList.IdTaskList))
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("DeleteWouldBreakRelationships", "The task list is still used by task items.");
                }

                var now = DateTimeOffset.UtcNow;
                context.TaskList.Remove(taskList);
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, taskList.IdTaskList, "TaskList", true, true, now));
            }
        }

        public ServiceResult<IReadOnlyList<TaskItemMasterDataDto>> GetTaskItems(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<TaskItemMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<TaskItemMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.TaskItem.Where(t => t.Project.IdTenant == request.IdTenant);
                if (!request.IncludeInactive)
                {
                    query = query.Where(t => t.IsActive && t.Project.IsActive);
                }

                if (!request.IncludeDeleted)
                {
                    query = query.Where(t => !t.IsDeleted && !t.Project.IsDeleted);
                }

                if (request.IdProject.HasValue)
                {
                    query = query.Where(t => t.IdProject == request.IdProject.Value);
                }

                if (request.IdTaskList.HasValue)
                {
                    query = query.Where(t => t.IdTaskList == request.IdTaskList.Value);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(t => t.IdUser == request.IdUser.Value);
                }

                var taskItems = query
                    .OrderBy(t => t.IdProject)
                    .ThenBy(t => t.IdTaskList)
                    .ThenBy(t => t.TaskItemName)
                    .ToList()
                    .Select(t => ToTaskItemDto(t, request.IdTenant, LoadTagIds(context, t)))
                    .ToList();

                return ServiceResult<IReadOnlyList<TaskItemMasterDataDto>>.Ok(taskItems);
            }
        }

        public ServiceResult<TaskItemMasterDataDto> GetTaskItem(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TaskItemMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<TaskItemMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var taskItem = context.TaskItem.SingleOrDefault(t => t.IdTaskItem == request.IdItem && t.Project.IdTenant == request.IdTenant);
                if (taskItem == null)
                {
                    return ServiceResult<TaskItemMasterDataDto>.Fail("TaskItemNotFound", "The tenant task item was not found.");
                }

                return ServiceResult<TaskItemMasterDataDto>.Ok(ToTaskItemDto(taskItem, request.IdTenant, LoadTagIds(context, taskItem)));
            }
        }

        public ServiceResult<TaskItemMasterDataDto> CreateTaskItem(SaveTaskItemRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TaskItemMasterDataDto>.Fail("InvalidRequest", "A task item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.TaskItemName, nameof(request.Item.TaskItemName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<TaskItemMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var validation = ValidateTaskProjectAndList(context, request.IdTenant, request.Item.IdProject, request.Item.IdTaskList);
                    if (!validation.Success)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(validation.ErrorCode, validation.ErrorMessage);
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var tagsResult = GetTenantTags(context, request.IdTenant, request.Item.IdTagList);
                    if (!tagsResult.Success)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(tagsResult.ErrorCode, tagsResult.ErrorMessage);
                    }

                    var now = DateTimeOffset.UtcNow;
                    var taskItem = new TaskItem
                    {
                        IdTaskItem = request.Item.IdTaskItem == Guid.Empty ? Guid.NewGuid() : request.Item.IdTaskItem,
                        IdUser = userResult.Value,
                        IdProject = validation.Value.IdProject,
                        IdTaskList = NormalizeNullableGuid(request.Item.IdTaskList),
                        IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol),
                        TaskItemName = name,
                        TaskItemDescription = TrimOrNull(request.Item.TaskItemDescription),
                        QuickInfo = TrimOrNull(request.Item.QuickInfo),
                        DueDate = request.Item.DueDate,
                        TaskHoursBudget = request.Item.TaskHoursBudget,
                        Priority = request.Item.Priority,
                        IsPrivateTask = request.Item.IsPrivateTask,
                        Scope = request.Item.Scope,
                        IsActive = true,
                        IsCompleted = request.Item.IsCompleted,
                        DateCompleted = request.Item.IsCompleted ? request.Item.DateCompleted ?? now : (DateTimeOffset?)null,
                        IsDeleted = false,
                        IsForeign = request.Item.IsForeign,
                        DateCreated = now,
                        DateModified = now,
                        SyncId = Guid.NewGuid(),
                        SyncStatus = 0,
                        ExternalId = TrimOrNull(request.Item.ExternalId)
                    };

                    foreach (var tag in tagsResult.Value)
                    {
                        taskItem.Tag.Add(tag);
                    }

                    context.TaskItem.Add(taskItem);
                    context.SaveChanges();
                    return ServiceResult<TaskItemMasterDataDto>.Ok(ToTaskItemDto(taskItem, request.IdTenant, LoadTagIds(context, taskItem)));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TaskItemMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<TaskItemMasterDataDto> UpdateTaskItem(SaveTaskItemRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TaskItemMasterDataDto>.Fail("InvalidRequest", "A task item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.TaskItemName, nameof(request.Item.TaskItemName));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<TaskItemMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var taskItem = context.TaskItem.SingleOrDefault(t => t.IdTaskItem == request.Item.IdTaskItem && t.Project.IdTenant == request.IdTenant);
                    if (taskItem == null)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail("TaskItemNotFound", "The tenant task item was not found.");
                    }

                    var validation = ValidateTaskProjectAndList(context, request.IdTenant, request.Item.IdProject, request.Item.IdTaskList);
                    if (!validation.Success)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(validation.ErrorCode, validation.ErrorMessage);
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var tagsResult = GetTenantTags(context, request.IdTenant, request.Item.IdTagList);
                    if (!tagsResult.Success)
                    {
                        return ServiceResult<TaskItemMasterDataDto>.Fail(tagsResult.ErrorCode, tagsResult.ErrorMessage);
                    }

                    var now = DateTimeOffset.UtcNow;
                    taskItem.IdUser = userResult.Value;
                    taskItem.IdProject = validation.Value.IdProject;
                    taskItem.IdTaskList = NormalizeNullableGuid(request.Item.IdTaskList);
                    taskItem.IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol);
                    taskItem.TaskItemName = name;
                    taskItem.TaskItemDescription = TrimOrNull(request.Item.TaskItemDescription);
                    taskItem.QuickInfo = TrimOrNull(request.Item.QuickInfo);
                    taskItem.DueDate = request.Item.DueDate;
                    taskItem.TaskHoursBudget = request.Item.TaskHoursBudget;
                    taskItem.Priority = request.Item.Priority;
                    taskItem.IsPrivateTask = request.Item.IsPrivateTask;
                    taskItem.Scope = request.Item.Scope;
                    taskItem.IsDeleted = request.Item.IsDeleted;
                    taskItem.IsActive = request.Item.IsDeleted ? false : request.Item.IsActive;
                    taskItem.IsCompleted = request.Item.IsCompleted;
                    taskItem.DateCompleted = request.Item.IsCompleted ? request.Item.DateCompleted ?? now : (DateTimeOffset?)null;
                    taskItem.IsForeign = request.Item.IsForeign;
                    taskItem.DateModified = now;
                    taskItem.ExternalId = TrimOrNull(request.Item.ExternalId);
                    ReplaceTags(context, taskItem, tagsResult.Value);
                    context.SaveChanges();

                    return ServiceResult<TaskItemMasterDataDto>.Ok(ToTaskItemDto(taskItem, request.IdTenant, LoadTagIds(context, taskItem)));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TaskItemMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteTaskItem(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var taskItem = context.TaskItem.SingleOrDefault(t => t.IdTaskItem == request.IdItem && t.Project.IdTenant == request.IdTenant);
                if (taskItem == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("TaskItemNotFound", "The tenant task item was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                if (request.HardDelete)
                {
                    if (context.TimeItem.Any(t => t.IdTask == taskItem.IdTaskItem) ||
                        context.Note.Any(n => n.IdTask == taskItem.IdTaskItem))
                    {
                        return ServiceResult<MasterDataDeleteResult>.Fail("DeleteWouldBreakRelationships", "The task item is used by time items or notes.");
                    }

                    context.Entry(taskItem).Collection(t => t.Tag).Load();
                    taskItem.Tag.Clear();
                    context.TaskItem.Remove(taskItem);
                    context.SaveChanges();
                    return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, taskItem.IdTaskItem, "TaskItem", true, true, now));
                }

                taskItem.IsDeleted = true;
                taskItem.IsActive = false;
                taskItem.DateModified = now;
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, taskItem.IdTaskItem, "TaskItem", true, false, now));
            }
        }

        public ServiceResult<IReadOnlyList<TagMasterDataDto>> GetTags(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<TagMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<TagMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.Tag.Where(t => t.User.IdTenant == request.IdTenant);
                if (!request.IncludeInactive)
                {
                    query = query.Where(t => t.User.IsActive);
                }

                if (!request.IncludeDeleted)
                {
                    query = query.Where(t => !t.User.IsDeleted);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(t => t.IdUser == request.IdUser.Value);
                }

                var tags = query
                    .OrderBy(t => t.Tag1)
                    .ToList()
                    .Select(t => ToTagDto(t, request.IdTenant))
                    .ToList();

                return ServiceResult<IReadOnlyList<TagMasterDataDto>>.Ok(tags);
            }
        }

        public ServiceResult<TagMasterDataDto> GetTag(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TagMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<TagMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var tag = context.Tag.SingleOrDefault(t => t.IdTag == request.IdItem && t.User.IdTenant == request.IdTenant);
                if (tag == null)
                {
                    return ServiceResult<TagMasterDataDto>.Fail("TagNotFound", "The tenant tag was not found.");
                }

                return ServiceResult<TagMasterDataDto>.Ok(ToTagDto(tag, request.IdTenant));
            }
        }

        public ServiceResult<TagMasterDataDto> CreateTag(SaveTagRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TagMasterDataDto>.Fail("InvalidRequest", "A tag item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.Tag, nameof(request.Item.Tag));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<TagMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<TagMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var now = DateTimeOffset.UtcNow;
                    var tag = new Tag
                    {
                        IdTag = request.Item.IdTag == Guid.Empty ? Guid.NewGuid() : request.Item.IdTag,
                        IdUser = userResult.Value,
                        Tag1 = name,
                        Description = TrimOrNull(request.Item.Description),
                        DateCreated = now,
                        DateModified = now
                    };

                    context.Tag.Add(tag);
                    context.SaveChanges();
                    return ServiceResult<TagMasterDataDto>.Ok(ToTagDto(tag, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TagMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<TagMasterDataDto> UpdateTag(SaveTagRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TagMasterDataDto>.Fail("InvalidRequest", "A tag item is required.");
            }

            try
            {
                var name = NormalizeRequired(request.Item.Tag, nameof(request.Item.Tag));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<TagMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var tag = context.Tag.SingleOrDefault(t => t.IdTag == request.Item.IdTag && t.User.IdTenant == request.IdTenant);
                    if (tag == null)
                    {
                        return ServiceResult<TagMasterDataDto>.Fail("TagNotFound", "The tenant tag was not found.");
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<TagMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    tag.IdUser = userResult.Value;
                    tag.Tag1 = name;
                    tag.Description = TrimOrNull(request.Item.Description);
                    tag.DateModified = DateTimeOffset.UtcNow;
                    context.SaveChanges();

                    return ServiceResult<TagMasterDataDto>.Ok(ToTagDto(tag, request.IdTenant));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<TagMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteTag(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var tag = context.Tag.SingleOrDefault(t => t.IdTag == request.IdItem && t.User.IdTenant == request.IdTenant);
                if (tag == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("TagNotFound", "The tenant tag was not found.");
                }

                if (context.TaskItem.Any(t => t.Tag.Any(tagItem => tagItem.IdTag == tag.IdTag)) ||
                    context.Note.Any(n => n.Tag.Any(tagItem => tagItem.IdTag == tag.IdTag)) ||
                    context.WebLink.Any(w => w.Tag.Any(tagItem => tagItem.IdTag == tag.IdTag)))
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("DeleteWouldBreakRelationships", "The tag is still assigned to task items, notes, or web links.");
                }

                var now = DateTimeOffset.UtcNow;
                context.Tag.Remove(tag);
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, tag.IdTag, "Tag", true, true, now));
            }
        }

        public ServiceResult<IReadOnlyList<NoteMasterDataDto>> GetNotes(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<NoteMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<NoteMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.Note.Where(n => n.User.IdTenant == request.IdTenant);
                if (request.IdProject.HasValue)
                {
                    query = query.Where(n => n.IdProject == request.IdProject.Value);
                }

                if (request.IdTask.HasValue)
                {
                    query = query.Where(n => n.IdTask == request.IdTask.Value);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(n => n.IdUser == request.IdUser.Value);
                }

                var notes = query
                    .OrderByDescending(n => n.DateModified)
                    .ThenBy(n => n.NoteMnemonic)
                    .ToList()
                    .Select(n => ToNoteDto(n, request.IdTenant, LoadTagIds(context, n)))
                    .ToList();

                return ServiceResult<IReadOnlyList<NoteMasterDataDto>>.Ok(notes);
            }
        }

        public ServiceResult<NoteMasterDataDto> GetNote(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<NoteMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<NoteMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var note = context.Note.SingleOrDefault(n => n.IdNote == request.IdItem && n.User.IdTenant == request.IdTenant);
                if (note == null)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail("NoteNotFound", "The tenant note was not found.");
                }

                return ServiceResult<NoteMasterDataDto>.Ok(ToNoteDto(note, request.IdTenant, LoadTagIds(context, note)));
            }
        }

        public ServiceResult<NoteMasterDataDto> CreateNote(SaveNoteRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<NoteMasterDataDto>.Fail("InvalidRequest", "A note item is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<NoteMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var validation = ValidateOptionalProjectAndTask(context, request.IdTenant, request.Item.IdProject, request.Item.IdTask);
                if (validation != null)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(validation.Item1, validation.Item2);
                }

                var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                if (!userResult.Success)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                }

                var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                if (symbolCheck != null)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                }

                var tagsResult = GetTenantTags(context, request.IdTenant, request.Item.IdTagList);
                if (!tagsResult.Success)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(tagsResult.ErrorCode, tagsResult.ErrorMessage);
                }

                var now = DateTimeOffset.UtcNow;
                var note = new Note
                {
                    IdNote = request.Item.IdNote == Guid.Empty ? Guid.NewGuid() : request.Item.IdNote,
                    IdUser = userResult.Value,
                    IdProject = NormalizeNullableGuid(request.Item.IdProject),
                    IdTask = NormalizeNullableGuid(request.Item.IdTask),
                    IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol),
                    NoteMnemonic = TrimOrNull(request.Item.NoteMnemonic),
                    Note1 = TrimOrNull(request.Item.NoteText),
                    DateCreated = now,
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = TrimOrNull(request.Item.ExternalId)
                };

                foreach (var tag in tagsResult.Value)
                {
                    note.Tag.Add(tag);
                }

                context.Note.Add(note);
                context.SaveChanges();
                return ServiceResult<NoteMasterDataDto>.Ok(ToNoteDto(note, request.IdTenant, LoadTagIds(context, note)));
            }
        }

        public ServiceResult<NoteMasterDataDto> UpdateNote(SaveNoteRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<NoteMasterDataDto>.Fail("InvalidRequest", "A note item is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<NoteMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var note = context.Note.SingleOrDefault(n => n.IdNote == request.Item.IdNote && n.User.IdTenant == request.IdTenant);
                if (note == null)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail("NoteNotFound", "The tenant note was not found.");
                }

                var validation = ValidateOptionalProjectAndTask(context, request.IdTenant, request.Item.IdProject, request.Item.IdTask);
                if (validation != null)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(validation.Item1, validation.Item2);
                }

                var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                if (!userResult.Success)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                }

                var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                if (symbolCheck != null)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                }

                var tagsResult = GetTenantTags(context, request.IdTenant, request.Item.IdTagList);
                if (!tagsResult.Success)
                {
                    return ServiceResult<NoteMasterDataDto>.Fail(tagsResult.ErrorCode, tagsResult.ErrorMessage);
                }

                note.IdUser = userResult.Value;
                note.IdProject = NormalizeNullableGuid(request.Item.IdProject);
                note.IdTask = NormalizeNullableGuid(request.Item.IdTask);
                note.IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol);
                note.NoteMnemonic = TrimOrNull(request.Item.NoteMnemonic);
                note.Note1 = TrimOrNull(request.Item.NoteText);
                note.DateModified = DateTimeOffset.UtcNow;
                note.ExternalId = TrimOrNull(request.Item.ExternalId);
                ReplaceTags(context, note, tagsResult.Value);
                context.SaveChanges();

                return ServiceResult<NoteMasterDataDto>.Ok(ToNoteDto(note, request.IdTenant, LoadTagIds(context, note)));
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteNote(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var note = context.Note.SingleOrDefault(n => n.IdNote == request.IdItem && n.User.IdTenant == request.IdTenant);
                if (note == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("NoteNotFound", "The tenant note was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                context.Entry(note).Collection(n => n.Tag).Load();
                note.Tag.Clear();
                context.Note.Remove(note);
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, note.IdNote, "Note", true, true, now));
            }
        }

        public ServiceResult<IReadOnlyList<WebLinkMasterDataDto>> GetWebLinks(MasterDataQueryRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<WebLinkMasterDataDto>>.Fail("InvalidRequest", "A master-data query request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<IReadOnlyList<WebLinkMasterDataDto>>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var query = context.WebLink.Where(w => w.User.IdTenant == request.IdTenant);
                if (request.IdProject.HasValue)
                {
                    query = query.Where(w => w.IdProject == request.IdProject.Value);
                }

                if (request.IdUser.HasValue)
                {
                    query = query.Where(w => w.IdUser == request.IdUser.Value);
                }

                var webLinks = query
                    .OrderBy(w => w.Domain)
                    .ThenBy(w => w.Title)
                    .ToList()
                    .Select(w => ToWebLinkDto(w, request.IdTenant, LoadTagIds(context, w)))
                    .ToList();

                return ServiceResult<IReadOnlyList<WebLinkMasterDataDto>>.Ok(webLinks);
            }
        }

        public ServiceResult<WebLinkMasterDataDto> GetWebLink(MasterDataItemRequest request)
        {
            if (request == null)
            {
                return ServiceResult<WebLinkMasterDataDto>.Fail("InvalidRequest", "A master-data item request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<WebLinkMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var webLink = context.WebLink.SingleOrDefault(w => w.IdWebLink == request.IdItem && w.User.IdTenant == request.IdTenant);
                if (webLink == null)
                {
                    return ServiceResult<WebLinkMasterDataDto>.Fail("WebLinkNotFound", "The tenant web link was not found.");
                }

                return ServiceResult<WebLinkMasterDataDto>.Ok(ToWebLinkDto(webLink, request.IdTenant, LoadTagIds(context, webLink)));
            }
        }

        public ServiceResult<WebLinkMasterDataDto> CreateWebLink(SaveWebLinkRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<WebLinkMasterDataDto>.Fail("InvalidRequest", "A web link item is required.");
            }

            try
            {
                var link = NormalizeRequired(request.Item.Link, nameof(request.Item.Link));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<WebLinkMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    if (request.Item.IdProject.HasValue)
                    {
                        var projectResult = GetTenantProject(context, request.IdTenant, request.Item.IdProject.Value);
                        if (!projectResult.Success)
                        {
                            return ServiceResult<WebLinkMasterDataDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                        }
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var tagsResult = GetTenantTags(context, request.IdTenant, request.Item.IdTagList);
                    if (!tagsResult.Success)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail(tagsResult.ErrorCode, tagsResult.ErrorMessage);
                    }

                    var now = DateTimeOffset.UtcNow;
                    var webLink = new WebLink
                    {
                        IdWebLink = request.Item.IdWebLink == Guid.Empty ? Guid.NewGuid() : request.Item.IdWebLink,
                        IdUser = userResult.Value,
                        IdProject = NormalizeNullableGuid(request.Item.IdProject),
                        IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol),
                        Link = link,
                        Domain = TrimOrNull(request.Item.Domain),
                        Title = TrimOrNull(request.Item.Title),
                        Description = TrimOrNull(request.Item.Description),
                        DateCreated = now,
                        DateModified = now,
                        SyncId = Guid.NewGuid(),
                        SyncStatus = 0,
                        ExternalId = TrimOrNull(request.Item.ExternalId)
                    };

                    foreach (var tag in tagsResult.Value)
                    {
                        webLink.Tag.Add(tag);
                    }

                    context.WebLink.Add(webLink);
                    context.SaveChanges();
                    return ServiceResult<WebLinkMasterDataDto>.Ok(ToWebLinkDto(webLink, request.IdTenant, LoadTagIds(context, webLink)));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<WebLinkMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<WebLinkMasterDataDto> UpdateWebLink(SaveWebLinkRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<WebLinkMasterDataDto>.Fail("InvalidRequest", "A web link item is required.");
            }

            try
            {
                var link = NormalizeRequired(request.Item.Link, nameof(request.Item.Link));
                using (var context = CreateContext())
                {
                    var authorization = Authorize<WebLinkMasterDataDto>(context, request.IdTenant, request.IdActingUser);
                    if (authorization != null)
                    {
                        return authorization;
                    }

                    var webLink = context.WebLink.SingleOrDefault(w => w.IdWebLink == request.Item.IdWebLink && w.User.IdTenant == request.IdTenant);
                    if (webLink == null)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail("WebLinkNotFound", "The tenant web link was not found.");
                    }

                    if (request.Item.IdProject.HasValue)
                    {
                        var projectResult = GetTenantProject(context, request.IdTenant, request.Item.IdProject.Value);
                        if (!projectResult.Success)
                        {
                            return ServiceResult<WebLinkMasterDataDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                        }
                    }

                    var userResult = ResolveActiveTenantUser(context, request.IdTenant, request.Item.IdUser, request.IdActingUser);
                    if (!userResult.Success)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    var symbolCheck = EnsureSymbolBelongsToTenantOrSystem(context, request.IdTenant, request.Item.IdSymbol);
                    if (symbolCheck != null)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail(symbolCheck.Item1, symbolCheck.Item2);
                    }

                    var tagsResult = GetTenantTags(context, request.IdTenant, request.Item.IdTagList);
                    if (!tagsResult.Success)
                    {
                        return ServiceResult<WebLinkMasterDataDto>.Fail(tagsResult.ErrorCode, tagsResult.ErrorMessage);
                    }

                    webLink.IdUser = userResult.Value;
                    webLink.IdProject = NormalizeNullableGuid(request.Item.IdProject);
                    webLink.IdSymbol = NormalizeNullableGuid(request.Item.IdSymbol);
                    webLink.Link = link;
                    webLink.Domain = TrimOrNull(request.Item.Domain);
                    webLink.Title = TrimOrNull(request.Item.Title);
                    webLink.Description = TrimOrNull(request.Item.Description);
                    webLink.DateModified = DateTimeOffset.UtcNow;
                    webLink.ExternalId = TrimOrNull(request.Item.ExternalId);
                    ReplaceTags(context, webLink, tagsResult.Value);
                    context.SaveChanges();

                    return ServiceResult<WebLinkMasterDataDto>.Ok(ToWebLinkDto(webLink, request.IdTenant, LoadTagIds(context, webLink)));
                }
            }
            catch (ArgumentException ex)
            {
                return ServiceResult<WebLinkMasterDataDto>.Fail("InvalidRequest", ex.Message);
            }
        }

        public ServiceResult<MasterDataDeleteResult> DeleteWebLink(DeleteMasterDataRequest request)
        {
            if (request == null)
            {
                return ServiceResult<MasterDataDeleteResult>.Fail("InvalidRequest", "A delete request is required.");
            }

            using (var context = CreateContext())
            {
                var authorization = Authorize<MasterDataDeleteResult>(context, request.IdTenant, request.IdActingUser);
                if (authorization != null)
                {
                    return authorization;
                }

                var webLink = context.WebLink.SingleOrDefault(w => w.IdWebLink == request.IdItem && w.User.IdTenant == request.IdTenant);
                if (webLink == null)
                {
                    return ServiceResult<MasterDataDeleteResult>.Fail("WebLinkNotFound", "The tenant web link was not found.");
                }

                var now = DateTimeOffset.UtcNow;
                context.Entry(webLink).Collection(w => w.Tag).Load();
                webLink.Tag.Clear();
                context.WebLink.Remove(webLink);
                context.SaveChanges();
                return ServiceResult<MasterDataDeleteResult>.Ok(ToDeleteResult(request.IdTenant, webLink.IdWebLink, "WebLink", true, true, now));
            }
        }

        private static ServiceResult<T> Authorize<T>(TaskOTimeContext context, Guid idTenant, Guid idActingUser)
        {
            if (idTenant == Guid.Empty || idActingUser == Guid.Empty)
            {
                return ServiceResult<T>.Fail("InvalidRequest", "Tenant and acting user ids are required.");
            }

            if (!context.Tenant.Any(t => t.IdTenant == idTenant && t.IsActive && !t.IsDeleted))
            {
                return ServiceResult<T>.Fail("TenantNotFound", "The tenant was not found or is inactive.");
            }

            if (!context.User.Any(u =>
                    u.IdTenant == idTenant &&
                    u.IdUser == idActingUser &&
                    u.IsAdmin &&
                    u.IsActive &&
                    !u.IsDeleted))
            {
                return ServiceResult<T>.Fail("NotAuthorized", "The acting user is not an active tenant admin.");
            }

            return null;
        }

        private static ServiceResult<Guid> ResolveActiveTenantUser(TaskOTimeContext context, Guid idTenant, Guid requestedUserId, Guid defaultUserId)
        {
            var idUser = requestedUserId == Guid.Empty ? defaultUserId : requestedUserId;
            if (!context.User.Any(u => u.IdTenant == idTenant && u.IdUser == idUser && u.IsActive && !u.IsDeleted))
            {
                return ServiceResult<Guid>.Fail("UserNotFound", "The active tenant user was not found.");
            }

            return ServiceResult<Guid>.Ok(idUser);
        }

        private static ServiceResult<Project> GetTenantProject(TaskOTimeContext context, Guid idTenant, Guid idProject)
        {
            if (idProject == Guid.Empty)
            {
                return ServiceResult<Project>.Fail("ProjectRequired", "A project id is required.");
            }

            var project = context.Project.SingleOrDefault(p => p.IdTenant == idTenant && p.IdProject == idProject && !p.IsDeleted);
            if (project == null)
            {
                return ServiceResult<Project>.Fail("ProjectNotFound", "The tenant project was not found.");
            }

            return ServiceResult<Project>.Ok(project);
        }

        private static ServiceResult<Project> ValidateTaskProjectAndList(TaskOTimeContext context, Guid idTenant, Guid idProject, Guid? idTaskList)
        {
            var projectResult = GetTenantProject(context, idTenant, idProject);
            if (!projectResult.Success)
            {
                return projectResult;
            }

            var normalizedListId = NormalizeNullableGuid(idTaskList);
            if (normalizedListId.HasValue &&
                !context.TaskList.Any(l =>
                    l.IdTaskList == normalizedListId.Value &&
                    l.IdProject == projectResult.Value.IdProject &&
                    l.Project.IdTenant == idTenant))
            {
                return ServiceResult<Project>.Fail("TaskListNotFound", "The tenant task list was not found for the selected project.");
            }

            return projectResult;
        }

        private static Tuple<string, string> ValidateOptionalProjectAndTask(TaskOTimeContext context, Guid idTenant, Guid? idProject, Guid? idTask)
        {
            var normalizedProjectId = NormalizeNullableGuid(idProject);
            var normalizedTaskId = NormalizeNullableGuid(idTask);
            if (normalizedProjectId.HasValue && !context.Project.Any(p => p.IdTenant == idTenant && p.IdProject == normalizedProjectId.Value && !p.IsDeleted))
            {
                return Tuple.Create("ProjectNotFound", "The tenant project was not found.");
            }

            if (normalizedTaskId.HasValue)
            {
                var task = context.TaskItem.SingleOrDefault(t => t.IdTaskItem == normalizedTaskId.Value && t.Project.IdTenant == idTenant && !t.IsDeleted);
                if (task == null)
                {
                    return Tuple.Create("TaskItemNotFound", "The tenant task item was not found.");
                }

                if (normalizedProjectId.HasValue && task.IdProject != normalizedProjectId.Value)
                {
                    return Tuple.Create("InvalidRequest", "The task item does not belong to the selected project.");
                }
            }

            return null;
        }

        private static Tuple<string, string> EnsureSymbolBelongsToTenantOrSystem(TaskOTimeContext context, Guid idTenant, Guid? idSymbol)
        {
            var normalizedSymbolId = NormalizeNullableGuid(idSymbol);
            if (!normalizedSymbolId.HasValue)
            {
                return null;
            }

            var symbol = context.CategorySymbol.SingleOrDefault(s => s.IdCategorySymbol == normalizedSymbolId.Value);
            if (symbol == null)
            {
                return Tuple.Create("CategorySymbolNotFound", "The category symbol was not found.");
            }

            if (symbol.IdProject.HasValue &&
                !context.Project.Any(p => p.IdProject == symbol.IdProject.Value && p.IdTenant == idTenant && !p.IsDeleted))
            {
                return Tuple.Create("CategorySymbolNotFound", "The category symbol does not belong to the tenant.");
            }

            return null;
        }

        private static ServiceResult<IReadOnlyList<Tag>> GetTenantTags(TaskOTimeContext context, Guid idTenant, IReadOnlyList<Guid> idTags)
        {
            var tagIds = (idTags ?? new Guid[0])
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            if (tagIds.Count == 0)
            {
                return ServiceResult<IReadOnlyList<Tag>>.Ok(new List<Tag>());
            }

            var tags = context.Tag
                .Where(t => tagIds.Contains(t.IdTag) && t.User.IdTenant == idTenant && t.User.IsActive && !t.User.IsDeleted)
                .ToList();
            if (tags.Count != tagIds.Count)
            {
                return ServiceResult<IReadOnlyList<Tag>>.Fail("TagNotFound", "One or more tenant tags were not found.");
            }

            return ServiceResult<IReadOnlyList<Tag>>.Ok(tags);
        }

        private static void EnsureOwnerAssignment(TaskOTimeContext context, Project project, Guid idActingUser, DateTimeOffset now)
        {
            var assignment = context.ProjectUserAssignment.SingleOrDefault(a =>
                a.IdProject == project.IdProject &&
                a.IdUser == project.IdUser);
            if (assignment == null)
            {
                assignment = new ProjectUserAssignment
                {
                    IdProjectUserAssignment = Guid.NewGuid(),
                    IdProject = project.IdProject,
                    IdUser = project.IdUser,
                    DateCreated = now,
                    DisplayOrder = GetNextAssignmentDisplayOrder(context, project.IdProject)
                };
                context.ProjectUserAssignment.Add(assignment);
            }

            assignment.IdAssignedByUser = idActingUser;
            assignment.AssignmentRole = 1;
            assignment.CanBookTime = true;
            assignment.CanManageTasks = true;
            assignment.CanManageProject = true;
            assignment.IsActive = true;
            assignment.IsDeleted = false;
            assignment.DateAssigned = now;
            assignment.DateRemoved = null;
            assignment.DateModified = now;
        }

        private static int GetNextAssignmentDisplayOrder(TaskOTimeContext context, Guid idProject)
        {
            var maxDisplayOrder = context.ProjectUserAssignment
                .Where(a => a.IdProject == idProject)
                .Select(a => (int?)a.DisplayOrder)
                .Max();
            return (maxDisplayOrder ?? -1) + 1;
        }

        private static string GetProjectDependency(TaskOTimeContext context, Guid idProject)
        {
            if (context.TaskList.Any(l => l.IdProject == idProject) ||
                context.TaskItem.Any(t => t.IdProject == idProject) ||
                context.Note.Any(n => n.IdProject == idProject) ||
                context.TimeItem.Any(t => t.IdProject == idProject) ||
                context.WebLink.Any(w => w.IdProject == idProject) ||
                context.CategorySymbol.Any(s => s.IdProject == idProject) ||
                context.ProjectUserAssignment.Any(a => a.IdProject == idProject) ||
                context.SharableProject.Any(s => s.IdProject == idProject))
            {
                return "The project is still referenced by tenant data.";
            }

            return null;
        }

        private static bool HasCategorySymbolDependencies(TaskOTimeContext context, Guid idSymbol)
        {
            return context.Category.Any(c => c.IdSymbol == idSymbol) ||
                   context.Project.Any(p => p.IdSymbol == idSymbol) ||
                   context.TaskList.Any(l => l.IdSymbol == idSymbol) ||
                   context.TaskItem.Any(t => t.IdSymbol == idSymbol) ||
                   context.Note.Any(n => n.IdSymbol == idSymbol) ||
                   context.WebLink.Any(w => w.IdSymbol == idSymbol);
        }

        private static void ReplaceTags(TaskOTimeContext context, TaskItem taskItem, IReadOnlyList<Tag> tags)
        {
            context.Entry(taskItem).Collection(t => t.Tag).Load();
            taskItem.Tag.Clear();
            foreach (var tag in tags)
            {
                taskItem.Tag.Add(tag);
            }
        }

        private static void ReplaceTags(TaskOTimeContext context, Note note, IReadOnlyList<Tag> tags)
        {
            context.Entry(note).Collection(n => n.Tag).Load();
            note.Tag.Clear();
            foreach (var tag in tags)
            {
                note.Tag.Add(tag);
            }
        }

        private static void ReplaceTags(TaskOTimeContext context, WebLink webLink, IReadOnlyList<Tag> tags)
        {
            context.Entry(webLink).Collection(w => w.Tag).Load();
            webLink.Tag.Clear();
            foreach (var tag in tags)
            {
                webLink.Tag.Add(tag);
            }
        }

        private static IReadOnlyList<Guid> LoadTagIds(TaskOTimeContext context, TaskItem taskItem)
        {
            context.Entry(taskItem).Collection(t => t.Tag).Load();
            return taskItem.Tag.Select(t => t.IdTag).OrderBy(id => id).ToList();
        }

        private static IReadOnlyList<Guid> LoadTagIds(TaskOTimeContext context, Note note)
        {
            context.Entry(note).Collection(n => n.Tag).Load();
            return note.Tag.Select(t => t.IdTag).OrderBy(id => id).ToList();
        }

        private static IReadOnlyList<Guid> LoadTagIds(TaskOTimeContext context, WebLink webLink)
        {
            context.Entry(webLink).Collection(w => w.Tag).Load();
            return webLink.Tag.Select(t => t.IdTag).OrderBy(id => id).ToList();
        }

        private static ProjectMainDataDto ToProjectMasterDataDto(Project project)
        {
            return new ProjectMainDataDto
            {
                IdProject = project.IdProject,
                IdTenant = project.IdTenant,
                IdUser = project.IdUser,
                IdSymbol = project.IdSymbol,
                ProjectName = project.ProjectName,
                ProjectType = project.ProjectType,
                ProjectNumber = project.ProjectNumber,
                ProjectDescription = project.ProjectDescription,
                ProjectIdentifier = project.ProjectIdentifier,
                ProjectSymbolChar = project.ProjectSymbolChar,
                ProjectSymbolColor = project.ProjectSymbolColor,
                ProjectSymbolUrl = project.ProjectSymbolUrl,
                StartOfProject = project.StartOfProject,
                EndOfProject = project.EndOfProject,
                IsActive = project.IsActive,
                IsDeleted = project.IsDeleted,
                Scope = project.Scope,
                IsSystem = project.IsSystem,
                DateCreated = project.DateCreated,
                DateModified = project.DateModified,
                ExternalId = project.ExternalId
            };
        }

        private static CategoryMasterDataDto ToCategoryDto(Category category, Guid idTenant)
        {
            return new CategoryMasterDataDto
            {
                IdCategory = category.IdCategory,
                IdTenant = idTenant,
                IdUser = category.IdUser,
                IdSymbol = category.IdSymbol,
                CategoryName = category.CategoryName,
                CategoryDescription = category.CategoryDescription,
                DisplayOrder = category.DisplayOrder,
                IsPublic = category.IsPublic
            };
        }

        private static CategorySymbolMasterDataDto ToCategorySymbolDto(CategorySymbol symbol, Guid idTenant)
        {
            return new CategorySymbolMasterDataDto
            {
                IdCategorySymbol = symbol.IdCategorySymbol,
                IdTenant = idTenant,
                IdProject = symbol.IdProject,
                SymbolName = symbol.SymbolName,
                SymbolDescription = symbol.SymbolDescription,
                SymbolChar = symbol.SymbolChar,
                FontName = symbol.FontName,
                FontSize = symbol.FontSize,
                SymbolOffsetX = symbol.SymbolOffsetX,
                SymbolOffsetY = symbol.SymbolOffsetY,
                SymbolColor = symbol.SymbolColor
            };
        }

        private static TaskListMasterDataDto ToTaskListDto(TaskList taskList, Guid idTenant)
        {
            return new TaskListMasterDataDto
            {
                IdTaskList = taskList.IdTaskList,
                IdTenant = idTenant,
                IdProject = taskList.IdProject,
                IdUser = taskList.IdUser,
                IdSymbol = taskList.IdSymbol,
                TaskListName = taskList.TaskListName,
                TaskListDescription = taskList.TaskListDescription,
                DisplayOrder = taskList.DisplayOrder,
                IsPublic = taskList.IsPublic
            };
        }

        private static TaskItemMasterDataDto ToTaskItemDto(TaskItem taskItem, Guid idTenant, IReadOnlyList<Guid> tagIds)
        {
            return new TaskItemMasterDataDto
            {
                IdTaskItem = taskItem.IdTaskItem,
                IdTenant = idTenant,
                IdUser = taskItem.IdUser,
                IdProject = taskItem.IdProject,
                IdTaskList = taskItem.IdTaskList,
                IdSymbol = taskItem.IdSymbol,
                TaskItemName = taskItem.TaskItemName,
                TaskItemDescription = taskItem.TaskItemDescription,
                QuickInfo = taskItem.QuickInfo,
                DueDate = taskItem.DueDate,
                TaskHoursBudget = taskItem.TaskHoursBudget,
                Priority = taskItem.Priority,
                IsPrivateTask = taskItem.IsPrivateTask,
                Scope = taskItem.Scope,
                IsActive = taskItem.IsActive,
                IsCompleted = taskItem.IsCompleted,
                DateCompleted = taskItem.DateCompleted,
                IsDeleted = taskItem.IsDeleted,
                IsForeign = taskItem.IsForeign,
                DateCreated = taskItem.DateCreated,
                DateModified = taskItem.DateModified,
                ExternalId = taskItem.ExternalId,
                IdTagList = tagIds
            };
        }

        private static TagMasterDataDto ToTagDto(Tag tag, Guid idTenant)
        {
            return new TagMasterDataDto
            {
                IdTag = tag.IdTag,
                IdTenant = idTenant,
                IdUser = tag.IdUser,
                Tag = tag.Tag1,
                Description = tag.Description,
                DateCreated = tag.DateCreated,
                DateModified = tag.DateModified
            };
        }

        private static NoteMasterDataDto ToNoteDto(Note note, Guid idTenant, IReadOnlyList<Guid> tagIds)
        {
            return new NoteMasterDataDto
            {
                IdNote = note.IdNote,
                IdTenant = idTenant,
                IdUser = note.IdUser,
                IdProject = note.IdProject,
                IdTask = note.IdTask,
                IdSymbol = note.IdSymbol,
                NoteMnemonic = note.NoteMnemonic,
                NoteText = note.Note1,
                DateCreated = note.DateCreated,
                DateModified = note.DateModified,
                ExternalId = note.ExternalId,
                IdTagList = tagIds
            };
        }

        private static WebLinkMasterDataDto ToWebLinkDto(WebLink webLink, Guid idTenant, IReadOnlyList<Guid> tagIds)
        {
            return new WebLinkMasterDataDto
            {
                IdWebLink = webLink.IdWebLink,
                IdTenant = idTenant,
                IdUser = webLink.IdUser,
                IdProject = webLink.IdProject,
                IdSymbol = webLink.IdSymbol,
                Link = webLink.Link,
                Domain = webLink.Domain,
                Title = webLink.Title,
                Description = webLink.Description,
                DateCreated = webLink.DateCreated,
                DateModified = webLink.DateModified,
                ExternalId = webLink.ExternalId,
                IdTagList = tagIds
            };
        }

        private static MasterDataDeleteResult ToDeleteResult(Guid idTenant, Guid idItem, string entityName, bool deleted, bool hardDeleted, DateTimeOffset deletedAt)
        {
            return new MasterDataDeleteResult
            {
                IdTenant = idTenant,
                IdItem = idItem,
                EntityName = entityName,
                Deleted = deleted,
                HardDeleted = hardDeleted,
                DeletedAt = deletedAt
            };
        }

        private static Guid? NormalizeNullableGuid(Guid? id)
        {
            return id.HasValue && id.Value != Guid.Empty ? id : (Guid?)null;
        }

        private static string TrimOrNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
