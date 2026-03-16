-- =============================================
-- اختبار الإرسال يدوياً — لعزل المشكلة
-- =============================================
-- الهدف: نفصل بين (1) البروسيدرات SQL و (2) إرسال الميل من الـ Worker
--
-- ملاحظة مهمة: الإرسال الفعلي (SMTP) يتم من C# WorkflowEngineWorker
-- وليس من SQL. البروسيدرات هنا فقط تُدخل سجل في WF_Logs
-- والـ Worker (كل 10 ثوان) يقرأ السجلات المعلقة ويرسل الميل.
-- =============================================

-- -----------------------------------------
-- الخطوة 1: تشغيل بروسيدر البداية
-- -----------------------------------------
PRINT '--- 1. تشغيل Approve_CreateFirstProcess ---'
EXEC Approve_CreateFirstProcess 
    @Param1 = 'TBL010', 
    @Param2 = '7D1F5AFF-316A-40A4-BDA0-0F79823AE4D8', 
    @Param3 = 'OnAfterInsert'

-- -----------------------------------------
-- الخطوة 2: فحص مخرجات البروسيدر
-- -----------------------------------------
PRINT '--- 2. فحص WF_Logs (المفروض فيهم سجل جديد) ---'
SELECT Id, WorkflowDefinitionId, SourceRecordId, CurrentStepOrder, Status, LastActionLog, CreatedDate
FROM WF_Logs 
WHERE SourceRecordId = '7D1F5AFF-316A-40A4-BDA0-0F79823AE4D8'
ORDER BY Id DESC

-- -----------------------------------------
-- الخطوة 3: فحص الخطوة الأولى للورك فلو (هل SendEmail؟)
-- -----------------------------------------
PRINT '--- 3. الخطوة الأولى في الورك فلو (يجب أن تكون SendEmail) ---'
SELECT S.StepOrder, S.StepName, S.ActionType, S.SelectedValue
FROM WF_Steps S
JOIN WF_Logs L ON L.WorkflowDefinitionId = S.WorkflowDefinitionId AND L.CurrentStepOrder = S.StepOrder
WHERE L.SourceRecordId = '7D1F5AFF-316A-40A4-BDA0-0F79823AE4D8'

-- -----------------------------------------
-- ماذا تتوقع بعد 10–20 ثانية؟
-- -----------------------------------------
-- إذا كان تطبيق Wafek_Web_Manager شغّال:
--   • Status يتحول من Pending → WaitingForResponse (لو الميل اتبعت)
--   • أو → Error / Failed (لو حصل خطأ)
-- أعد تشغيل هذا الاستعلام للتحقق:
-- SELECT Id, Status, LastActionLog FROM WF_Logs WHERE SourceRecordId = '7D1F5AFF-316A-40A4-BDA0-0F79823AE4D8'
--
-- إذا الميل مبوصّلش: المشكلة غالباً في WorkflowEngineWorker (SMTP أو الإعدادات)
-- =============================================
