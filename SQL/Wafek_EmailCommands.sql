
-- =============================================
-- Wafek Email Command Center Schema
-- =============================================

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailCommands]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[WF_EmailCommands](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[CommandCode] [nvarchar](50) NOT NULL, -- e.g. *01#, *05#
	[Description] [nvarchar](200) NOT NULL, -- e.g. Get Daily Report
	[IsActive] [bit] NOT NULL DEFAULT ((1)),
	
	-- Action Type: Report, SQLQuery, StoredProcedure, Backup
	[ActionType] [nvarchar](50) NOT NULL, 
	
	-- The content to execute (SQL, Proc Name, Report Name)
	[ExecutionContent] [nvarchar](max) NOT NULL, 
	
	-- Response Configuration
	[ResponseSubject] [nvarchar](200) NULL, -- Subject of the reply email
	[ResponseBodyTemplate] [nvarchar](max) NULL, -- HTML Body of the reply
	
	[CreatedDate] [datetime] NOT NULL DEFAULT (getdate()),
 CONSTRAINT [PK_WF_EmailCommands] PRIMARY KEY CLUSTERED ([Id] ASC)
)
END
GO

-- Unique Index on CommandCode to prevent duplicates
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WF_EmailCommands_Code' AND object_id = OBJECT_ID('WF_EmailCommands'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_WF_EmailCommands_Code] ON [dbo].[WF_EmailCommands] ([CommandCode] ASC);
END
GO

-- Log Table for Incoming Email Commands
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

-- إضافة ImapMessageId للجداول القديمة
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_EmailLogs]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_EmailLogs', 'ImapMessageId') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_EmailLogs] ADD [ImapMessageId] [nvarchar](500) NULL;
END
GO
