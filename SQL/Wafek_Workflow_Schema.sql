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

-- إضافة أعمدة ربط رد الموافقة (#1#/#2#/#3#) إن لم تكن موجودة (ترقية)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_EmailLogs', 'InReplyTo') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [InReplyTo] [nvarchar](500) NULL;
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_EmailLogs', 'ApprovalResponse') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [ApprovalResponse] [nvarchar](50) NULL;
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WF_EmailLogs_InReplyTo' AND object_id = OBJECT_ID('WF_EmailLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_WF_EmailLogs_InReplyTo] ON [dbo].[WF_EmailLogs] ([InReplyTo]) WHERE [InReplyTo] IS NOT NULL;
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

-- 9. Approve_OnApproved / Approve_OnRejected (تحديث المستند حسب SourceTable)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_OnApproved]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_OnApproved]
GO
CREATE PROCEDURE [dbo].[Approve_OnApproved]
    @LogId bigint, @SourceTable nvarchar(50), @SourceId uniqueidentifier
AS
BEGIN SET NOCOUNT ON;
    DECLARE @col nvarchar(50), @sql nvarchar(500)
    IF @SourceTable IN ('TBL010','TBL022','TBL085')
    BEGIN
        IF   COL_LENGTH('dbo.' + @SourceTable, 'Security')      IS NOT NULL SET @col = 'Security'
        ELSE IF COL_LENGTH('dbo.' + @SourceTable, 'SecurityLevel') IS NOT NULL SET @col = 'SecurityLevel'
        IF @col IS NOT NULL
        BEGIN
            SET @sql = 'UPDATE ' + @SourceTable + ' SET ' + @col + ' = 2 WHERE CardGuide = @id'
            EXEC sp_executesql @sql, N'@id uniqueidentifier', @id = @SourceId
        END
    END
END
GO

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_OnRejected]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_OnRejected]
GO
CREATE PROCEDURE [dbo].[Approve_OnRejected]
    @LogId bigint, @SourceTable nvarchar(50), @SourceId uniqueidentifier
AS
BEGIN SET NOCOUNT ON;
    DECLARE @sql nvarchar(500)
    IF @SourceTable IN ('TBL010','TBL022','TBL085')
        AND COL_LENGTH('dbo.' + @SourceTable, 'RejectReason') IS NOT NULL
    BEGIN
        SET @sql = N'UPDATE ' + @SourceTable + N' SET RejectReason = N''مرفوض من الموافق'' WHERE CardGuide = @id'
        EXEC sp_executesql @sql, N'@id uniqueidentifier', @id = @SourceId
    END
END
GO

-- 10. مسار إكسترا (SQL Mail) — اسم منفصل لتفادي التعارض مع محرك الـWorkflow
-- ملاحظة: إذا كان عندك Trigger/حدث قديم يستدعي Approve_CreateFirstProcess @GUID, 1 (إكسترا)، غيّر الاستدعاء إلى هذا الإجراء.
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_CreateFirstProcess_XtraMail]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_CreateFirstProcess_XtraMail];
GO
CREATE PROCEDURE [dbo].[Approve_CreateFirstProcess_XtraMail](@Guide uniqueidentifier, @Type int)
AS
BEGIN
    DECLARE @StageGuide uniqueidentifier
    DECLARE @sender uniqueidentifier
    DECLARE @UserNameSender nvarchar(max)
    DECLARE @Note nvarchar(max)
    DECLARE @CardNumber nvarchar(max)
    DECLARE @MainGuid uniqueidentifier
    DECLARE @emailSendDate nvarchar(max)
    DECLARE @CompanyName nvarchar(Max)
    DECLARE @CompanyPhone nvarchar(Max)
    DECLARE @SendToName nvarchar(max)
    DECLARE @TypeGuide uniqueidentifier
    DECLARE @PrcGuide uniqueidentifier
    DECLARE @UsGuide uniqueidentifier
    DECLARE @_emilusr nvarchar(Max)
    DECLARE @_lang int
    DECLARE @EntryName nvarchar(max)
    DECLARE @AgentGuide uniqueidentifier
    DECLARE @subject nvarchar(max)
    DECLARE @body nvarchar(Max)
    DECLARE @DetailsBlock nvarchar(max) = N''

    SET @emailSendDate = CONVERT(nvarchar(30), GETDATE(), 120)
    SET @CompanyName = (SELECT CompanyName FROM Approve_Email WHERE ID = 1)
    SET @CompanyPhone = (SELECT CompanyPhone FROM Approve_Email WHERE ID = 1)

    IF (@Type = 1)
    BEGIN
        SET @CardNumber = (SELECT bondnumber FROM tbl010 WHERE CardGuide = @Guide)
        SET @MainGuid = (SELECT Mainguide FROM tbl010 WHERE CardGuide = @Guide)
        SET @sender = (SELECT ByUser FROM tbl010 WHERE CardGuide = @Guide)
        IF (@sender IS NULL) SET @sender = (SELECT AgentGuide FROM tbl010 WHERE CardGuide = @Guide)
        SET @Note = (SELECT Notes FROM tbl010 WHERE CardGuide = @Guide)

        SELECT @DetailsBlock = N'<p style="margin:10px 0;padding:8px;background:#fff;border:1px solid #ddd;border-radius:6px;text-align:right;direction:rtl">عدد الأسطر: <b>' + CAST(COUNT(*) AS nvarchar(10)) + N'</b> | المبلغ الإجمالي: <b>' + ISNULL(CAST(SUM(ISNULL(DebitRate,0)+ISNULL(CreditRate,0)) AS nvarchar(30)), N'0') + N'</b></p>'
        FROM TBL038 WHERE MainGuide = @Guide
        IF (@DetailsBlock IS NULL) SET @DetailsBlock = N''
    END
    ELSE IF (@Type = 2)
    BEGIN
        SET @CardNumber = (SELECT BillNumber FROM tbl022 WHERE CardGuide = @Guide)
        SET @MainGuid = (SELECT Mainguide FROM tbl022 WHERE CardGuide = @Guide)
        SET @sender = (SELECT ByUser FROM tbl022 WHERE CardGuide = @Guide)
        SET @Note = (SELECT Notes FROM tbl022 WHERE CardGuide = @Guide)
    END
    ELSE IF (@Type = 3)
    BEGIN
        SET @CardNumber = (SELECT CardNumber FROM tbl085 WHERE CardGuide = @Guide)
        SET @TypeGuide = (SELECT TypeGuide FROM tbl085 WHERE CardGuide = @Guide)
        SET @MainGuid = (SELECT MainGuide FROM tbl034 WHERE ID = (SELECT MainID FROM tbl035 WHERE PropertyValue = CONVERT(nvarchar(max), @TypeGuide)))
        SET @sender = (SELECT ByUser FROM tbl085 WHERE CardGuide = @Guide)
        SET @Note = ''
    END
    ELSE IF (@Type = 4)
    BEGIN
        SET @CardNumber = (SELECT IntValue01 FROM tbl014 WHERE CardGuide = @Guide)
        SET @MainGuid = (SELECT RelatedCard FROM tbl014 WHERE CardGuide = @Guide)
        SET @sender = (SELECT ByUser FROM tbl014 WHERE CardGuide = @Guide)
        IF (@sender IS NULL) SET @sender = (SELECT AgentGuide FROM tbl014 WHERE CardGuide = @Guide)
        SET @Note = (SELECT Notes FROM tbl014 WHERE CardGuide = @Guide)
    END

    SET @StageGuide = (SELECT StageGuide FROM Approve_Stage WHERE MainGuide = @MainGuid AND Stage_Num = 1)

    IF (@StageGuide IS NOT NULL)
    BEGIN
        SET @PrcGuide = NEWID()
        INSERT INTO [dbo].[Approve_Process] ([ProcessGuide],[StageGuide],[State],[CardGuide],[Sender],[SendDate],[SenderNote],[CartType])
        VALUES (@PrcGuide, @StageGuide, 1, @Guide, @sender, GETDATE(), @Note, @Type);

        DECLARE UsGuideCursor CURSOR LOCAL FOR
        SELECT UsGuide FROM Approve_User_Stage WHERE StageGuide = @StageGuide
        OPEN UsGuideCursor
        FETCH NEXT FROM UsGuideCursor INTO @UsGuide

        WHILE (@@FETCH_STATUS = 0)
        BEGIN
            IF (@UsGuide = '00000000-0000-0000-0000-000000000000')
            BEGIN
                SET @AgentGuide = (SELECT AgentGuide FROM tbl022 WHERE CardGuide = @Guide)
                SET @_emilusr = (SELECT Email FROM tbl016 WHERE CardGuide = @AgentGuide)
                SET @_lang = ISNULL((SELECT AgentLanguage FROM tbl016 WHERE CardGuide = @AgentGuide), 1)
                IF (@_lang = 1)
                    SET @SendToName = (SELECT AgentName FROM tbl016 WHERE CardGuide = @AgentGuide)
                ELSE
                    SET @SendToName = (SELECT LatinName FROM tbl016 WHERE CardGuide = @AgentGuide)
            END
            ELSE
            BEGIN
                SET @_emilusr = (SELECT UserEmail FROM tbl013 WHERE UsGuide = @UsGuide)
                SET @_lang = ISNULL((SELECT UserLanguage FROM tbl013 WHERE UsGuide = @UsGuide), 1)
                SET @SendToName = (SELECT UserName FROM tbl013 WHERE UsGuide = @UsGuide)
            END

            IF (@_emilusr IS NOT NULL)
            BEGIN
                SET @UserNameSender = (SELECT UserName FROM TBL013 WHERE UsGuide = @sender)
                IF (@UserNameSender IS NULL) SET @UserNameSender = (SELECT AgentName FROM TBL016 WHERE CardGuide = @sender)

                IF (@_lang = 1)
                BEGIN
                    IF (@Type = 1) SET @EntryName = (SELECT EntryName FROM Tbl009 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 2) SET @EntryName = (SELECT InvoiceName FROM Tbl020 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 3) SET @EntryName = (SELECT CardName FROM Tbl033 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 4) SET @EntryName = (SELECT CardName FROM Tbl014 WHERE CardGuide = @MainGuid)
                    IF (@UserNameSender IS NULL) SET @UserNameSender = (SELECT AgentName FROM TBL016 WHERE CardGuide = @sender)
                    SET @subject = N'طلب الموافقة على ' + ISNULL(@EntryName, '') + N', المرسل ' + ISNULL(@UserNameSender, '')
                    SET @body = N'<!DOCTYPE html><html lang=ar dir=rtl><head><meta charset=UTF-8/><title>Xtra | Wafek</title></head><body><table style="background:#f1f1f1;border:solid 8px #32b380;border-radius:20px;margin:10px auto;max-width:500px"><tr><td><img src="https://i.ibb.co/NynTy6Q/xtra-wafek-logo.png" width="300" style="margin:15px 10px" alt="" /><h1 style="font-weight:bold;font-size:25px">طلب موافقة | <span style="color:#32b380">نظام وافق</span></h1><p style="background:#f1f1f1;text-align:right;margin:10px;direction:rtl">إلى السيد: <b>' + @SendToName + '</b><br/>نوع البطاقة: <b>' + ISNULL(@EntryName, '') + '</b> | رقمها: <b>' + @CardNumber + '</b><br/>تاريخ إرسال الطلب: <b>' + @emailSendDate + '</b><br/>المرسل: <b>' + ISNULL(@UserNameSender, '') + '</b><br/>شركة: <b>' + ISNULL(@CompanyName, '') + '</b><br/>هاتف: <b>' + ISNULL(@CompanyPhone, '') + '</b></p><p style="color:#128ab5;text-align:right;margin:10px;background:#f1f1f1">ملاحظات الطلب: <b>' + ISNULL(@Note, '') + '</b></p>' + ISNULL(@DetailsBlock, N'') + N'<p style="font-size:14px;font-weight:bold;margin-top:20px;text-align:center;background:#e8f5e9;padding:14px;border-radius:10px;border:2px solid #22c55e">للرد: أعد الإرسال (Reply) على هذا الميل واكتب في الموضوع أو الجسد:</p><p style="font-size:16px;text-align:center;margin:8px 0;letter-spacing:2px"><b>#1#</b> موافق &nbsp; <b>#2#</b> مرفوض &nbsp; <b>#3#</b> يؤجل</p><p style="font-size:12px;text-align:center;color:#555">مثال: اكتب #1# للموافقة أو #2# للرفض أو #3# للتأجيل.</p></td></tr></table></body></html>'
                END
                ELSE
                BEGIN
                    IF (@Type = 1) SET @EntryName = (SELECT EntryLatinName FROM Tbl009 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 2) SET @EntryName = (SELECT LatinName FROM Tbl020 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 3) SET @EntryName = (SELECT LatinName FROM Tbl033 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 4) SET @EntryName = (SELECT LatinName FROM Tbl014 WHERE CardGuide = @MainGuid)
                    IF (@UserNameSender IS NULL) SET @UserNameSender = (SELECT LatinName FROM TBL016 WHERE CardGuide = @sender)
                    SET @subject = N'Request for Approval of ' + ISNULL(@EntryName, '') + N', Sender ' + ISNULL(@UserNameSender, '')
                    SET @body = N'<!DOCTYPE html><html lang=en dir=ltr><head><meta charset=UTF-8/><title>Xtra | Wafek</title></head><body><table style="background:#f1f1f1;border:solid 8px #32b380;border-radius:20px;margin:10px auto;max-width:500px"><tr><td><img src="https://i.ibb.co/NynTy6Q/xtra-wafek-logo.png" width="300" style="margin:15px 10px" alt="" /><h1 style="font-weight:bold;font-size:25px">Request for Approval | <span style="color:#32b380">Wafek System</span></h1><p style="background:#f1f1f1;text-align:left;margin:10px">To Mr: <b>' + @SendToName + '</b><br/>Card Type: <b>' + ISNULL(@EntryName, '') + '</b> | Card Number: <b>' + @CardNumber + '</b><br/>Request Send Date: <b>' + @emailSendDate + '</b><br/>Sender: <b>' + ISNULL(@UserNameSender, '') + '</b><br/>Company: <b>' + ISNULL(@CompanyName, '') + '</b><br/>Phone: <b>' + ISNULL(@CompanyPhone, '') + '</b></p><p style="color:#128ab5;text-align:left;margin:10px;background:#f1f1f1">Request Notes: <b>' + ISNULL(@Note, '') + '</b></p>' + ISNULL(@DetailsBlock, N'') + N'<p style="font-size:14px;font-weight:bold;margin-top:20px;text-align:center;background:#e8f5e9;padding:14px;border-radius:10px;border:2px solid #22c55e">To reply: Reply to this email and type in subject or body:</p><p style="font-size:16px;text-align:center;margin:8px 0;letter-spacing:2px"><b>#1#</b> Approved &nbsp; <b>#2#</b> Rejected &nbsp; <b>#3#</b> Postponed</p><p style="font-size:12px;text-align:center;color:#555">Example: type #1# to approve, #2# to reject, #3# to postpone.</p></td></tr></table></body></html>'
                END

                EXEC Email 'XtraMail', @_emilusr, @subject, @body
            END

            FETCH NEXT FROM UsGuideCursor INTO @UsGuide
        END
        CLOSE UsGuideCursor
        DEALLOCATE UsGuideCursor
    END
END
GO

-- =============================================
-- WORKFLOW TRIGGERS (Integration with Source Tables)
-- =============================================

-- 11. Trigger for TBL010 (Bonds Transaction)
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_WF_TBL010_AfterInsert')
    DROP TRIGGER [dbo].[trg_WF_TBL010_AfterInsert]
GO

CREATE TRIGGER [dbo].[trg_WF_TBL010_AfterInsert]
ON [dbo].[TBL010]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CardGuide uniqueidentifier
    DECLARE @GuidStr nvarchar(50)
    
    DECLARE c CURSOR FOR SELECT CardGuide FROM inserted
    OPEN c
    FETCH NEXT FROM c INTO @CardGuide
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @GuidStr = CAST(@CardGuide AS nvarchar(50))
        EXEC Approve_CreateFirstProcess 
            @Param1 = 'TBL010', 
            @Param2 = @GuidStr, 
            @Param3 = 'OnAfterInsert'
            
        FETCH NEXT FROM c INTO @CardGuide
    END
    
    CLOSE c
    DEALLOCATE c
END
GO

-- 12. Trigger for TBL022 (Bills/Invoices)
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_WF_TBL022_AfterInsert')
    DROP TRIGGER [dbo].[trg_WF_TBL022_AfterInsert]
GO

CREATE TRIGGER [dbo].[trg_WF_TBL022_AfterInsert]
ON [dbo].[TBL022]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CardGuide uniqueidentifier
    DECLARE @GuidStr nvarchar(50)
    
    DECLARE c CURSOR FOR SELECT CardGuide FROM inserted
    OPEN c
    FETCH NEXT FROM c INTO @CardGuide
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @GuidStr = CAST(@CardGuide AS nvarchar(50))
        EXEC Approve_CreateFirstProcess 
            @Param1 = 'TBL022', 
            @Param2 = @GuidStr, 
            @Param3 = 'OnAfterInsert'
            
        FETCH NEXT FROM c INTO @CardGuide
    END
    
    CLOSE c
    DEALLOCATE c
END
GO

-- 13. Trigger for TBL085 (Inventory/Store)
IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'trg_WF_TBL085_AfterInsert')
    DROP TRIGGER [dbo].[trg_WF_TBL085_AfterInsert]
GO

CREATE TRIGGER [dbo].[trg_WF_TBL085_AfterInsert]
ON [dbo].[TBL085]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CardGuide uniqueidentifier
    DECLARE @GuidStr nvarchar(50)
    
    DECLARE c CURSOR FOR SELECT CardGuide FROM inserted
    OPEN c
    FETCH NEXT FROM c INTO @CardGuide
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @GuidStr = CAST(@CardGuide AS nvarchar(50))
        EXEC Approve_CreateFirstProcess 
            @Param1 = 'TBL085', 
            @Param2 = @GuidStr, 
            @Param3 = 'OnAfterInsert'
            
        FETCH NEXT FROM c INTO @CardGuide
    END
    
    CLOSE c
    DEALLOCATE c
END
GO
