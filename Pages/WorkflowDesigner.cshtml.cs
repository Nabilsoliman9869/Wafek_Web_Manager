using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data;

namespace Wafek_Web_Manager.Pages
{
    public class WorkflowDesignerModel : PageModel
    {
        private string GetConnectionString()
        {
            try
            {
                var path = ConfigHelper.GetConfigFilePath();
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path);
                    var s = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    var server = s.GetProperty("DbServer").GetString();
                    var db = s.GetProperty("DbName").GetString();
                    var user = s.GetProperty("DbUser").GetString();
                    var pass = s.GetProperty("DbPassword").GetString();
                    var encrypt = s.TryGetProperty("DbEncrypt", out var enc) ? enc.GetBoolean() : true;
                    return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt={encrypt};";
                }
            }
            catch { }
            return "";
        }

        public WorkflowDefDto SelectedWorkflow { get; set; }
        public IList<StepDesignerDto> Steps { get; set; } = new List<StepDesignerDto>();

        // قوائم البيانات (Data Lists)
        public List<SelectListItem> ActionTypes { get; set; }
        public List<SelectListItem> Stages { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> CostCenters { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Projects { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> SecurityLevels { get; set; }
        public List<SelectListItem> EmailRecipients { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null) return RedirectToPage("/WorkflowCreate");

            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return NotFound();

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                // Load Definition from WF_Definitions (EmailFormatQuery إن وُجد)
                try
                {
                    using (var cmd = new SqlCommand("SELECT Id, Name, Description, IsActive, SourceTable, TriggerEvent, ConditionSql, EmailFormatQuery FROM WF_Definitions WHERE Id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id.Value);
                        using var r = await cmd.ExecuteReaderAsync();
                        if (!await r.ReadAsync()) return NotFound();
                        SelectedWorkflow = new WorkflowDefDto
                        {
                            Id = r.GetInt32(0),
                            Name = r.GetString(1),
                            Description = r.IsDBNull(2) ? null : r.GetString(2),
                            IsActive = r.GetBoolean(3),
                            SourceTable = r.GetString(4),
                            TriggerEvent = r.GetString(5),
                            ConditionSql = r.IsDBNull(6) ? null : r.GetString(6),
                            EmailFormatQuery = r.FieldCount > 7 && !r.IsDBNull(7) ? r.GetString(7) : null
                        };
                    }
                }
                catch
                {
                    using (var cmd = new SqlCommand("SELECT Id, Name, Description, IsActive, SourceTable, TriggerEvent, ConditionSql FROM WF_Definitions WHERE Id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id.Value);
                        using var r = await cmd.ExecuteReaderAsync();
                        if (!await r.ReadAsync()) return NotFound();
                        SelectedWorkflow = new WorkflowDefDto
                        {
                            Id = r.GetInt32(0),
                            Name = r.GetString(1),
                            Description = r.IsDBNull(2) ? null : r.GetString(2),
                            IsActive = r.GetBoolean(3),
                            SourceTable = r.GetString(4),
                            TriggerEvent = r.GetString(5),
                            ConditionSql = r.IsDBNull(6) ? null : r.GetString(6),
                            EmailFormatQuery = null
                        };
                    }
                }

                // Load Steps from WF_Steps
                Steps.Clear();
                using (var cmd = new SqlCommand("SELECT Id, StepOrder, StepName, ActionType, SelectedValue, StepCondition FROM WF_Steps WHERE WorkflowDefinitionId = @id ORDER BY StepOrder", conn))
                {
                    cmd.Parameters.AddWithValue("@id", id.Value);
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        Steps.Add(new StepDesignerDto
                        {
                            Id = r.GetInt32(0),
                            StepOrder = r.GetInt32(1),
                            StepName = r.GetString(2),
                            ActionType = r.GetString(3),
                            SelectedValue = r.IsDBNull(4) ? "" : r.GetString(4),
                            StepCondition = r.IsDBNull(5) ? "" : r.GetString(5)
                        });
                    }
                }
            }
            catch
            {
                return NotFound();
            }

            LoadLookups(connStr);
            return Page();
        }

        public async Task<IActionResult> OnPostAddStepAsync(int WorkflowId, int StepOrder, string StepName, string ActionType, string SelectedValue, string StepCondition)
        {
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return RedirectToPage(new { id = WorkflowId });

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand(
                    "INSERT INTO WF_Steps (WorkflowDefinitionId, StepOrder, StepName, ActionType, SelectedValue, StepCondition, ActionConfigJson) VALUES (@wid, @so, @sn, @at, @sv, @sc, '{}')", conn);
                cmd.Parameters.AddWithValue("@wid", WorkflowId);
                cmd.Parameters.AddWithValue("@so", StepOrder);
                cmd.Parameters.AddWithValue("@sn", StepName ?? "Step " + StepOrder);
                cmd.Parameters.AddWithValue("@at", ActionType ?? "");
                cmd.Parameters.AddWithValue("@sv", (object)SelectedValue ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@sc", (object)StepCondition ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }

            return RedirectToPage(new { id = WorkflowId });
        }

        public async Task<IActionResult> OnPostDeleteStepAsync(int id, int wfId)
        {
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return RedirectToPage(new { id = wfId });

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("DELETE FROM WF_Steps WHERE Id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }

            return RedirectToPage(new { id = wfId });
        }

        public async Task<IActionResult> OnPostSaveFormatQueryAsync(int WorkflowId, string EmailFormatQuery)
        {
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return RedirectToPage(new { id = WorkflowId });

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();
                using var cmd = new SqlCommand("UPDATE WF_Definitions SET EmailFormatQuery = @q WHERE Id = @id", conn);
                cmd.Parameters.AddWithValue("@q", string.IsNullOrWhiteSpace(EmailFormatQuery) ? (object)DBNull.Value : EmailFormatQuery.Trim());
                cmd.Parameters.AddWithValue("@id", WorkflowId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch { }

            return RedirectToPage(new { id = WorkflowId });
        }

        private void LoadLookups(string connStr)
        {
            ActionTypes = new List<SelectListItem>
            {
                new SelectListItem { Value = "ChangeSecurity", Text = "رفع سرية (Change Security)" },
                new SelectListItem { Value = "ChangeStage", Text = "مرحل الفاتورة (Current Stage)" },
                new SelectListItem { Value = "ExecuteProc", Text = "تشغيل بروسيجر (Execute Proc)" },
                new SelectListItem { Value = "UpdateTable", Text = "تحديث جدول (Update Table)" },
                new SelectListItem { Value = "SendEmail", Text = "إرسال ميل (Send Email)" },
                new SelectListItem { Value = "SetCostCenter", Text = "حمل مركز كلفة (Set Cost Center)" },
                new SelectListItem { Value = "SetProject", Text = "حمل مشروع (Set Project)" },
                new SelectListItem { Value = "SendReport", Text = "ارسل تقرير (Send Report)" },
                new SelectListItem { Value = "ExecuteFunction", Text = "شغل فانكشن (Execute Function)" }
            };

            SecurityLevels = new List<SelectListItem>
            {
                new SelectListItem { Value = "1", Text = "منخفض (Low)" },
                new SelectListItem { Value = "2", Text = "عادي (Normal)" },
                new SelectListItem { Value = "3", Text = "متوسط (Medium)" },
                new SelectListItem { Value = "4", Text = "مرتفع (High)" }
            };

            EmailRecipients = new List<SelectListItem>
            {
                new SelectListItem { Value = "Dynamic:DocumentAgent", Text = "العميل في المستند (Document Agent)" },
                new SelectListItem { Value = "Dynamic:CreatedBy", Text = "منشئ المستند (Created By)" },
                new SelectListItem { Value = "Dynamic:SalesMan", Text = "المندوب (Sales Rep)" },
                new SelectListItem { Value = "Dynamic:Manager", Text = "المدير المباشر (Direct Manager)" }
            };

            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT StageGuide, StageName FROM Approve_Stage";
                    try { using (var r = cmd.ExecuteReader()) while (r.Read()) Stages.Add(new SelectListItem { Value = r[0].ToString(), Text = r[1].ToString() }); } catch { }

                    cmd.CommandText = "SELECT CostCenterGuide, CostCenterName FROM TBL005";
                    try { using (var r = cmd.ExecuteReader()) while (r.Read()) CostCenters.Add(new SelectListItem { Value = r[0].ToString(), Text = r[1].ToString() }); } catch { }

                    cmd.CommandText = "SELECT ProjectGuide, ProjectName FROM TBL049";
                    try { using (var r = cmd.ExecuteReader()) while (r.Read()) Projects.Add(new SelectListItem { Value = r[0].ToString(), Text = r[1].ToString() }); } catch { }

                    cmd.CommandText = "SELECT UsGuide, UserName FROM TBL013 WHERE NotActive = 0";
                    try
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                var userItem = new SelectListItem { Value = "User:" + r[0].ToString(), Text = "مستخدم: " + r[1].ToString() };
                                EmailRecipients.Add(userItem);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public class WorkflowDefDto
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public bool IsActive { get; set; }
            public string SourceTable { get; set; }
            public string TriggerEvent { get; set; }
            public string ConditionSql { get; set; }
            public string? EmailFormatQuery { get; set; }
        }

        public class StepDesignerDto
        {
            public int Id { get; set; }
            public int StepOrder { get; set; }
            public string StepName { get; set; }
            public string ActionType { get; set; }
            public string SelectedValue { get; set; }
            public string StepCondition { get; set; }
        }
    }
}