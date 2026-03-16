-- إضافة عمود ImapMessageId لتجنب تسجيل نفس الميل الوارد أكثر من مرة
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND name = 'ImapMessageId')
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [ImapMessageId] [nvarchar](500) NULL;
END
GO

-- فهرس لتسريع البحث عن التكرار
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WF_EmailLogs_ImapMessageId' AND object_id = OBJECT_ID('WF_EmailLogs'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_WF_EmailLogs_ImapMessageId] ON [dbo].[WF_EmailLogs] ([ImapMessageId]) WHERE [ImapMessageId] IS NOT NULL;
END
GO
