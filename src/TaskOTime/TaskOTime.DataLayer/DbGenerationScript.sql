/*
    TaskOTime database generation script.
    Target: SQL Server 2012 compatibility level 110.
*/

IF DB_ID(N'TaskOTime') IS NULL
BEGIN
    CREATE DATABASE [TaskOTime];
END
GO

ALTER DATABASE [TaskOTime] SET COMPATIBILITY_LEVEL = 110;
GO

USE [TaskOTime];
GO

SET ANSI_NULLS ON;
GO

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[ReserveSequentialGuids]', N'P') IS NOT NULL
    DROP PROCEDURE [dbo].[ReserveSequentialGuids];
GO

DECLARE @DropForeignKeys nvarchar(max) = N'';

SELECT @DropForeignKeys = @DropForeignKeys +
    N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' +
    QUOTENAME(OBJECT_NAME(parent_object_id)) + N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' +
    CHAR(13) + CHAR(10)
FROM sys.foreign_keys
WHERE OBJECT_SCHEMA_NAME(parent_object_id) = N'dbo'
  AND OBJECT_NAME(parent_object_id) IN
  (
      N'Category',
      N'CategorySymbol',
      N'LogItem',
      N'Note',
      N'NoteTag',
      N'Project',
      N'ProjectUserAssignment',
      N'SharableProject',
      N'Tag',
      N'TaskItem',
      N'TaskItemTag',
      N'TaskList',
      N'Tenant',
      N'TenantLead',
      N'TimeItem',
      N'User',
      N'WebLink',
      N'WebLinkTag'
  );

IF LEN(@DropForeignKeys) > 0
    EXEC sys.sp_executesql @DropForeignKeys;
GO

IF OBJECT_ID(N'[dbo].[WebLinkTag]', N'U') IS NOT NULL DROP TABLE [dbo].[WebLinkTag];
IF OBJECT_ID(N'[dbo].[TaskItemTag]', N'U') IS NOT NULL DROP TABLE [dbo].[TaskItemTag];
IF OBJECT_ID(N'[dbo].[NoteTag]', N'U') IS NOT NULL DROP TABLE [dbo].[NoteTag];
IF OBJECT_ID(N'[dbo].[TimeItem]', N'U') IS NOT NULL DROP TABLE [dbo].[TimeItem];
IF OBJECT_ID(N'[dbo].[WebLink]', N'U') IS NOT NULL DROP TABLE [dbo].[WebLink];
IF OBJECT_ID(N'[dbo].[Note]', N'U') IS NOT NULL DROP TABLE [dbo].[Note];
IF OBJECT_ID(N'[dbo].[TaskItem]', N'U') IS NOT NULL DROP TABLE [dbo].[TaskItem];
IF OBJECT_ID(N'[dbo].[TaskList]', N'U') IS NOT NULL DROP TABLE [dbo].[TaskList];
IF OBJECT_ID(N'[dbo].[ProjectUserAssignment]', N'U') IS NOT NULL DROP TABLE [dbo].[ProjectUserAssignment];
IF OBJECT_ID(N'[dbo].[SharableProject]', N'U') IS NOT NULL DROP TABLE [dbo].[SharableProject];
IF OBJECT_ID(N'[dbo].[Category]', N'U') IS NOT NULL DROP TABLE [dbo].[Category];
IF OBJECT_ID(N'[dbo].[CategorySymbol]', N'U') IS NOT NULL DROP TABLE [dbo].[CategorySymbol];
IF OBJECT_ID(N'[dbo].[Project]', N'U') IS NOT NULL DROP TABLE [dbo].[Project];
IF OBJECT_ID(N'[dbo].[Tag]', N'U') IS NOT NULL DROP TABLE [dbo].[Tag];
IF OBJECT_ID(N'[dbo].[LogItem]', N'U') IS NOT NULL DROP TABLE [dbo].[LogItem];
IF OBJECT_ID(N'[dbo].[TenantLead]', N'U') IS NOT NULL DROP TABLE [dbo].[TenantLead];
IF OBJECT_ID(N'[dbo].[LookupItem]', N'U') IS NOT NULL DROP TABLE [dbo].[LookupItem];
IF OBJECT_ID(N'[dbo].[User]', N'U') IS NOT NULL DROP TABLE [dbo].[User];
IF OBJECT_ID(N'[dbo].[Tenant]', N'U') IS NOT NULL DROP TABLE [dbo].[Tenant];
IF OBJECT_ID(N'[dbo].[GuidReservation]', N'U') IS NOT NULL DROP TABLE [dbo].[GuidReservation];
GO

CREATE TABLE [dbo].[GuidReservation]
(
    [IdGuidReservation] uniqueidentifier NOT NULL
        CONSTRAINT [DF_GuidReservation_IdGuidReservation] DEFAULT NEWSEQUENTIALID(),
    [EntityName] nvarchar(128) NOT NULL,
    [ReservedAt] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_GuidReservation_ReservedAt] DEFAULT SYSDATETIMEOFFSET(),
    [ReservedBy] nvarchar(128) NULL,
    [ConsumedAt] datetimeoffset(7) NULL,
    [IsConsumed] bit NOT NULL
        CONSTRAINT [DF_GuidReservation_IsConsumed] DEFAULT (0),
    CONSTRAINT [PK_GuidReservation] PRIMARY KEY CLUSTERED ([IdGuidReservation] ASC)
);
GO

CREATE TABLE [dbo].[Tenant]
(
    [IdTenant] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Tenant_IdTenant] DEFAULT NEWSEQUENTIALID(),
    [TenantName] nvarchar(200) NOT NULL,
    [TenantIdentifier] nvarchar(100) NULL,
    [Description] nvarchar(2000) NULL,
    [IsActive] bit NOT NULL
        CONSTRAINT [DF_Tenant_IsActive] DEFAULT (1),
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_Tenant_IsDeleted] DEFAULT (0),
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Tenant_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Tenant_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_Tenant] PRIMARY KEY CLUSTERED ([IdTenant] ASC)
);
GO

CREATE TABLE [dbo].[User]
(
    [IdUser] uniqueidentifier NOT NULL
        CONSTRAINT [DF_User_IdUser] DEFAULT NEWSEQUENTIALID(),
    [IdTenant] uniqueidentifier NOT NULL,
    [UserIdent] nvarchar(200) NOT NULL,
    [FirstName] nvarchar(100) NULL,
    [MiddleName] nvarchar(100) NULL,
    [LastName] nvarchar(200) NOT NULL,
    [EMail] nvarchar(100) NOT NULL,
    [IsAdmin] bit NOT NULL
        CONSTRAINT [DF_User_IsAdmin] DEFAULT (0),
    [IsActive] bit NOT NULL
        CONSTRAINT [DF_User_IsActive] DEFAULT (1),
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_User_IsDeleted] DEFAULT (0),
    [MustChangePassword] bit NOT NULL
        CONSTRAINT [DF_User_MustChangePassword] DEFAULT (0),
    [PasswordHash] nvarchar(512) NULL,
    [PasswordSalt] nvarchar(256) NULL,
    [PasswordChangedAt] datetimeoffset(7) NULL,
    [PreliminaryPasswordExpiresAt] datetimeoffset(7) NULL,
    [FailedLoginCount] int NOT NULL
        CONSTRAINT [DF_User_FailedLoginCount] DEFAULT (0),
    [LockoutUntil] datetimeoffset(7) NULL,
    [EmojiIndex] int NULL,
    [MaxProjects] int NULL,
    [LastLogin] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_User_LastLogin] DEFAULT SYSDATETIMEOFFSET(),
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_User_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_User_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_User_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_User_SyncStatus] DEFAULT (0),
    [DateDeactivated] datetimeoffset(7) NULL,
    [DateDeleted] datetimeoffset(7) NULL,
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([IdUser] ASC)
);
GO

CREATE TABLE [dbo].[LookupItem]
(
    [Id] uniqueidentifier NOT NULL
        CONSTRAINT [DF_LookupItem_Id] DEFAULT NEWSEQUENTIALID(),
    [Key] nvarchar(128) NOT NULL,
    [IdForeign] nvarchar(64) NULL,
    [StringValue] nvarchar(max) NULL,
    [BlobReference] nvarchar(64) NULL,
    [IntegerValue] bigint NULL,
    [DateValue] datetimeoffset(7) NULL,
    [DecimalValue] decimal(18, 4) NULL,
    CONSTRAINT [PK_LookupItem] PRIMARY KEY CLUSTERED ([Id] ASC)
);
GO

CREATE TABLE [dbo].[TenantLead]
(
    [IdTenantLead] uniqueidentifier NOT NULL
        CONSTRAINT [DF_TenantLead_IdTenantLead] DEFAULT NEWSEQUENTIALID(),
    [IdAssignedToUser] uniqueidentifier NULL,
    [TenantLeadName] nvarchar(200) NOT NULL,
    [OrganizationName] nvarchar(200) NULL,
    [FirstName] nvarchar(100) NULL,
    [LastName] nvarchar(100) NULL,
    [EMail] nvarchar(100) NULL,
    [Phone] nvarchar(50) NULL,
    [LeadSource] nvarchar(100) NULL,
    [LeadStatus] nvarchar(50) NULL,
    [Notes] nvarchar(2000) NULL,
    [DateFirstContact] datetimeoffset(7) NULL,
    [DateLastContact] datetimeoffset(7) NULL,
    [DateConverted] datetimeoffset(7) NULL,
    [IsConverted] bit NOT NULL
        CONSTRAINT [DF_TenantLead_IsConverted] DEFAULT (0),
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_TenantLead_IsDeleted] DEFAULT (0),
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_TenantLead_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_TenantLead_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT [PK_TenantLead] PRIMARY KEY CLUSTERED ([IdTenantLead] ASC)
);
GO

CREATE TABLE [dbo].[CategorySymbol]
(
    [IdCategorySymbol] uniqueidentifier NOT NULL
        CONSTRAINT [DF_CategorySymbol_IdCategorySymbol] DEFAULT NEWSEQUENTIALID(),
    [IdProject] uniqueidentifier NULL,
    [SymbolName] nvarchar(50) NOT NULL,
    [SymbolDescription] nvarchar(2000) NULL,
    [SymbolChar] nvarchar(4) NOT NULL,
    [FontName] nvarchar(100) NULL,
    [FontSize] real NULL,
    [SymbolOffsetX] real NULL,
    [SymbolOffsetY] real NULL,
    [SymbolColor] int NOT NULL
        CONSTRAINT [DF_CategorySymbol_SymbolColor] DEFAULT (0),
    CONSTRAINT [PK_CategorySymbol] PRIMARY KEY CLUSTERED ([IdCategorySymbol] ASC)
);
GO

CREATE TABLE [dbo].[Project]
(
    [IdProject] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Project_IdProject] DEFAULT NEWSEQUENTIALID(),
    [IdTenant] uniqueidentifier NOT NULL,
    [IdUser] uniqueidentifier NOT NULL,
    [IdSymbol] uniqueidentifier NULL,
    [ProjectName] nvarchar(200) NOT NULL,
    [ProjectType] int NOT NULL
        CONSTRAINT [DF_Project_ProjectType] DEFAULT (0),
    [ProjectNumber] int NULL,
    [ProjectDescription] nvarchar(2000) NULL,
    [ProjectIdentifier] nvarchar(100) NULL,
    [ProjectSymbolChar] nvarchar(4) NULL,
    [ProjectSymbolColor] int NOT NULL
        CONSTRAINT [DF_Project_ProjectSymbolColor] DEFAULT (0),
    [ProjectSymbolUrl] nvarchar(255) NULL,
    [StartOfProject] datetimeoffset(7) NULL,
    [EndOfProject] datetimeoffset(7) NULL,
    [IsActive] bit NOT NULL
        CONSTRAINT [DF_Project_IsActive] DEFAULT (0),
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_Project_IsDeleted] DEFAULT (0),
    [Scope] int NOT NULL
        CONSTRAINT [DF_Project_Scope] DEFAULT (0),
    [IsSystem] bit NOT NULL
        CONSTRAINT [DF_Project_IsSystem] DEFAULT (0),
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Project_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Project_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Project_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_Project_SyncStatus] DEFAULT (0),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_Project] PRIMARY KEY CLUSTERED ([IdProject] ASC)
);
GO

CREATE TABLE [dbo].[ProjectUserAssignment]
(
    [IdProjectUserAssignment] uniqueidentifier NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_IdProjectUserAssignment] DEFAULT NEWSEQUENTIALID(),
    [IdProject] uniqueidentifier NOT NULL,
    [IdUser] uniqueidentifier NOT NULL,
    [IdAssignedByUser] uniqueidentifier NULL,
    [AssignmentRole] int NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_AssignmentRole] DEFAULT (0),
    [CanBookTime] bit NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_CanBookTime] DEFAULT (1),
    [CanManageTasks] bit NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_CanManageTasks] DEFAULT (0),
    [CanManageProject] bit NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_CanManageProject] DEFAULT (0),
    [DisplayOrder] int NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_DisplayOrder] DEFAULT (0),
    [IsActive] bit NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_IsActive] DEFAULT (1),
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_IsDeleted] DEFAULT (0),
    [DateAssigned] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_DateAssigned] DEFAULT SYSDATETIMEOFFSET(),
    [DateRemoved] datetimeoffset(7) NULL,
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_ProjectUserAssignment_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_ProjectUserAssignment] PRIMARY KEY CLUSTERED ([IdProjectUserAssignment] ASC)
);
GO

CREATE TABLE [dbo].[Category]
(
    [IdCategory] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Category_IdCategory] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [IdSymbol] uniqueidentifier NULL,
    [CategoryName] nvarchar(50) NOT NULL,
    [CategoryDescription] nvarchar(2000) NULL,
    [DisplayOrder] int NOT NULL
        CONSTRAINT [DF_Category_DisplayOrder] DEFAULT (0),
    [IsPublic] bit NOT NULL
        CONSTRAINT [DF_Category_IsPublic] DEFAULT (0),
    CONSTRAINT [PK_Category] PRIMARY KEY CLUSTERED ([IdCategory] ASC)
);
GO

CREATE TABLE [dbo].[SharableProject]
(
    [IdSharableProject] uniqueidentifier NOT NULL
        CONSTRAINT [DF_SharableProject_IdSharableProject] DEFAULT NEWSEQUENTIALID(),
    [IdProject] uniqueidentifier NOT NULL,
    [IdUser] uniqueidentifier NOT NULL,
    [IdDefaultCategory] uniqueidentifier NOT NULL,
    [ProjectSymbolColor] int NOT NULL
        CONSTRAINT [DF_SharableProject_ProjectSymbolColor] DEFAULT (0),
    [InvitationMessage] nvarchar(512) NULL,
    [DateInvitationSent] datetimeoffset(7) NULL,
    [DateInvitationAccepted] datetimeoffset(7) NULL,
    [FirstUsed] datetimeoffset(7) NULL,
    [IsValid] bit NOT NULL
        CONSTRAINT [DF_SharableProject_IsValid] DEFAULT (0),
    [IsActive] bit NOT NULL
        CONSTRAINT [DF_SharableProject_IsActive] DEFAULT (0),
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_SharableProject_IsDeleted] DEFAULT (0),
    [CurrentRelationStatus] int NOT NULL
        CONSTRAINT [DF_SharableProject_CurrentRelationStatus] DEFAULT (0),
    [ProjectLocationNameSharedUser] nvarchar(200) NULL,
    [StartOfProjectSharedUser] datetimeoffset(7) NULL,
    [EndOfProjectSharedUser] datetimeoffset(7) NULL,
    [IsTasksCoOwner] bit NOT NULL
        CONSTRAINT [DF_SharableProject_IsTasksCoOwner] DEFAULT (0),
    [IsCoOwner] bit NOT NULL
        CONSTRAINT [DF_SharableProject_IsCoOwner] DEFAULT (0),
    [AllowOthersToSeeMyTasksAndTimes] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowOthersToSeeMyTasksAndTimes] DEFAULT (0),
    [AllowOthersToAssignTasksToMe] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowOthersToAssignTasksToMe] DEFAULT (0),
    [AllowOthersToDoMyReporting] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowOthersToDoMyReporting] DEFAULT (0),
    [AllowCoOwnerToDoMyReporting] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowCoOwnerToDoMyReporting] DEFAULT (0),
    [AllowCoOwnerToEditMyTimes] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowCoOwnerToEditMyTimes] DEFAULT (0),
    [AllowGeoFencingForTimeRecordings] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowGeoFencingForTimeRecordings] DEFAULT (0),
    [AllowGeoFencingForTasks] bit NOT NULL
        CONSTRAINT [DF_SharableProject_AllowGeoFencingForTasks] DEFAULT (0),
    [MetaInfo] nvarchar(4000) NULL,
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_SharableProject_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_SharableProject_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_SharableProject_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_SharableProject_SyncStatus] DEFAULT (0),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_SharableProject] PRIMARY KEY CLUSTERED ([IdSharableProject] ASC)
);
GO

CREATE TABLE [dbo].[TaskList]
(
    [IdTaskList] uniqueidentifier NOT NULL
        CONSTRAINT [DF_TaskList_IdTaskList] DEFAULT NEWSEQUENTIALID(),
    [IdProject] uniqueidentifier NOT NULL,
    [IdUser] uniqueidentifier NOT NULL,
    [IdSymbol] uniqueidentifier NULL,
    [TaskListName] nvarchar(50) NOT NULL,
    [TaskListDescription] nvarchar(2000) NULL,
    [DisplayOrder] int NOT NULL
        CONSTRAINT [DF_TaskList_DisplayOrder] DEFAULT (0),
    [IsPublic] bit NOT NULL
        CONSTRAINT [DF_TaskList_IsPublic] DEFAULT (0),
    CONSTRAINT [PK_TaskList] PRIMARY KEY CLUSTERED ([IdTaskList] ASC)
);
GO

CREATE TABLE [dbo].[TaskItem]
(
    [IdTaskItem] uniqueidentifier NOT NULL
        CONSTRAINT [DF_TaskItem_IdTaskItem] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [IdProject] uniqueidentifier NOT NULL,
    [IdTaskList] uniqueidentifier NULL,
    [IdSymbol] uniqueidentifier NULL,
    [TaskItemName] nvarchar(200) NOT NULL,
    [TaskItemDescription] nvarchar(2000) NULL,
    [QuickInfo] nvarchar(100) NULL,
    [DueDate] datetimeoffset(7) NULL,
    [TaskHoursBudget] int NULL,
    [Priority] int NOT NULL
        CONSTRAINT [DF_TaskItem_Priority] DEFAULT (0),
    [IsPrivateTask] bit NOT NULL
        CONSTRAINT [DF_TaskItem_IsPrivateTask] DEFAULT (0),
    [Scope] int NOT NULL
        CONSTRAINT [DF_TaskItem_Scope] DEFAULT (0),
    [IsActive] bit NOT NULL
        CONSTRAINT [DF_TaskItem_IsActive] DEFAULT (0),
    [IsCompleted] bit NOT NULL
        CONSTRAINT [DF_TaskItem_IsCompleted] DEFAULT (0),
    [DateCompleted] datetimeoffset(7) NULL,
    [IsDeleted] bit NOT NULL
        CONSTRAINT [DF_TaskItem_IsDeleted] DEFAULT (0),
    [IsForeign] bit NOT NULL
        CONSTRAINT [DF_TaskItem_IsForeign] DEFAULT (0),
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_TaskItem_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_TaskItem_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_TaskItem_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_TaskItem_SyncStatus] DEFAULT (0),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_TaskItem] PRIMARY KEY CLUSTERED ([IdTaskItem] ASC)
);
GO

CREATE TABLE [dbo].[TimeItem]
(
    [IdTimeItem] uniqueidentifier NOT NULL
        CONSTRAINT [DF_TimeItem_IdTimeItem] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [IdProject] uniqueidentifier NOT NULL,
    [IdNextItem] uniqueidentifier NULL,
    [IdPreviousItem] uniqueidentifier NULL,
    [IdTask] uniqueidentifier NULL,
    [IdCategory] uniqueidentifier NOT NULL,
    [ShortTitle] nvarchar(255) NULL,
    [Description] nvarchar(2000) NULL,
    [EventTime] datetimeoffset(7) NULL,
    [BookingDate] datetime2(7) NULL,
    [EventInfo] nvarchar(512) NULL,
    [EventTypeInfo] int NOT NULL
        CONSTRAINT [DF_TimeItem_EventTypeInfo] DEFAULT (0),
    [DurationToNext] time(7) NULL,
    [DurationTicksToNext] bigint NULL,
    [DurationToPrevious] time(7) NULL,
    [DurationTicksToPrevious] bigint NULL,
    [IdParentItem] uniqueidentifier NULL,
    [Scope] int NOT NULL
        CONSTRAINT [DF_TimeItem_Scope] DEFAULT (0),
    [IsItemCompleted] bit NOT NULL
        CONSTRAINT [DF_TimeItem_IsItemCompleted] DEFAULT (0),
    [IsItemDeleted] bit NOT NULL
        CONSTRAINT [DF_TimeItem_IsItemDeleted] DEFAULT (0),
    [IsStartAction] bit NOT NULL
        CONSTRAINT [DF_TimeItem_IsStartAction] DEFAULT (0),
    [IsEndAction] bit NOT NULL
        CONSTRAINT [DF_TimeItem_IsEndAction] DEFAULT (0),
    [Value] decimal(18, 4) NULL,
    [Priority] int NOT NULL
        CONSTRAINT [DF_TimeItem_Priority] DEFAULT (0),
    [DateItemAcceptedOrRejected] datetimeoffset(7) NULL,
    [MachineID] nvarchar(100) NULL,
    [IdItemAssignedToUser] uniqueidentifier NULL,
    [IdUserItemFrom] uniqueidentifier NULL,
    [DateItemFinished] datetimeoffset(7) NULL,
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_TimeItem_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_TimeItem_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_TimeItem_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_TimeItem_SyncStatus] DEFAULT (0),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_TimeItem] PRIMARY KEY CLUSTERED ([IdTimeItem] ASC)
);
GO

CREATE TABLE [dbo].[Tag]
(
    [IdTag] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Tag_IdTag] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [Tag] nvarchar(30) NOT NULL,
    [Description] nvarchar(2000) NULL,
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Tag_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Tag_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    CONSTRAINT [PK_Tag] PRIMARY KEY CLUSTERED ([IdTag] ASC)
);
GO

CREATE TABLE [dbo].[Note]
(
    [IdNote] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Note_IdNote] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [IdProject] uniqueidentifier NULL,
    [IdTask] uniqueidentifier NULL,
    [IdSymbol] uniqueidentifier NULL,
    [NoteMnemonic] nvarchar(100) NOT NULL,
    [Note] nvarchar(4000) NOT NULL,
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Note_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_Note_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_Note_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_Note_SyncStatus] DEFAULT (0),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_Note] PRIMARY KEY CLUSTERED ([IdNote] ASC)
);
GO

CREATE TABLE [dbo].[WebLink]
(
    [IdWebLink] uniqueidentifier NOT NULL
        CONSTRAINT [DF_WebLink_IdWebLink] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [IdProject] uniqueidentifier NULL,
    [IdSymbol] uniqueidentifier NULL,
    [Link] nvarchar(2000) NOT NULL,
    [Domain] nvarchar(200) NOT NULL,
    [Title] nvarchar(100) NULL,
    [Description] nvarchar(2000) NULL,
    [DateCreated] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_WebLink_DateCreated] DEFAULT SYSDATETIMEOFFSET(),
    [DateModified] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_WebLink_DateModified] DEFAULT SYSDATETIMEOFFSET(),
    [SyncId] uniqueidentifier NOT NULL
        CONSTRAINT [DF_WebLink_SyncId] DEFAULT NEWSEQUENTIALID(),
    [SyncStatus] int NOT NULL
        CONSTRAINT [DF_WebLink_SyncStatus] DEFAULT (0),
    [ExternalId] nvarchar(128) NULL,
    CONSTRAINT [PK_WebLink] PRIMARY KEY CLUSTERED ([IdWebLink] ASC)
);
GO

CREATE TABLE [dbo].[LogItem]
(
    [IdLogItem] uniqueidentifier NOT NULL
        CONSTRAINT [DF_LogItem_IdLogItem] DEFAULT NEWSEQUENTIALID(),
    [IdUser] uniqueidentifier NOT NULL,
    [Category] int NOT NULL
        CONSTRAINT [DF_LogItem_Category] DEFAULT (0),
    [LocalLogTime] datetimeoffset(7) NOT NULL
        CONSTRAINT [DF_LogItem_LocalLogTime] DEFAULT SYSDATETIMEOFFSET(),
    [Message] nvarchar(2000) NOT NULL,
    CONSTRAINT [PK_LogItem] PRIMARY KEY CLUSTERED ([IdLogItem] ASC)
);
GO

CREATE TABLE [dbo].[NoteTag]
(
    [IdNote] uniqueidentifier NOT NULL,
    [IdTag] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_NoteTag] PRIMARY KEY CLUSTERED ([IdNote] ASC, [IdTag] ASC)
);
GO

CREATE TABLE [dbo].[TaskItemTag]
(
    [IdTaskItem] uniqueidentifier NOT NULL,
    [IdTag] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_TaskItemTag] PRIMARY KEY CLUSTERED ([IdTaskItem] ASC, [IdTag] ASC)
);
GO

CREATE TABLE [dbo].[WebLinkTag]
(
    [IdWebLink] uniqueidentifier NOT NULL,
    [IdTag] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_WebLinkTag] PRIMARY KEY CLUSTERED ([IdWebLink] ASC, [IdTag] ASC)
);
GO

ALTER TABLE [dbo].[TenantLead] ADD CONSTRAINT [FK_TenantLead_User_IdAssignedToUser]
    FOREIGN KEY ([IdAssignedToUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[User] ADD CONSTRAINT [FK_User_Tenant_IdTenant]
    FOREIGN KEY ([IdTenant]) REFERENCES [dbo].[Tenant] ([IdTenant]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[CategorySymbol] ADD CONSTRAINT [FK_CategorySymbol_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Project] ADD CONSTRAINT [FK_Project_Tenant_IdTenant]
    FOREIGN KEY ([IdTenant]) REFERENCES [dbo].[Tenant] ([IdTenant]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Project] ADD CONSTRAINT [FK_Project_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Project] ADD CONSTRAINT [FK_Project_CategorySymbol_IdSymbol]
    FOREIGN KEY ([IdSymbol]) REFERENCES [dbo].[CategorySymbol] ([IdCategorySymbol]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Category] ADD CONSTRAINT [FK_Category_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Category] ADD CONSTRAINT [FK_Category_CategorySymbol_IdSymbol]
    FOREIGN KEY ([IdSymbol]) REFERENCES [dbo].[CategorySymbol] ([IdCategorySymbol]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[SharableProject] ADD CONSTRAINT [FK_SharableProject_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[SharableProject] ADD CONSTRAINT [FK_SharableProject_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[SharableProject] ADD CONSTRAINT [FK_SharableProject_Category_IdDefaultCategory]
    FOREIGN KEY ([IdDefaultCategory]) REFERENCES [dbo].[Category] ([IdCategory]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[ProjectUserAssignment] ADD CONSTRAINT [FK_ProjectUserAssignment_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[ProjectUserAssignment] ADD CONSTRAINT [FK_ProjectUserAssignment_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[ProjectUserAssignment] ADD CONSTRAINT [FK_ProjectUserAssignment_User_IdAssignedByUser]
    FOREIGN KEY ([IdAssignedByUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskList] ADD CONSTRAINT [FK_TaskList_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskList] ADD CONSTRAINT [FK_TaskList_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskList] ADD CONSTRAINT [FK_TaskList_CategorySymbol_IdSymbol]
    FOREIGN KEY ([IdSymbol]) REFERENCES [dbo].[CategorySymbol] ([IdCategorySymbol]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskItem] ADD CONSTRAINT [FK_TaskItem_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskItem] ADD CONSTRAINT [FK_TaskItem_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskItem] ADD CONSTRAINT [FK_TaskItem_TaskList_IdTaskList]
    FOREIGN KEY ([IdTaskList]) REFERENCES [dbo].[TaskList] ([IdTaskList]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskItem] ADD CONSTRAINT [FK_TaskItem_CategorySymbol_IdSymbol]
    FOREIGN KEY ([IdSymbol]) REFERENCES [dbo].[CategorySymbol] ([IdCategorySymbol]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_TaskItem_IdTask]
    FOREIGN KEY ([IdTask]) REFERENCES [dbo].[TaskItem] ([IdTaskItem]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_Category_IdCategory]
    FOREIGN KEY ([IdCategory]) REFERENCES [dbo].[Category] ([IdCategory]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_TimeItem_IdNextItem]
    FOREIGN KEY ([IdNextItem]) REFERENCES [dbo].[TimeItem] ([IdTimeItem]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_TimeItem_IdPreviousItem]
    FOREIGN KEY ([IdPreviousItem]) REFERENCES [dbo].[TimeItem] ([IdTimeItem]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_TimeItem_IdParentItem]
    FOREIGN KEY ([IdParentItem]) REFERENCES [dbo].[TimeItem] ([IdTimeItem]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_User_IdItemAssignedToUser]
    FOREIGN KEY ([IdItemAssignedToUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TimeItem] ADD CONSTRAINT [FK_TimeItem_User_IdUserItemFrom]
    FOREIGN KEY ([IdUserItemFrom]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Tag] ADD CONSTRAINT [FK_Tag_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Note] ADD CONSTRAINT [FK_Note_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Note] ADD CONSTRAINT [FK_Note_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Note] ADD CONSTRAINT [FK_Note_TaskItem_IdTask]
    FOREIGN KEY ([IdTask]) REFERENCES [dbo].[TaskItem] ([IdTaskItem]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[Note] ADD CONSTRAINT [FK_Note_CategorySymbol_IdSymbol]
    FOREIGN KEY ([IdSymbol]) REFERENCES [dbo].[CategorySymbol] ([IdCategorySymbol]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[WebLink] ADD CONSTRAINT [FK_WebLink_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[WebLink] ADD CONSTRAINT [FK_WebLink_Project_IdProject]
    FOREIGN KEY ([IdProject]) REFERENCES [dbo].[Project] ([IdProject]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[WebLink] ADD CONSTRAINT [FK_WebLink_CategorySymbol_IdSymbol]
    FOREIGN KEY ([IdSymbol]) REFERENCES [dbo].[CategorySymbol] ([IdCategorySymbol]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[LogItem] ADD CONSTRAINT [FK_LogItem_User_IdUser]
    FOREIGN KEY ([IdUser]) REFERENCES [dbo].[User] ([IdUser]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[NoteTag] ADD CONSTRAINT [FK_NoteTag_Note_IdNote]
    FOREIGN KEY ([IdNote]) REFERENCES [dbo].[Note] ([IdNote]) ON DELETE CASCADE;

ALTER TABLE [dbo].[NoteTag] ADD CONSTRAINT [FK_NoteTag_Tag_IdTag]
    FOREIGN KEY ([IdTag]) REFERENCES [dbo].[Tag] ([IdTag]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[TaskItemTag] ADD CONSTRAINT [FK_TaskItemTag_TaskItem_IdTaskItem]
    FOREIGN KEY ([IdTaskItem]) REFERENCES [dbo].[TaskItem] ([IdTaskItem]) ON DELETE CASCADE;

ALTER TABLE [dbo].[TaskItemTag] ADD CONSTRAINT [FK_TaskItemTag_Tag_IdTag]
    FOREIGN KEY ([IdTag]) REFERENCES [dbo].[Tag] ([IdTag]) ON DELETE NO ACTION;

ALTER TABLE [dbo].[WebLinkTag] ADD CONSTRAINT [FK_WebLinkTag_WebLink_IdWebLink]
    FOREIGN KEY ([IdWebLink]) REFERENCES [dbo].[WebLink] ([IdWebLink]) ON DELETE CASCADE;

ALTER TABLE [dbo].[WebLinkTag] ADD CONSTRAINT [FK_WebLinkTag_Tag_IdTag]
    FOREIGN KEY ([IdTag]) REFERENCES [dbo].[Tag] ([IdTag]) ON DELETE NO ACTION;
GO

CREATE UNIQUE INDEX [UX_User_UserIdent] ON [dbo].[User] ([UserIdent]);
CREATE UNIQUE INDEX [UX_User_EMail] ON [dbo].[User] ([EMail]);
CREATE INDEX [IX_User_IdTenant] ON [dbo].[User] ([IdTenant]);
CREATE INDEX [IX_User_LastName] ON [dbo].[User] ([LastName]);

CREATE UNIQUE INDEX [UX_Tenant_TenantName] ON [dbo].[Tenant] ([TenantName]);
CREATE INDEX [IX_Tenant_TenantIdentifier] ON [dbo].[Tenant] ([TenantIdentifier]);

CREATE INDEX [IX_GuidReservation_EntityName_IsConsumed] ON [dbo].[GuidReservation] ([EntityName], [IsConsumed]);
CREATE INDEX [IX_GuidReservation_ReservedAt] ON [dbo].[GuidReservation] ([ReservedAt]);

CREATE INDEX [IX_LookupItem_Key_IdForeign] ON [dbo].[LookupItem] ([Key], [IdForeign]);

CREATE INDEX [IX_TenantLead_EMail] ON [dbo].[TenantLead] ([EMail]);
CREATE INDEX [IX_TenantLead_TenantLeadName] ON [dbo].[TenantLead] ([TenantLeadName]);
CREATE INDEX [IX_TenantLead_IdAssignedToUser] ON [dbo].[TenantLead] ([IdAssignedToUser]);

CREATE INDEX [IX_CategorySymbol_IdProject] ON [dbo].[CategorySymbol] ([IdProject]);
CREATE INDEX [IX_CategorySymbol_SymbolName] ON [dbo].[CategorySymbol] ([SymbolName]);

CREATE UNIQUE INDEX [UX_Category_IdUser_CategoryName] ON [dbo].[Category] ([IdUser], [CategoryName]);
CREATE INDEX [IX_Category_IdSymbol] ON [dbo].[Category] ([IdSymbol]);

CREATE INDEX [IX_Project_IdUser] ON [dbo].[Project] ([IdUser]);
CREATE INDEX [IX_Project_IdTenant] ON [dbo].[Project] ([IdTenant]);
CREATE INDEX [IX_Project_IdSymbol] ON [dbo].[Project] ([IdSymbol]);
CREATE INDEX [IX_Project_ProjectIdentifier] ON [dbo].[Project] ([ProjectIdentifier]);

CREATE UNIQUE INDEX [UX_SharableProject_IdProject_IdUser] ON [dbo].[SharableProject] ([IdProject], [IdUser]);
CREATE INDEX [IX_SharableProject_IdUser] ON [dbo].[SharableProject] ([IdUser]);
CREATE INDEX [IX_SharableProject_IdDefaultCategory] ON [dbo].[SharableProject] ([IdDefaultCategory]);

CREATE UNIQUE INDEX [UX_ProjectUserAssignment_IdProject_IdUser] ON [dbo].[ProjectUserAssignment] ([IdProject], [IdUser]);
CREATE INDEX [IX_ProjectUserAssignment_IdUser] ON [dbo].[ProjectUserAssignment] ([IdUser]);
CREATE INDEX [IX_ProjectUserAssignment_IdAssignedByUser] ON [dbo].[ProjectUserAssignment] ([IdAssignedByUser]);
CREATE INDEX [IX_ProjectUserAssignment_IsActive_IsDeleted] ON [dbo].[ProjectUserAssignment] ([IsActive], [IsDeleted]);

CREATE UNIQUE INDEX [UX_TaskList_IdProject_TaskListName] ON [dbo].[TaskList] ([IdProject], [TaskListName]);
CREATE INDEX [IX_TaskList_IdUser] ON [dbo].[TaskList] ([IdUser]);
CREATE INDEX [IX_TaskList_IdSymbol] ON [dbo].[TaskList] ([IdSymbol]);

CREATE INDEX [IX_TaskItem_IdUser] ON [dbo].[TaskItem] ([IdUser]);
CREATE INDEX [IX_TaskItem_IdProject] ON [dbo].[TaskItem] ([IdProject]);
CREATE INDEX [IX_TaskItem_IdTaskList] ON [dbo].[TaskItem] ([IdTaskList]);
CREATE INDEX [IX_TaskItem_IdSymbol] ON [dbo].[TaskItem] ([IdSymbol]);
CREATE INDEX [IX_TaskItem_DueDate] ON [dbo].[TaskItem] ([DueDate]);

CREATE INDEX [IX_TimeItem_BookingDate] ON [dbo].[TimeItem] ([BookingDate]);
CREATE INDEX [IX_TimeItem_EventTime] ON [dbo].[TimeItem] ([EventTime]);
CREATE INDEX [IX_TimeItem_IdUser] ON [dbo].[TimeItem] ([IdUser]);
CREATE INDEX [IX_TimeItem_IdProject] ON [dbo].[TimeItem] ([IdProject]);
CREATE INDEX [IX_TimeItem_IdTask] ON [dbo].[TimeItem] ([IdTask]);
CREATE INDEX [IX_TimeItem_IdCategory] ON [dbo].[TimeItem] ([IdCategory]);
CREATE INDEX [IX_TimeItem_IdNextItem] ON [dbo].[TimeItem] ([IdNextItem]);
CREATE INDEX [IX_TimeItem_IdPreviousItem] ON [dbo].[TimeItem] ([IdPreviousItem]);
CREATE INDEX [IX_TimeItem_IdParentItem] ON [dbo].[TimeItem] ([IdParentItem]);
CREATE INDEX [IX_TimeItem_IdUser_BookingDate_EventTypeInfo] ON [dbo].[TimeItem] ([IdUser], [BookingDate], [EventTypeInfo]);

CREATE UNIQUE INDEX [UX_Tag_IdUser_Tag] ON [dbo].[Tag] ([IdUser], [Tag]);

CREATE INDEX [IX_Note_IdUser] ON [dbo].[Note] ([IdUser]);
CREATE INDEX [IX_Note_IdProject] ON [dbo].[Note] ([IdProject]);
CREATE INDEX [IX_Note_IdTask] ON [dbo].[Note] ([IdTask]);
CREATE INDEX [IX_Note_IdSymbol] ON [dbo].[Note] ([IdSymbol]);
CREATE INDEX [IX_Note_NoteMnemonic] ON [dbo].[Note] ([NoteMnemonic]);

CREATE INDEX [IX_WebLink_IdUser] ON [dbo].[WebLink] ([IdUser]);
CREATE INDEX [IX_WebLink_IdProject] ON [dbo].[WebLink] ([IdProject]);
CREATE INDEX [IX_WebLink_IdSymbol] ON [dbo].[WebLink] ([IdSymbol]);
CREATE INDEX [IX_WebLink_Domain] ON [dbo].[WebLink] ([Domain]);
CREATE INDEX [IX_WebLink_Title] ON [dbo].[WebLink] ([Title]);

CREATE INDEX [IX_LogItem_IdUser] ON [dbo].[LogItem] ([IdUser]);
CREATE INDEX [IX_LogItem_LocalLogTime] ON [dbo].[LogItem] ([LocalLogTime]);

CREATE INDEX [IX_NoteTag_IdTag] ON [dbo].[NoteTag] ([IdTag]);
CREATE INDEX [IX_TaskItemTag_IdTag] ON [dbo].[TaskItemTag] ([IdTag]);
CREATE INDEX [IX_WebLinkTag_IdTag] ON [dbo].[WebLinkTag] ([IdTag]);
GO

CREATE PROCEDURE [dbo].[ReserveSequentialGuids]
    @EntityName nvarchar(128),
    @BlockSize int = 1,
    @ReservedBy nvarchar(128) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @EntityName IS NULL OR LEN(LTRIM(RTRIM(@EntityName))) = 0
    BEGIN
        RAISERROR(N'EntityName must be provided.', 16, 1);
        RETURN;
    END;

    IF @BlockSize IS NULL OR @BlockSize < 1 OR @BlockSize > 1000
    BEGIN
        RAISERROR(N'BlockSize must be between 1 and 1000.', 16, 1);
        RETURN;
    END;

    DECLARE @ReservedGuids TABLE
    (
        [ReservationOrder] int IDENTITY(1, 1) NOT NULL,
        [GuidValue] uniqueidentifier NOT NULL
    );

    DECLARE @Counter int = 0;

    WHILE @Counter < @BlockSize
    BEGIN
        INSERT INTO [dbo].[GuidReservation] ([EntityName], [ReservedBy])
            OUTPUT inserted.[IdGuidReservation] INTO @ReservedGuids ([GuidValue])
            VALUES (@EntityName, @ReservedBy);

        SET @Counter = @Counter + 1;
    END;

    SELECT
        [GuidValue],
        @EntityName AS [EntityName],
        @ReservedBy AS [ReservedBy]
    FROM @ReservedGuids
    ORDER BY [ReservationOrder];
END;
GO
