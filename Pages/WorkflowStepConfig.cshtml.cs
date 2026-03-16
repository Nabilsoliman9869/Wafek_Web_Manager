using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Wafek_Web_Manager.Data;

namespace Wafek_Web_Manager.Pages
{
    public class WorkflowStepConfigModel : PageModel
    {
        private readonly WafekDbContext _context;

        public WorkflowStepConfigModel(WafekDbContext context) => _context = context;

        public int StepId { get; set; }
        public int WorkflowId { get; set; }
        public string StepName { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string ActionConfigJson { get; set; } = "{}";
        public string Message { get; set; } = "";
        public bool IsSuccess { get; set; }

        public async Task<IActionResult> OnGetAsync(int stepId)
        {
            StepId = stepId;
            var step = await _context.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId);
            if (step == null) return NotFound();
            WorkflowId = step.WorkflowDefinitionId;
            StepName = step.StepName ?? "";
            ActionType = step.ActionType ?? "";
            ActionConfigJson = string.IsNullOrEmpty(step.ActionConfigJson) ? "{}" : step.ActionConfigJson;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int stepId, string actionConfigJson, string? useTemplate)
        {
            StepId = stepId;
            var step = await _context.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId);
            if (step == null) return NotFound();
            WorkflowId = step.WorkflowDefinitionId;
            StepName = step.StepName ?? "";
            ActionType = step.ActionType ?? "";

            if (!string.IsNullOrEmpty(useTemplate))
            {
                ActionConfigJson = @"{
  ""responseActions"": {
    ""onApprove"": [
      { ""actionType"": ""ChangeSecurity"", ""selectedValue"": ""guid-مستوى-السرية"" },
      { ""actionType"": ""ExecuteProc"", ""selectedValue"": ""SP_PostBond"" }
    ],
    ""onReject"": [
      { ""actionType"": ""ExecuteProc"", ""selectedValue"": ""SP_RejectDocument"" }
    ],
    ""onPostpone"": []
  }
}";
                step.ActionConfigJson = ActionConfigJson;
                await _context.SaveChangesAsync();
                Message = "تم تطبيق القالب الافتراضي.";
                IsSuccess = true;
                return Page();
            }

            if (string.IsNullOrWhiteSpace(actionConfigJson))
            {
                Message = "أدخل JSON صالح.";
                IsSuccess = false;
                ActionConfigJson = step.ActionConfigJson ?? "{}";
                return Page();
            }

            try
            {
                var trimmed = actionConfigJson.Trim();
                if (!trimmed.StartsWith("{")) trimmed = "{" + trimmed + "}";
                _ = System.Text.Json.JsonDocument.Parse(trimmed);
                step.ActionConfigJson = trimmed;
                await _context.SaveChangesAsync();
                ActionConfigJson = trimmed;
                Message = "تم حفظ التوابع بنجاح.";
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                Message = "JSON غير صالح: " + ex.Message;
                IsSuccess = false;
                ActionConfigJson = actionConfigJson;
            }
            return Page();
        }
    }
}
