
-- =============================================
-- Wafek Workflow System - Stored Procedures
-- =============================================

PRINT N'ملاحظة: يُفضّل استخدام SQL/Wafek_Workflow_Schema.sql كتهيئة موحدة.';
PRINT N'هذا الملف قديم وسيتم الإبقاء عليه للتوافق فقط.';
GO

-- 1) Approve_CreateFirstProcess — النسخة المرنة (نفس الموجودة في Wafek_Workflow_Schema.sql)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_CreateFirstProcess]') AND type in (N'P', N'PC'))
DROP PROCEDURE [dbo].[Approve_CreateFirstProcess]
GO

CREATE PROCEDURE [dbo].[Approve_CreateFirstProcess]
	@Param1 nvarchar(50),
	@Param2 nvarchar(50) = NULL,
	@Param3 nvarchar(50) = NULL
AS
BEGIN
	SET NOCOUNT ON;

	DECLARE @SourceTable nvarchar(50)
	DECLARE @SourceID uniqueidentifier
	DECLARE @TriggerEvent nvarchar(50)

	IF TRY_CAST(@Param1 AS uniqueidentifier) IS NOT NULL
	BEGIN
		SET @SourceID = CAST(@Param1 AS uniqueidentifier)
		SET @SourceTable = 'TBL010'
		SET @TriggerEvent = 'OnAfterInsert'
	END
	ELSE
	BEGIN
		SET @SourceTable = @Param1
		IF TRY_CAST(@Param2 AS uniqueidentifier) IS NOT NULL
			SET @SourceID = CAST(@Param2 AS uniqueidentifier)
		ELSE
			SET @SourceID = NULL
		SET @TriggerEvent = ISNULL(@Param3, 'OnAfterInsert')
	END

	IF @SourceID IS NOT NULL
	BEGIN
		DECLARE @WF_Id int, @ConditionSql nvarchar(max), @SpecificGuid uniqueidentifier
		DECLARE wf_cursor CURSOR FOR 
		SELECT Id, ConditionSql, SpecificDocTypeGuid 
		FROM WF_Definitions 
		WHERE IsActive = 1 
		  AND SourceTable = @SourceTable 
		  AND (@TriggerEvent IS NULL OR TriggerEvent = @TriggerEvent)

		OPEN wf_cursor
		FETCH NEXT FROM wf_cursor INTO @WF_Id, @ConditionSql, @SpecificGuid

		WHILE @@FETCH_STATUS = 0
		BEGIN
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

-- 2. Approve_ExecuteStep (The Step Executor)
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
		-- Log Action
		UPDATE WF_Logs 
		SET LastActionLog = 'Executing Step ' + CAST(@StepOrder as nvarchar(10)) + ': ' + @ActionType,
		    LastUpdatedDate = GETDATE()
		WHERE WorkflowDefinitionId = @WF_Id AND SourceRecordId = @SourceID

		-- Execution Logic (Placeholder for now)
		-- In real scenario, this would call other procs or external services
		
		-- Move to next step if auto-complete
		-- EXEC Approve_ExecuteStep @WF_Id, @SourceID, @StepOrder + 1
	END
END
GO
