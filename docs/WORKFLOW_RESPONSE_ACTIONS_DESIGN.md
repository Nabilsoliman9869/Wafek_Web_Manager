# تصميم إجراءات الرد — وافق Web Manager
## نسخة طموحة قابلة للتطوير إلى حد كبير

---

## 1. الرؤية

بدلاً من اقتصار "ماذا يفعل موافق" على إجراء ثابت، نُصمّم نظاماً:

- **قابلاً للتوسع** — إضافة نوع إجراء جديد دون تعديل الـ Schema
- **تكوينياً** — كل شيء من واجهة الإعدادات، لا كود
- **متعدد الإجراءات** — الموافقة قد تُطلق عدة إجراءات (رفع السرية + توليد + إشعار)
- **قابل للتفرع** — مسارات مختلفة حسب نوع الرد (موافق، رفض، يؤجل، أو خيارات مخصصة)
- **متوافق مع BPM** — يتبع أنماط أنظمة الأعمال (Power Automate، Elsa، Oracle)

---

## 2. هيكل ActionConfigJson المقترح

نستخدم **ActionConfigJson** الموجود في `WF_Steps` بدون تغيير في الـ Schema:

```json
{
  "responseActions": {
    "onApprove": [
      { "actionType": "ChangeSecurity", "selectedValue": "guid-mستوى-السرية" },
      { "actionType": "ExecuteProc", "selectedValue": "SP_PostBond", "params": {} },
      { "actionType": "ChangeStage", "selectedValue": "guid-المرحلة" }
    ],
    "onReject": [
      { "actionType": "ExecuteProc", "selectedValue": "SP_RejectDocument" },
      { "actionType": "SendEmail", "selectedValue": "User:guid-المرسل", "templateKey": "rejection_notice" }
    ],
    "onPostpone": [
      { "actionType": "UpdateField", "field": "PostponedUntil", "value": "{{Now+24h}}" }
    ]
  },
  "customButtons": [
    { "code": "Approve10", "label": "موافق حتى 10%", "actions": [...] }
  ],
  "advanceToNextStep": true
}
```

**شرح الحقول:**

| المفتاح | الوصف |
|--------|--------|
| `responseActions` | إجراءات لكل نوع رد (موافق، رفض، تأجيل) |
| `onApprove` / `onReject` / `onPostpone` | مصفوفة إجراءات — تُنفَّذ بالترتيب |
| `actionType` | نوع الإجراء (مطابق لـ WorkflowDesigner) |
| `selectedValue` | القيمة/المعرف |
| `params` | معاملات إضافية لكل إجراء |
| `customButtons` | أزرار مخصصة (مثلاً: موافق بخصم 10%) |
| `advanceToNextStep` | هل ننتقل للخطوة التالية بعد تنفيذ الإجراءات |

---

## 3. جدول WF_ActionTypes (اختياري — للتوسع المستقبلي)

لتسجيل أنواع الإجراءات وإمكانيات كل نوع دون تغيير الكود:

```sql
CREATE TABLE WF_ActionTypes (
  Id int IDENTITY PRIMARY KEY,
  Code nvarchar(50) NOT NULL UNIQUE,      -- ChangeSecurity, ExecuteProc, ...
  DisplayNameAr nvarchar(100),
  DisplayNameEn nvarchar(100),
  Category nvarchar(50),                   -- Document, Notification, Security
  ConfigSchema nvarchar(max),              -- JSON Schema للـ params
  HandlerAssembly nvarchar(200) NULL,      -- للتوسع بـ Plugins
  IsActive bit DEFAULT 1
);
```

- يُمكن البدء بدون هذا الجدول، وربطه لاحقاً عند الحاجة لواجهة تكوين ذكية.

---

## 4. نمط Action Handler (Plugin-ready)

في C# نعرّف واجهة موحّدة لكل إجراء:

```csharp
public interface IWorkflowActionHandler
{
    string ActionType { get; }  // ChangeSecurity, ExecuteProc, ...
    Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct = default);
}

public class WorkflowActionContext
{
    public long LogId { get; set; }
    public Guid SourceRecordId { get; set; }
    public string SourceTable { get; set; }
    public int WorkflowId { get; set; }
    public int StepOrder { get; set; }
    public string ResponseType { get; set; }  // Approved, Rejected, Postponed
    public Dictionary<string, object> ActionParams { get; set; }
    public string SelectedValue { get; set; }
}
```

**مثال تطبيق:**

```csharp
public class ChangeSecurityHandler : IWorkflowActionHandler
{
    public string ActionType => "ChangeSecurity";
    public async Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct)
    {
        // تحديث مستوى السرية في المستند المصدر
        await UpdateDocumentSecurity(ctx.SourceTable, ctx.SourceRecordId, ctx.SelectedValue);
    }
}
```

- إضافة نوع جديد = إضافة Handler وتَسجيله في الـ DI.

---

## 5. مسار التنفيذ

```
المستخدم يضغط "موافق"
    → Approve_ProcessResponse أو ResponseActionExecutor
    → قراءة WF_Steps.ActionConfigJson للخطوة الحالية
    → استخراج responseActions.onApprove
    → لكل إجراء في المصفوفة:
        → resolve IWorkflowActionHandler المناسب
        → ExecuteAsync(ctx)
    → إذا advanceToNextStep: الانتقال للخطوة التالية
    → تحديث WF_Logs
```

---

## 6. مراحل التطبيق (Roadmap)

| المرحلة | المحتوى | الأولوية |
|---------|---------|----------|
| **1** | دعم `responseActions` في ActionConfigJson + تنفيذ من Approve_ProcessResponse | عالية |
| **2** | تنفيذ Handlers أساسية: ChangeSecurity, ExecuteProc, ChangeStage | عالية |
| **3** | واجهة WorkflowDesigner لتكوين responseActions | عالية |
| **4** | دعم customButtons و خيارات مخصصة | متوسطة |
| **5** | WF_ActionTypes + واجهة إدارة أنواع الإجراءات | متوسطة |
| **6** | Plugin assembly loading (إجراءات خارجية) | منخفضة |

---

## 7. مقارنة سريعة

| الجانب | وافق الأصلي | التصميم المقترح |
|--------|-------------|------------------|
| إجراء الموافقة | غالباً إجراء ثابت أو خطوة واحدة | مصفوفة إجراءات قابلة للتكوين |
| مصدر الإعداد | كود أو إعداد محدود | JSON في الخطوة + واجهة Designer |
| إضافة نوع جديد | تعديل كود | إضافة Handler + تسجيل |
| تفرع المسارات | محدود | متعدد (موافق، رفض، تأجيل، مخصص) |
| التوثيق والتتبع | بسيط | يمكن توسيعه (سجلات، تقارير) |

---

## 8. ملخص التنفيذ (تم)

- **ResponseActionExecutor** + **IWorkflowActionHandler** — تنفيذ إجراءات onApprove/onReject/onPostpone من ActionConfigJson
- **ChangeSecurityHandler, ExecuteProcHandler, ChangeStageHandler** — معالجات جاهزة
- **WorkflowEngineWorker** — تقييم StepCondition قبل إرسال الميل، وتخطي الخطوة عند فشل الشرط
- **InboundEmailCommandWorker** — معالجة WF_EmailLogs (ExecutionStatus='Pending') وتنفيذ الأوامر والرد
- **محاكاة ميل وارد** — في صفحة EmailCommands للتجربة دون IMAP

## 9. التوصية النهائية

**البدء بالمرحلة 1 و 2:**

1. توحيد schema لـ `responseActions` داخل `ActionConfigJson`.
2. تعديل `Approve_ProcessResponse` (أو منفذ مركزي في C#) لقراءة وتنفيذ الإجراءات من JSON.
3. تنفيذ Handlers أساسية: `ChangeSecurity`, `ExecuteProc`, `ChangeStage`.
4. إضافة شاشة تكوين في WorkflowDesigner لإعداد `onApprove`, `onReject`, `onPostpone` لكل خطوة SendEmail.

بهذا نضع أساساً قابلاً للتوسع دون تغيير هيكل قواعد البيانات الحالي، مع إمكانية الانتقال لاحقاً إلى WF_ActionTypes و Plugins.
