-- =============================================================================
-- Approve_OnApproved / Approve_OnRejected
-- الربط: الرد على الميل (InReplyTo → logId → WF_Logs.SourceRecordId = CardGuide)
--        #1# = موافق → Approve_OnApproved
--        #2# = مرفوض → Approve_OnRejected
-- الجداول: TBL010 (سند), TBL022 (فاتورة), TBL085 (أرشيف)
-- التنفيذ: UPDATE [الجدول] SET Security = المستوى WHERE CardGuide = @SourceId
-- =============================================================================

-- Approve_OnApproved: عند الموافقة (#1#) — رفع Security و Posted للمستند
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_OnApproved]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_OnApproved]
GO

CREATE PROCEDURE [dbo].[Approve_OnApproved]
    @LogId bigint,
    @SourceTable nvarchar(50),
    @SourceId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    -- السند: TBL010
    IF @SourceTable = 'TBL010'
    BEGIN
        IF COL_LENGTH('dbo.TBL010', 'Security') IS NOT NULL
            UPDATE TBL010 SET Security = 2 WHERE CardGuide = @SourceId;
        ELSE IF COL_LENGTH('dbo.TBL010', 'SecurityLevel') IS NOT NULL
            UPDATE TBL010 SET SecurityLevel = 2 WHERE CardGuide = @SourceId;
            
        -- الترحيل عند الموافقة (تغيير حالة Posted إلى 1)
        IF COL_LENGTH('dbo.TBL010', 'Posted') IS NOT NULL
            UPDATE TBL010 SET Posted = 1 WHERE CardGuide = @SourceId;
    END

    -- الفاتورة: TBL022
    ELSE IF @SourceTable = 'TBL022'
    BEGIN
        IF COL_LENGTH('dbo.TBL022', 'Security') IS NOT NULL
            UPDATE TBL022 SET Security = 2 WHERE CardGuide = @SourceId;
        ELSE IF COL_LENGTH('dbo.TBL022', 'SecurityLevel') IS NOT NULL
            UPDATE TBL022 SET SecurityLevel = 2 WHERE CardGuide = @SourceId;
    END

    -- الأرشيف: TBL085
    ELSE IF @SourceTable = 'TBL085'
    BEGIN
        IF COL_LENGTH('dbo.TBL085', 'Security') IS NOT NULL
            UPDATE TBL085 SET Security = 2 WHERE CardGuide = @SourceId;
        ELSE IF COL_LENGTH('dbo.TBL085', 'SecurityLevel') IS NOT NULL
            UPDATE TBL085 SET SecurityLevel = 2 WHERE CardGuide = @SourceId;
    END

    -- أضف TBL014 (إجازة) أو جداول أخرى حسب الحاجة
END
GO

-- Approve_OnRejected: عند الرفض (#2#)
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_OnRejected]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_OnRejected]
GO

CREATE PROCEDURE [dbo].[Approve_OnRejected]
    @LogId bigint,
    @SourceTable nvarchar(50),
    @SourceId uniqueidentifier
AS
BEGIN
    SET NOCOUNT ON;

    IF @SourceTable = 'TBL010'
    BEGIN
        IF COL_LENGTH('dbo.TBL010', 'RejectReason') IS NOT NULL
            UPDATE TBL010 SET RejectReason = N'مرفوض من الموافق' WHERE CardGuide = @SourceId;
        -- أو تحديث حالة أخرى حسب المنظومة
    END
    ELSE IF @SourceTable = 'TBL022'
    BEGIN
        IF COL_LENGTH('dbo.TBL022', 'RejectReason') IS NOT NULL
            UPDATE TBL022 SET RejectReason = N'مرفوض من الموافق' WHERE CardGuide = @SourceId;
    END
    ELSE IF @SourceTable = 'TBL085'
    BEGIN
        IF COL_LENGTH('dbo.TBL085', 'RejectReason') IS NOT NULL
            UPDATE TBL085 SET RejectReason = N'مرفوض من الموافق' WHERE CardGuide = @SourceId;
    END
END
GO

PRINT N'تم إنشاء Approve_OnApproved و Approve_OnRejected.';
PRINT N'الربط: الميل الأصلي MessageId=wafek-{logId}@wafek → الرد InReplyTo → logId → WF_Logs.SourceRecordId = CardGuide';
