
-- =============================================
-- Wafek Workflow System - Stored Procedures
-- =============================================

-- 1. Approve_CreateFirstProcess (The Trigger Engine)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_CreateFirstProcess]') AND type in (N'P', N'PC'))
DROP PROCEDURE [dbo].[Approve_CreateFirstProcess]
GO

CREATE PROCEDURE [dbo].[Approve_CreateFirstProcess]
	@SourceTable nvarchar(50),
	@SourceID uniqueidentifier,
	@TriggerEvent nvarchar(50) -- 'After Insert', 'After Update'
AS
BEGIN
	SET NOCOUNT ON;

	-- 1. Find Matching Workflow Definition
	DECLARE @WF_Id int, @ConditionSql nvarchar(max), @SpecificGuid uniqueidentifier
	
	DECLARE wf_cursor CURSOR FOR 
	SELECT Id, ConditionSql, SpecificDocTypeGuid 
	FROM WF_Definitions 
	WHERE IsActive = 1 
	  AND SourceTable = @SourceTable 
	  AND TriggerEvent = @TriggerEvent

	OPEN wf_cursor
	FETCH NEXT FROM wf_cursor INTO @WF_Id, @ConditionSql, @SpecificGuid

	WHILE @@FETCH_STATUS = 0
	BEGIN
		-- TODO: Execute Dynamic SQL to check ConditionSql against SourceID
		-- For now, we assume condition is met if empty
		
		-- 2. Create Initial Log Entry
		INSERT INTO WF_Logs (WorkflowDefinitionId, SourceRecordId, CurrentStepOrder, Status, CreatedDate)
		VALUES (@WF_Id, @SourceID, 1, 'Pending', GETDATE())

		-- 3. Execute First Step
		EXEC Approve_ExecuteStep @WF_Id, @SourceID, 1

		FETCH NEXT FROM wf_cursor INTO @WF_Id, @ConditionSql, @SpecificGuid
	END

	CLOSE wf_cursor
	DEALLOCATE wf_cursor
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
