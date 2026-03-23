-- =============================================================================
-- نشر مسار الموافقة عبر الميل (#1# #2# #3#)
-- الترتيب: 1) أعمدة WF_EmailLogs  2) Approve_ProcessResponse  3) Approve_OnApproved/OnRejected
-- =============================================================================

PRINT N'=== 1. إضافة InReplyTo و ApprovalResponse لجدول WF_EmailLogs ===';
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
BEGIN
    IF COL_LENGTH('dbo.WF_EmailLogs', 'InReplyTo') IS NULL
    BEGIN
        ALTER TABLE [dbo].[WF_EmailLogs] ADD [InReplyTo] [nvarchar](500) NULL;
        PRINT N'تمت إضافة عمود InReplyTo';
    END
    IF COL_LENGTH('dbo.WF_EmailLogs', 'ApprovalResponse') IS NULL
    BEGIN
        ALTER TABLE [dbo].[WF_EmailLogs] ADD [ApprovalResponse] [nvarchar](50) NULL;
        PRINT N'تمت إضافة عمود ApprovalResponse';
    END
END
GO

PRINT N'=== 2. Approve_ProcessResponse (إن لم يكن موجوداً) ===';
IF OBJECT_ID('dbo.Approve_ProcessResponse', 'P') IS NULL
BEGIN
    PRINT N'يرجى تنفيذ Wafek_Approve_ProcessResponse.sql أولاً';
END
ELSE
    PRINT N'Approve_ProcessResponse موجود';
GO

PRINT N'=== 3. Approve_OnApproved و Approve_OnRejected ===';
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_OnApproved]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_OnApproved]
GO
CREATE PROCEDURE [dbo].[Approve_OnApproved]
    @LogId bigint, @SourceTable nvarchar(50), @SourceId uniqueidentifier
AS
BEGIN SET NOCOUNT ON;
    IF @SourceTable = 'TBL010' AND COL_LENGTH('dbo.TBL010', 'Security') IS NOT NULL UPDATE TBL010 SET Security = 2 WHERE CardGuide = @SourceId;
    ELSE IF @SourceTable = 'TBL010' AND COL_LENGTH('dbo.TBL010', 'SecurityLevel') IS NOT NULL UPDATE TBL010 SET SecurityLevel = 2 WHERE CardGuide = @SourceId;
    IF @SourceTable = 'TBL010' AND COL_LENGTH('dbo.TBL010', 'Posted') IS NOT NULL UPDATE TBL010 SET Posted = 1 WHERE CardGuide = @SourceId;
    IF @SourceTable = 'TBL010' AND OBJECT_ID('dbo.Prc027', 'P') IS NOT NULL EXEC dbo.Prc027 @SourceId;
    ELSE IF @SourceTable = 'TBL022' AND COL_LENGTH('dbo.TBL022', 'Security') IS NOT NULL UPDATE TBL022 SET Security = 2 WHERE CardGuide = @SourceId;
    ELSE IF @SourceTable = 'TBL022' AND COL_LENGTH('dbo.TBL022', 'SecurityLevel') IS NOT NULL UPDATE TBL022 SET SecurityLevel = 2 WHERE CardGuide = @SourceId;
    ELSE IF @SourceTable = 'TBL085' AND COL_LENGTH('dbo.TBL085', 'Security') IS NOT NULL UPDATE TBL085 SET Security = 2 WHERE CardGuide = @SourceId;
    ELSE IF @SourceTable = 'TBL085' AND COL_LENGTH('dbo.TBL085', 'SecurityLevel') IS NOT NULL UPDATE TBL085 SET SecurityLevel = 2 WHERE CardGuide = @SourceId;
END
GO
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Approve_OnRejected]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[Approve_OnRejected]
GO
CREATE PROCEDURE [dbo].[Approve_OnRejected]
    @LogId bigint, @SourceTable nvarchar(50), @SourceId uniqueidentifier
AS
BEGIN SET NOCOUNT ON;
    IF @SourceTable = 'TBL010' AND COL_LENGTH('dbo.TBL010', 'RejectReason') IS NOT NULL UPDATE TBL010 SET RejectReason = N'مرفوض من الموافق' WHERE CardGuide = @SourceId;
    ELSE IF @SourceTable = 'TBL022' AND COL_LENGTH('dbo.TBL022', 'RejectReason') IS NOT NULL UPDATE TBL022 SET RejectReason = N'مرفوض من الموافق' WHERE CardGuide = @SourceId;
    ELSE IF @SourceTable = 'TBL085' AND COL_LENGTH('dbo.TBL085', 'RejectReason') IS NOT NULL UPDATE TBL085 SET RejectReason = N'مرفوض من الموافق' WHERE CardGuide = @SourceId;
END
GO
PRINT N'تم إنشاء Approve_OnApproved و Approve_OnRejected.';
PRINT N'=== جاهز ===';
PRINT N'المسار: الميل الأصلي (MessageId=wafek-{logId}@wafek) ← المدير يرد بـ #1# أو #2# أو #3#';
PRINT N'        → IMAP → WF_EmailLogs → InboundEmailCommandWorker → Approve_ProcessResponse → Approve_OnApproved';
PRINT N'        → UPDATE TBL010/TBL022/TBL085 SET Security=2 WHERE CardGuide=@SourceId';
