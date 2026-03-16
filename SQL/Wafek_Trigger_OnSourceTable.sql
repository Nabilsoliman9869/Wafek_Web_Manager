-- =============================================
-- تريجر على جدول المصدر — لاستدعاء الورك فلو تلقائياً
-- =============================================
-- نفّذ هذا السكربت على قاعدة البيانات.
-- غيّر اسم الجدول (TBL010) واسم عمود الـ GUID (CardGuide) حسب جدولك.
--
-- الورك فلو 222: تحقق من SourceTable و TriggerEvent من صفحة مراجعة 222
-- =============================================

-- مثال: تريجر على TBL010 (سندات) عند الإدراج
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
    
    DECLARE c CURSOR FOR SELECT CardGuide FROM inserted
    OPEN c
    FETCH NEXT FROM c INTO @CardGuide
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC Approve_CreateFirstProcess 
            @Param1 = 'TBL010', 
            @Param2 = CAST(@CardGuide AS nvarchar(50)), 
            @Param3 = 'OnAfterInsert'
        FETCH NEXT FROM c INTO @CardGuide
    END
    
    CLOSE c
    DEALLOCATE c
END
GO

-- ملاحظة: إذا كان الورك فلو 222 على جدول آخر (مثلاً TBL021، TBL085)،
-- انسخ القالب وغيّر TBL010 إلى اسم الجدول والعمود المناسب.
