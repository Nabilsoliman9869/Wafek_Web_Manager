-- =============================================================================
-- إزالة الرابط وإعادة إنشاء الإجراء Approve_CreateFirstProcess
-- 1) إضافة الأعمدة الناقصة إن لم تكن موجودة
-- 2) إعادة إنشاء الإجراء بدون الرابط
-- =============================================================================

-- إضافة الأعمدة إن لم تكن موجودة
IF OBJECT_ID('dbo.tbl013', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tbl013') AND name = 'UserEmail')
   ALTER TABLE dbo.tbl013 ADD UserEmail nvarchar(50) NULL;

IF OBJECT_ID('dbo.tbl013', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tbl013') AND name = 'UserLanguage')
   ALTER TABLE dbo.tbl013 ADD UserLanguage smallint NULL DEFAULT 1;

IF OBJECT_ID('dbo.tbl016', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.tbl016') AND name = 'AgentLanguage')
   ALTER TABLE dbo.tbl016 ADD AgentLanguage smallint NULL DEFAULT -1;

GO

IF OBJECT_ID('dbo.Approve_CreateFirstProcess', 'P') IS NOT NULL
    DROP PROCEDURE [dbo].[Approve_CreateFirstProcess];
GO

CREATE PROCEDURE [dbo].[Approve_CreateFirstProcess](@Guide uniqueidentifier, @Type int)
AS
BEGIN
    DECLARE @StageGuide uniqueidentifier
    DECLARE @sender uniqueidentifier
    DECLARE @UserNameSender nvarchar(max)
    DECLARE @Note nvarchar(max)
    DECLARE @CardNumber nvarchar(max)
    DECLARE @MainGuid uniqueidentifier
    DECLARE @emailSendDate nvarchar(max)
    DECLARE @MainRoot nvarchar(Max)
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

    SET @emailSendDate = CONVERT(nvarchar(30), GETDATE(), 120)
    SET @MainRoot = (SELECT MainRoor FROM Approve_Email WHERE ID = 1)
    SET @CompanyName = (SELECT CompanyName FROM Approve_Email WHERE ID = 1)
    SET @CompanyPhone = (SELECT CompanyPhone FROM Approve_Email WHERE ID = 1)

    IF (@Type = 1)
    BEGIN
        SET @CardNumber = (SELECT bondnumber FROM tbl010 WHERE CardGuide = @Guide)
        SET @MainGuid = (SELECT Mainguide FROM tbl010 WHERE CardGuide = @Guide)
        SET @sender = (SELECT ByUser FROM tbl010 WHERE CardGuide = @Guide)
        IF (@sender IS NULL) SET @sender = (SELECT AgentGuide FROM tbl010 WHERE CardGuide = @Guide)
        SET @Note = (SELECT Notes FROM tbl010 WHERE CardGuide = @Guide)
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
                    SET @body = N'<!DOCTYPE html><html lang=ar dir=rtl><head><meta charset=UTF-8/><title>Xtra | Wafek</title></head><body><table style="background:#f1f1f1;border:solid 8px #32b380;border-radius:20px;margin:10px auto;max-width:500px"><tr><td><img src=https://i.ibb.co/NynTy6Q/xtra-wafek-logo.png width=300 style="margin:15px 10px"/><h1 style="font-weight:bold;font-size:25px">طلب موافقة | <span style="color:#32b380">نظام وافق</span></h1><p style="background:#f1f1f1;text-align:right;margin:10px;direction:rtl">إلى السيد: <b>' + @SendToName + '</b><br/>نوع البطاقة: <b>' + ISNULL(@EntryName, '') + '</b> | رقمها: <b>' + @CardNumber + '</b><br/>تاريخ إرسال الطلب: <b>' + @emailSendDate + '</b><br/>المرسل: <b>' + ISNULL(@UserNameSender, '') + '</b><br/>شركة: <b>' + ISNULL(@CompanyName, '') + '</b><br/>هاتف: <b>' + ISNULL(@CompanyPhone, '') + '</b></p><p style="color:#128ab5;text-align:right;margin:10px;background:#f1f1f1">ملاحظات الطلب: <b>' + ISNULL(@Note, '') + '</b></p><p style="font-size:14px;font-weight:bold;margin-top:20px;text-align:center;background:#e8f5e9;padding:14px;border-radius:10px;border:2px solid #22c55e">للرد: أعد الإرسال (Reply) على هذا الميل واكتب في الموضوع أو الجسد:</p><p style="font-size:16px;text-align:center;margin:8px 0;letter-spacing:2px"><b>#1#</b> موافق &nbsp; <b>#2#</b> مرفوض &nbsp; <b>#3#</b> يؤجل</p><p style="font-size:12px;text-align:center;color:#555">مثال: اكتب #1# للموافقة أو #2# للرفض أو #3# للتأجيل.</p></td></tr></table></body></html>'
                END
                ELSE
                BEGIN
                    IF (@Type = 1) SET @EntryName = (SELECT EntryLatinName FROM Tbl009 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 2) SET @EntryName = (SELECT LatinName FROM Tbl020 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 3) SET @EntryName = (SELECT LatinName FROM Tbl033 WHERE CardGuide = @MainGuid)
                    ELSE IF (@Type = 4) SET @EntryName = (SELECT LatinName FROM Tbl014 WHERE CardGuide = @MainGuid)
                    IF (@UserNameSender IS NULL) SET @UserNameSender = (SELECT LatinName FROM TBL016 WHERE CardGuide = @sender)
                    SET @subject = N'Request for Approval of ' + ISNULL(@EntryName, '') + N', Sender ' + ISNULL(@UserNameSender, '')
                    SET @body = N'<!DOCTYPE html><html lang=en dir=ltr><head><meta charset=UTF-8/><title>Xtra | Wafek</title></head><body><table style="background:#f1f1f1;border:solid 8px #32b380;border-radius:20px;margin:10px auto;max-width:500px"><tr><td><img src=https://i.ibb.co/NynTy6Q/xtra-wafek-logo.png width=300 style="margin:15px 10px"/><h1 style="font-weight:bold;font-size:25px">Request for Approval | <span style="color:#32b380">Wafek System</span></h1><p style="background:#f1f1f1;text-align:left;margin:10px">To Mr: <b>' + @SendToName + '</b><br/>Card Type: <b>' + ISNULL(@EntryName, '') + '</b> | Card Number: <b>' + @CardNumber + '</b><br/>Request Send Date: <b>' + @emailSendDate + '</b><br/>Sender: <b>' + ISNULL(@UserNameSender, '') + '</b><br/>Company: <b>' + ISNULL(@CompanyName, '') + '</b><br/>Phone: <b>' + ISNULL(@CompanyPhone, '') + '</b></p><p style="color:#128ab5;text-align:left;margin:10px;background:#f1f1f1">Request Notes: <b>' + ISNULL(@Note, '') + '</b></p><p style="font-size:14px;font-weight:bold;margin-top:20px;text-align:center;background:#e8f5e9;padding:14px;border-radius:10px;border:2px solid #22c55e">To reply: Reply to this email and type in subject or body:</p><p style="font-size:16px;text-align:center;margin:8px 0;letter-spacing:2px"><b>#1#</b> Approved &nbsp; <b>#2#</b> Rejected &nbsp; <b>#3#</b> Postponed</p><p style="font-size:12px;text-align:center;color:#555">Example: type #1# to approve, #2# to reject, #3# to postpone.</p></td></tr></table></body></html>'
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

PRINT N'تم إعادة إنشاء الإجراء Approve_CreateFirstProcess بدون الرابط.';
