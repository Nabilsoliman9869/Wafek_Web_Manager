-- =============================================
-- Approve_ProcessResponse
-- معالجة رد الموافقة من الرابط أو الإيميل
-- القيم: Approved, Rejected, Postponed
-- كل قيمة تُحدّث WF_Logs وقد تُطلق إجراءات في النظام
-- =============================================

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

	-- جلب بيانات السجل
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

	-- عدم المعالجة إذا كان السجل في حالة نهائية
	IF EXISTS (SELECT 1 FROM WF_Logs WHERE Id = @LogId AND Status IN ('Approved', 'Rejected', 'Completed'))
		RETURN

	-- 1. تحديث حالة WF_Logs حسب نوع الرد
	IF @ResponseType = 'Approved'
	BEGIN
		UPDATE WF_Logs 
		SET Status = 'Approved', 
		    LastActionLog = N'تمت الموافقة من الرابط/الإيميل',
		    LastUpdatedDate = GETDATE()
		WHERE Id = @LogId

		-- الموافقة: التحقق إن وُجد خطوة تالية وتشغيلها
		SELECT @NextStepOrder = MIN(StepOrder) 
		FROM WF_Steps 
		WHERE WorkflowDefinitionId = @WF_Id AND StepOrder > @CurrentStep

		IF @NextStepOrder IS NOT NULL
		BEGIN
			-- إنشاء سجل جديد للخطوة التالية أو تحديث الحالي حسب تصميمك
			-- هنا: نقوم بتحديث CurrentStepOrder وتشغيل الخطوة التالية
			UPDATE WF_Logs SET CurrentStepOrder = @NextStepOrder, Status = 'Pending', LastActionLog = N'الانتقال للخطوة التالية'
			WHERE Id = @LogId
			
			EXEC Approve_ExecuteStep @WF_Id, @SourceId, @NextStepOrder
		END
		ELSE
		BEGIN
			-- لا توجد خطوة تالية — الورك فلو اكتمل
			UPDATE WF_Logs SET LastActionLog = N'تمت الموافقة — اكتمال الورك فلو', Status = 'Approved'
			WHERE Id = @LogId
		END

		-- استدعاء إجراء الأعمال إن وُجد (لتحديث المستند المصدر مثلاً)
		IF OBJECT_ID('dbo.Approve_OnApproved', 'P') IS NOT NULL
			EXEC Approve_OnApproved @LogId, @SourceTable, @SourceId
	END
	ELSE IF @ResponseType = 'Rejected'
	BEGIN
		UPDATE WF_Logs 
		SET Status = 'Rejected', 
		    LastActionLog = N'تم الرفض من الرابط/الإيميل',
		    LastUpdatedDate = GETDATE()
		WHERE Id = @LogId

		-- استدعاء إجراء الرفض إن وُجد
		IF OBJECT_ID('dbo.Approve_OnRejected', 'P') IS NOT NULL
			EXEC Approve_OnRejected @LogId, @SourceTable, @SourceId
	END
	ELSE IF @ResponseType = 'Postponed'
	BEGIN
		UPDATE WF_Logs 
		SET Status = 'Postponed', 
		    LastActionLog = N'تم التأجيل — سيُعاد الإرسال لاحقاً',
		    LastUpdatedDate = GETDATE()
		WHERE Id = @LogId

		-- استدعاء إجراء التأجيل إن وُجد (مثلاً لجدولة إعادة الإرسال)
		IF OBJECT_ID('dbo.Approve_OnPostponed', 'P') IS NOT NULL
			EXEC Approve_OnPostponed @LogId, @SourceTable, @SourceId
	END
	ELSE
	BEGIN
		RAISERROR('Invalid ResponseType. Use: Approved, Rejected, or Postponed', 16, 1)
		RETURN
	END
END
GO

-- =============================================
-- إجراءات اختيارية — يمكن إنشاؤها في قاعدة البيانات
-- لتطبيق منطق الأعمال عند كل نوع رد
-- =============================================
/*
-- مثال Approve_OnApproved (تحديث حالة المستند في TBL010 مثلاً):
CREATE PROCEDURE [dbo].[Approve_OnApproved]
	@LogId bigint,
	@SourceTable nvarchar(50),
	@SourceId uniqueidentifier
AS
BEGIN
	IF @SourceTable = 'TBL010'
	BEGIN
	    -- تعديل الحماية إلى معتمد
		IF COL_LENGTH('dbo.TBL010', 'Security') IS NOT NULL
			UPDATE TBL010 SET Security = 2 WHERE CardGuide = @SourceId;
			
		-- ترحيل السند
		IF COL_LENGTH('dbo.TBL010', 'Posted') IS NOT NULL
			UPDATE TBL010 SET Posted = 1 WHERE CardGuide = @SourceId;
	END
END
GO

-- مثال Approve_OnRejected:
CREATE PROCEDURE [dbo].[Approve_OnRejected]
	@LogId bigint,
	@SourceTable nvarchar(50),
	@SourceId uniqueidentifier
AS
BEGIN
	IF @SourceTable = 'TBL010'
		UPDATE TBL010 SET [حقل_الحالة] = N'مرفوض' WHERE CardGuide = @SourceId
END
GO

-- مثال Approve_OnPostponed (جدولة إعادة الإرسال بعد ساعات):
CREATE PROCEDURE [dbo].[Approve_OnPostponed]
	@LogId bigint,
	@SourceTable nvarchar(50),
	@SourceId uniqueidentifier
AS
BEGIN
	-- مثلاً: تحديث PostponedUntil = DATEADD(hour, 24, GETDATE())
	-- والـ WorkflowEngineWorker يتحقق منه ويعيد الإرسال
END
GO
*/
