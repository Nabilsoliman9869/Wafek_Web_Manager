-- =============================================================================
-- إنشاء إجراء Email — مطلوب لإرسال الميل من Approve_CreateFirstProcess
-- يستخدم msdb.dbo.sp_send_dbmail (Database Mail)
-- 
-- ملاحظة: يجب تفعيل Database Mail وتكوين Profile باسم XtraMail في SQL Server
-- =============================================================================

IF OBJECT_ID('dbo.Email', 'P') IS NOT NULL
    DROP PROCEDURE [dbo].[Email];
GO

CREATE PROCEDURE [dbo].[Email](
    @profile nvarchar(max),
    @recerver nvarchar(max),
    @subject nvarchar(max),
    @boday nvarchar(max)
)
AS
BEGIN
    EXEC msdb.dbo.sp_send_dbmail 
        @profile_name = @profile,
        @recipients = @recerver,
        @subject = @subject,
        @body = @boday,
        @body_format = 'HTML';
END
GO

PRINT N'تم إنشاء إجراء Email بنجاح. تأكد من تفعيل Database Mail وتكوين Profile باسم XtraMail.';
