# تدفق الورك فلو — نظام وافق
## الكود الأصلي وما قبْل الإرسال وما بعده

---

## 1. ما قبل إرسال الميل — قواعد الفلترة والتشغيل

قبل إرسال ميل الموافقة، يمر المستند بعدة فلاتر:

### أ) على مستوى التعريف (WF_Definitions)

| الحقل | الغرض |
|-------|--------|
| **SourceTable** | الجدول المصدر (TBL010 سند، TBL022 فاتورة، TBL085 أرشيف، إلخ) |
| **TriggerEvent** | حدث التشغيل (OnAfterInsert، OnAfterUpdate، إلخ) |
| **ConditionSql** | شرط عام على المستند — إذا لم يتحقق لا يبدأ الورك فلو أصلاً |
| **SpecificDocTypeGuid** | نوع مستند معيّن (اختياري) |

**مثال:** `ConditionSql = "NetTotal > 5000 AND BranchID = 1"` — الورك فلو يبدأ فقط للمستندات التي تتجاوز 5000 وتنتمي للفرع 1.

### ب) على مستوى الخطوة (WF_Steps — StepCondition)

لكل خطوة **SendEmail** يُقيّم **StepCondition** ضد المستند:

- **قيمة المستند** — مثل `NetTotal > 10000` أو `BondAmount >= 5000`
- **اسم العميل** — مثل `EXISTS (SELECT 1 FROM TBL004 WHERE CardGuide = TBL010.AgentGuide AND AccountName LIKE N'%شركة%')`
- **اسم المستخدم** — عبر الـ JOIN مع TBL013/TBL016
- **أي شرط SQL** — يُنفَّذ في سياق المستند: `SELECT 1 FROM [SourceTable] WHERE CardGuide = @id AND (StepCondition)`

**إذا لم يتحقق الشرط:**
- إن وُجدت خطوة تالية → ينتقل إليها (تخطي إرسال الميل)
- إن لم توجد → `Status = 'Skipped'`

**إذا تحقق الشرط** → يُرسل الميل للمرسل إليه.

---

## 2. إرسال الميل

- **المرسل إليه:** من `SelectedValue` (إيميل مباشر أو `User:Guid`)
- **المحتوى:** من `EmailBodyBuilder` (عربي/إنجليزي حسب لغة المستخدم)
- **الرابط:** ApproveRequest مع LogId
- **الإرشادات في الميل:** `*#Approve` موافق | `*#Reject` غير موفق | `*#Postpone` يؤجل

بعد الإرسال: `Status = 'WaitingForResponse'`

---

## 3. قراءة المرسل إليه والرد

المستلم يفتح الميل ويزور الرابط أو يرد بالإيميل:
- **موافق** → `Approved`
- **غير موفق** → `Rejected`
- **يؤجل** → `Postponed`

---

## 4. نافذة إعدادات التوابع — عند الموافقة

في **ActionConfigJson** → `responseActions.onApprove`:

| نوع الإجراء | الوصف |
|-------------|--------|
| **ChangeSecurity** | يرفع مستوى السرية في المستند (SecurityLevel) |
| **ChangeStage** | يغير المرحلة (StageGuide) |
| **ExecuteProc** | يشغّل إجراء مخزن (مثلاً: يحرر مستند جديد، يحدّث جداول) |
| **ChangeStage** | ينقل المستند لمرحلة أخرى |
| **UpdateTable** | يُحدّث حقل في جدول |
| **InsertTable** | يُدخل سطراً في جدول |

**مثال JSON:**
```json
{
  "responseActions": {
    "onApprove": [
      { "actionType": "ChangeSecurity", "selectedValue": "guid-مستوى-السرية" },
      { "actionType": "ExecuteProc", "selectedValue": "SP_PostBond" },
      { "actionType": "ChangeStage", "selectedValue": "guid-المرحلة" },
      { "actionType": "UpdateTable", "selectedValue": "TBL010", "params": { "column": "Posted", "value": "1" } }
    ]
  }
}
```

---

## 5. نافذة إعدادات التوابع — عند الرفض

في **ActionConfigJson** → `responseActions.onReject`:

نفس أنواع الإجراءات — مثلاً:
- تشغيل `SP_RejectDocument` لتحديث حالة المستند إلى مرفوض
- إرسال ميل تنبيه للمرسل
- تحديث حقل في جدول
- إدخال سطر في جدول السجلات

**مثال:**
```json
{
  "responseActions": {
    "onReject": [
      { "actionType": "ExecuteProc", "selectedValue": "SP_RejectDocument" },
      { "actionType": "UpdateTable", "selectedValue": "TBL010", "params": { "column": "RejectReason", "value": "مرفوض من الموافق" } }
    ]
  }
}
```

---

## 6. نافذة إعدادات التوابع — عند التأجيل

في **responseActions.onPostpone** — مثلاً:
- تحديث `PostponedUntil`
- إشعار المرسل بأن الطلب مُؤجّل

---

## 7. الخلاصة

```
مستند جديد/تعديل
       ↓
Approve_CreateFirstProcess (يُستدعى من Trigger أو يدوياً)
       ↓
ConditionSql (على التعريف) → لا يتحقق؟ انتهى
       ↓
WF_Logs (Pending)
       ↓
WorkflowEngineWorker كل 10 ثوان
       ↓
StepCondition (على الخطوة) → لا يتحقق؟ تخطي أو Skipped
       ↓
إرسال الميل → WaitingForResponse
       ↓
المستلم يرد (موافق / رفض / يؤجل)
       ↓
ResponseActionExecutor → تنفيذ onApprove / onReject / onPostpone
       ↓
تحديث المستند، تشغيل Procs، تغيير الحالة، إلخ
```

---

## 8. الملفات الرئيسية

| الملف | الدور |
|-------|-------|
| `WorkflowEngineWorker.cs` | تقييم StepCondition، إرسال الميل |
| `ResponseActionExecutor.cs` | تنفيذ responseActions من JSON |
| `ResponseActionHandlers.cs` | ChangeSecurity، ExecuteProc، ChangeStage، UpdateTable |
| `ApproveRequest.cshtml` | صفحة الموافقة/الرفض/التأجيل |
| `WorkflowDesigner` | تكوين الخطوات وActionConfigJson |
| `SQL/Wafek_Workflow_Schema.sql` | جداول WF_Definitions، WF_Steps، WF_Logs |
