-- =============================================
-- Wafek Workflow System - Unified Schema (Full & Clean)
-- =============================================

-- 1. WF_Definitions
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_Definitions]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WF_Definitions](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[Name] [nvarchar](200) NOT NULL,
	[Description] [nvarchar](500) NULL,
	[IsActive] [bit] NOT NULL DEFAULT ((1)),
	[SourceTable] [nvarchar](50) NOT NULL,
	[SpecificDocTypeGuid] [uniqueidentifier] NULL,
	[TriggerEvent] [nvarchar](50) NOT NULL,
	[ConditionSql] [nvarchar](max) NULL,
	[EmailFormatQuery] [nvarchar](max) NULL,
 CONSTRAINT [PK_WF_Definitions] PRIMARY KEY CLUSTERED ([Id] ASC)
)
END
GO

-- 2. WF_Steps
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_Steps]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WF_Steps](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[WorkflowDefinitionId] [int] NOT NULL,
	[StepOrder] [int] NOT NULL,
	[StepName] [nvarchar](100) NOT NULL,
	[ExecutionPhase] [nvarchar](50) NULL DEFAULT 'After',
	[StepCondition] [nvarchar](500) NULL,
	[ActionType] [nvarchar](50) NOT NULL,
	[SelectedValue] [nvarchar](max) NULL,
	[ActionConfigJson] [nvarchar](max) NULL,
	[TimeoutHours] [int] NULL,
	[TimeoutActionJson] [nvarchar](max) NULL,
 CONSTRAINT [PK_WF_Steps] PRIMARY KEY CLUSTERED ([Id] ASC),
 CONSTRAINT [FK_WF_Steps_WF_Definitions] FOREIGN KEY([WorkflowDefinitionId]) REFERENCES [dbo].[WF_Definitions] ([Id]) ON DELETE CASCADE
)
END
GO

-- 3. WF_Logs
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_Logs]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WF_Logs](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[WorkflowDefinitionId] [int] NOT NULL,
	[SourceRecordId] [uniqueidentifier] NOT NULL,
	[CurrentStepOrder] [int] NOT NULL,
	[Status] [nvarchar](50) NOT NULL,
	[CreatedDate] [datetime] NOT NULL DEFAULT (getdate()),
	[LastUpdatedDate] [datetime] NULL,
	[LastActionLog] [nvarchar](max) NULL,
 CONSTRAINT [PK_WF_Logs] PRIMARY KEY CLUSTERED ([Id] ASC)
)
END
GO

-- 4. WF_EmailCommands
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailCommands]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WF_EmailCommands](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CommandCode] [nvarchar](50) NOT NULL,
	[Description] [nvarchar](200) NOT NULL,
	[IsActive] [bit] NOT NULL DEFAULT ((1)),
	[ActionType] [nvarchar](50) NOT NULL,
	[ExecutionContent] [nvarchar](max) NOT NULL,
	[ResponseSubject] [nvarchar](200) NULL,
	[ResponseBodyTemplate] [nvarchar](max) NULL,
	[CreatedDate] [datetime] NOT NULL DEFAULT (getdate()),
 CONSTRAINT [PK_WF_EmailCommands] PRIMARY KEY CLUSTERED ([Id] ASC)
)
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WF_EmailCommands_Code' AND object_id = OBJECT_ID('WF_EmailCommands'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_WF_EmailCommands_Code] ON [dbo].[WF_EmailCommands] ([CommandCode] ASC);
END
GO

-- 5. WF_EmailLogs (استقبال الميل من IMAP + المحاكاة)
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WF_EmailLogs](
	[Id] [bigint] IDENTITY(1,1) NOT NULL,
	[SenderEmail] [nvarchar](200) NOT NULL,
	[ReceivedDate] [datetime] NOT NULL DEFAULT (getdate()),
	[Subject] [nvarchar](500) NULL,
	[DetectedCommand] [nvarchar](50) NULL,
	[ExecutionStatus] [nvarchar](50) NOT NULL,
	[ResultMessage] [nvarchar](max) NULL,
	[ImapMessageId] [nvarchar](500) NULL,
 CONSTRAINT [PK_WF_EmailLogs] PRIMARY KEY CLUSTERED ([Id] ASC)
)
END
GO

-- إضافة ImapMessageId إن كان الجدول موجوداً بدونها (جداول قديمة)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_EmailLogs', 'ImapMessageId') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [ImapMessageId] [nvarchar](500) NULL;
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WF_EmailLogs_ImapMessageId' AND object_id = OBJECT_ID('WF_EmailLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_WF_EmailLogs_ImapMessageId] ON [dbo].[WF_EmailLogs] ([ImapMessageId]) WHERE [ImapMessageId] IS NOT NULL;
END
GO

-- =============================================
-- STORED PROCEDURES
-- =============================================

-- 6. Approve_CreateFirstProcess (The Trigger Engine - FLEXIBLE)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_CreateFirstProcess]') AND type in (N'P', N'PC'))
DROP PROCEDURE [dbo].[Approve_CreateFirstProcess]
GO

CREATE PROCEDURE [dbo].[Approve_CreateFirstProcess]
	@Param1 nvarchar(50),  -- Could be TableName OR GUID as String
	@Param2 nvarchar(50) = NULL, -- Could be GUID OR Event
	@Param3 nvarchar(50) = NULL  -- Could be Event OR NULL
AS
BEGIN
	SET NOCOUNT ON;

	-- Variables to hold resolved values
	DECLARE @SourceTable nvarchar(50)
	DECLARE @SourceID uniqueidentifier
	DECLARE @TriggerEvent nvarchar(50)

	-- Logic to detect parameter types
	-- Check if Param1 is a GUID (Legacy Call: GUID, 1)
	IF TRY_CAST(@Param1 AS uniqueidentifier) IS NOT NULL
	BEGIN
		-- Scenario: Exec Approve_CreateFirstProcess @GUID, 1
		SET @SourceID = CAST(@Param1 AS uniqueidentifier)
		SET @SourceTable = 'TBL010' -- Default to Bonds Transaction
		SET @TriggerEvent = 'OnAfterInsert' -- Default Event
	END
	ELSE
	BEGIN
		-- Scenario: Exec Approve_CreateFirstProcess 'TBL010', @GUID, 'OnAfterInsert'
		SET @SourceTable = @Param1
		IF TRY_CAST(@Param2 AS uniqueidentifier) IS NOT NULL
			SET @SourceID = CAST(@Param2 AS uniqueidentifier)
		ELSE
			SET @SourceID = NULL -- Error case

		SET @TriggerEvent = ISNULL(@Param3, 'OnAfterInsert')
	END

	-- Proceed if we have valid inputs
	IF @SourceID IS NOT NULL
	BEGIN
		DECLARE @WF_Id int, @ConditionSql nvarchar(max), @SpecificGuid uniqueidentifier
		
		DECLARE wf_cursor CURSOR FOR 
		SELECT Id, ConditionSql, SpecificDocTypeGuid 
		FROM WF_Definitions 
		WHERE IsActive = 1 
		  AND SourceTable = @SourceTable 
		  -- If Event is passed, filter by it. If not, match any.
		  AND (@TriggerEvent IS NULL OR TriggerEvent = @TriggerEvent)

		OPEN wf_cursor
		FETCH NEXT FROM wf_cursor INTO @WF_Id, @ConditionSql, @SpecificGuid

		WHILE @@FETCH_STATUS = 0
		BEGIN
			-- Log & Execute
			INSERT INTO WF_Logs (WorkflowDefinitionId, SourceRecordId, CurrentStepOrder, Status, CreatedDate)
			VALUES (@WF_Id, @SourceID, 1, 'Pending', GETDATE())

			EXEC Approve_ExecuteStep @WF_Id, @SourceID, 1

			FETCH NEXT FROM wf_cursor INTO @WF_Id, @ConditionSql, @SpecificGuid
		END

		CLOSE wf_cursor
		DEALLOCATE wf_cursor
	END
END
GO

-- 7. Approve_ExecuteStep
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_ExecuteStep]') AND type in (N'P', N'PC'))
DROP PROCEDURE [dbo].[Approve_ExecuteStep]
GO

CREATE PROCEDURE [dbo].[Approve_ExecuteStep]
	@WF_Id int,
	@SourceID uniqueidentifier,
	@StepOrder int
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @ActionType nvarchar(50), @SelectedValue nvarchar(max)
	
	SELECT @ActionType = ActionType, @SelectedValue = SelectedValue
	FROM WF_Steps
	WHERE WorkflowDefinitionId = @WF_Id AND StepOrder = @StepOrder

	IF @ActionType IS NOT NULL
	BEGIN
		UPDATE WF_Logs 
		SET LastActionLog = 'Executing Step ' + CAST(@StepOrder as nvarchar(10)) + ': ' + @ActionType,
		    LastUpdatedDate = GETDATE()
		WHERE WorkflowDefinitionId = @WF_Id AND SourceRecordId = @SourceID
	END
END
GO

-- 8. Approve_ProcessResponse (معالجة الرد: موافق، غير موفق، يؤجل)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_ProcessResponse]') AND type in (N'P', N'PC'))
DROP PROCEDURE [dbo].[Approve_ProcessResponse]
GO

CREATE PROCEDURE [dbo].[Approve_ProcessResponse]
	@LogId bigint,
	@ResponseType nvarchar(50)  -- 'Approved', 'Rejected', 'Postponed'
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @WF_Id int, @SourceId uniqueidentifier, @SourceTable nvarchar(50), @CurrentStep int
	DECLARE @NextStepOrder int

	SELECT @WF_Id = L.WorkflowDefinitionId, @SourceId = L.SourceRecordId, @CurrentStep = L.CurrentStepOrder,
	       @SourceTable = D.SourceTable
	FROM WF_Logs L
	JOIN WF_Definitions D ON D.Id = L.WorkflowDefinitionId
	WHERE L.Id = @LogId

	IF @SourceId IS NULL
	BEGIN
		RAISERROR('Log entry not found', 16, 1)
		RETURN
	END

	IF EXISTS (SELECT 1 FROM WF_Logs WHERE Id = @LogId AND Status IN ('Approved', 'Rejected', 'Completed'))
		RETURN

	IF @ResponseType = 'Approved'
	BEGIN
		UPDATE WF_Logs SET Status = 'Approved', LastActionLog = N'تمت الموافقة', LastUpdatedDate = GETDATE() WHERE Id = @LogId

		SELECT @NextStepOrder = MIN(StepOrder) FROM WF_Steps WHERE WorkflowDefinitionId = @WF_Id AND StepOrder > @CurrentStep

		IF @NextStepOrder IS NOT NULL
		BEGIN
			UPDATE WF_Logs SET CurrentStepOrder = @NextStepOrder, Status = 'Pending', LastActionLog = N'الانتقال للخطوة التالية' WHERE Id = @LogId
			EXEC Approve_ExecuteStep @WF_Id, @SourceId, @NextStepOrder
		END
		ELSE
			UPDATE WF_Logs SET LastActionLog = N'تمت الموافقة — اكتمال الورك فلو', Status = 'Approved' WHERE Id = @LogId

		IF OBJECT_ID('dbo.Approve_OnApproved', 'P') IS NOT NULL
			EXEC Approve_OnApproved @LogId, @SourceTable, @SourceId
	END
	ELSE IF @ResponseType = 'Rejected'
	BEGIN
		UPDATE WF_Logs SET Status = 'Rejected', LastActionLog = N'تم الرفض', LastUpdatedDate = GETDATE() WHERE Id = @LogId
		IF OBJECT_ID('dbo.Approve_OnRejected', 'P') IS NOT NULL
			EXEC Approve_OnRejected @LogId, @SourceTable, @SourceId
	END
	ELSE IF @ResponseType = 'Postponed'
	BEGIN
		UPDATE WF_Logs SET Status = 'Postponed', LastActionLog = N'تم التأجيل', LastUpdatedDate = GETDATE() WHERE Id = @LogId
		IF OBJECT_ID('dbo.Approve_OnPostponed', 'P') IS NOT NULL
			EXEC Approve_OnPostponed @LogId, @SourceTable, @SourceId
	END
	ELSE
		RAISERROR('Invalid ResponseType', 16, 1)
END
GO
