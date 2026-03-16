-- =============================================================================
-- رفع الصلاحية درجة واحدة عند الموافقة — للتحقق من صحة التشغيل
-- يُحدّث ActionConfigJson في خطوة SendEmail لورك فلو 222 (سند قبض)
-- =============================================================================

UPDATE WF_Steps
SET ActionConfigJson = N'{
  "responseActions": {
    "onApprove": [
      { "actionType": "IncrementSecurity", "selectedValue": "1" }
    ],
    "onReject": [],
    "onPostpone": []
  }
}'
WHERE WorkflowDefinitionId = 2
  AND ActionType = 'SendEmail';

-- التحقق
SELECT Id, StepOrder, StepName, ActionType, ActionConfigJson
FROM WF_Steps
WHERE WorkflowDefinitionId = 2;

PRINT N'تم تحديث ActionConfigJson — عند الموافقة سترتفع الصلاحية (Security) درجة واحدة.';
