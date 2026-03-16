using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Wafek_Web_Manager.Models
{
    // تعريف الورك فلو الأساسي (الرأس)
    public class WorkflowDefinition
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } // اسم الورك فلو (مثلاً: اعتماد مشتريات > 10000)

        [MaxLength(500)]
        public string Description { get; set; }

        public bool IsActive { get; set; } = true;

        // المصدر (Trigger Source)
        [Required]
        [MaxLength(50)]
        public string SourceTable { get; set; } // TBL009, TBL020, TBL085 (Archive)

        // تحديد نوع المستند بدقة (CardGuide)
        // مثال: معرف سند القبض، معرف فاتورة المبيعات
        public Guid? SpecificDocTypeGuid { get; set; }

        [Required]
        [MaxLength(50)]
        public string TriggerEvent { get; set; } // OnBeforeInsert, OnAfterInsert, OnUpdate, OnDelete

        public string ConditionSql { get; set; } // شرط SQL (مثلاً: NetTotal > 5000 AND BranchID = 1)
    }

    // خطوات الورك فلو (التسلسل)
    public class WorkflowStep
    {
        [Key]
        public int Id { get; set; }

        public int WorkflowDefinitionId { get; set; }

        public int StepOrder { get; set; } // ترتيب الخطوة (1, 2, 3...)

        [Required]
        [MaxLength(100)]
        public string StepName { get; set; } // اسم الخطوة (مثلاً: موافقة المدير المالي)

        // شرط تنفيذ الخطوة (Condition at Step Level)
        // مثال: Amount > 50000 (إذا تحقق الشرط تنفذ الخطوة، وإلا يتم تجاوزها)
        [MaxLength(500)]
        public string StepCondition { get; set; }

        // نوع الأكشن (Email, CreateBond, CreateInvoice, Report, UpdateTable, ExecuteProc, ChangeSecurity, ChangeStage, LoadCostCenter, LoadProject)
        [Required]
        [MaxLength(50)]
        public string ActionType { get; set; }

        // تفاصيل الأكشن (JSON Configuration)
        // هنا نخزن كل التفاصيل: نص الإيميل، الأزرار، الحقول المراد تحديثها، القيم الافتراضية
        public string ActionConfigJson { get; set; }

        // القيمة المختارة (Selected Value)
        // لتخزين الـ Guid أو القيمة التي اختارها المستخدم (مثل CostCenterID, StageID, SecurityLevel)
        public string SelectedValue { get; set; }

        // المهلة الزمنية (Escalation)
        public int? TimeoutHours { get; set; } // كم ساعة مسموح للتأخير؟
        public string TimeoutActionJson { get; set; } // ماذا نفعل عند انتهاء الوقت؟ (مثلاً: حول للمدير العام)
    }

    // سجل العمليات (Log) - لتتبع ما حدث
    public class WorkflowLog
    {
        [Key]
        public long Id { get; set; }

        public int WorkflowDefinitionId { get; set; }
        public Guid SourceRecordId { get; set; } // CardGuide للسند/الفاتورة

        public int CurrentStepOrder { get; set; }
        public string Status { get; set; } // Pending, Approved, Rejected, Error

        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime? LastUpdatedDate { get; set; }

        public string LastActionLog { get; set; } // تفاصيل آخر عملية تمت
    }
}