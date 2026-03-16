-- إضافة InReplyTo و ApprovalResponse لربط رد الميل بطلب الموافقة
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_EmailLogs', 'InReplyTo') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [InReplyTo] [nvarchar](500) NULL;
END
GO
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_EmailLogs', 'ApprovalResponse') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [ApprovalResponse] [nvarchar](50) NULL; -- Approved, Rejected, Postponed
END
GO
