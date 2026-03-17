using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;

namespace Wafek_Web_Manager.Pages
{
    public class WorkflowReviewModel : PageModel
    {
        private string GetConnectionString()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (System.IO.File.Exists(configPath))
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    string server = settings.GetProperty("DbServer").GetString() ?? "";
                    string db = settings.GetProperty("DbName").GetString() ?? "";
                    string user = settings.GetProperty("DbUser").GetString() ?? "";
                    string pass = settings.GetProperty("DbPassword").GetString() ?? "";
                    System.Text.Json.JsonElement enc;
                    var encrypt = settings.TryGetProperty("DbEncrypt", out enc) ? enc.GetBoolean() : false;
                    return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt={encrypt};Connect Timeout=30;";
                }
            }
            catch { }
            return "";
        }

        public int WorkflowId { get; set; }
        public WorkflowDefDto Definition { get; set; }
        public List<StepReviewDto> Steps { get; set; } = new();

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            WorkflowId = id ?? 222;
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr))
            {
                return Page();
            }

            try
            {
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                // 1. Load Definition
                using (var cmd = new SqlCommand(
                    "SELECT Id, Name, Description, IsActive, SourceTable, TriggerEvent, ConditionSql FROM WF_Definitions WHERE Id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", WorkflowId);
                    using var r = await cmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        Definition = new WorkflowDefDto
                        {
                            Id = r.GetInt32(0),
                            Name = r.GetString(1),
                            Description = r.IsDBNull(2) ? null : r.GetString(2),
                            IsActive = r.GetBoolean(3),
                            SourceTable = r.GetString(4),
                            TriggerEvent = r.GetString(5),
                            ConditionSql = r.IsDBNull(6) ? null : r.GetString(6)
                        };
                    }
                }

                if (Definition == null) return Page();

                // 2. Load Steps
                var stepRows = new List<(int Order, string Name, string ActionType, string SelectedValue, string Condition)>();
                using (var cmd = new SqlCommand(
                    "SELECT StepOrder, StepName, ActionType, SelectedValue, StepCondition FROM WF_Steps WHERE WorkflowDefinitionId = @id ORDER BY StepOrder", conn))
                {
                    cmd.Parameters.AddWithValue("@id", WorkflowId);
                    using var r = await cmd.ExecuteReaderAsync();
                    while (await r.ReadAsync())
                    {
                        stepRows.Add((
                            r.GetInt32(0),
                            r.GetString(1),
                            r.GetString(2),
                            r.IsDBNull(3) ? null : r.GetString(3),
                            r.IsDBNull(4) ? null : r.GetString(4)
                        ));
                    }
                }

                foreach (var row in stepRows)
                {
                    string resolvedEmail = null, emailNote = null;
                    if (row.ActionType == "SendEmail" && !string.IsNullOrEmpty(row.SelectedValue))
                        (resolvedEmail, emailNote) = ResolveEmail(conn, row.SelectedValue);

                    Steps.Add(new StepReviewDto
                    {
                        StepOrder = row.Order,
                        StepName = row.Name,
                        ActionType = row.ActionType,
                        SelectedValue = row.SelectedValue,
                        StepCondition = row.Condition,
                        ResolvedEmail = resolvedEmail,
                        EmailNote = emailNote
                    });
                }
            }
            catch { }

            return Page();
        }

        private (string email, string note) ResolveEmail(SqlConnection conn, string rule)
        {
            if (string.IsNullOrEmpty(rule)) return (null, null);

            // مباشرة إيميل
            if (rule.Contains("@")) return (rule, "إيميل مباشر");

            // User:Guid
            var val = rule;
            if (val.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
                val = val.Substring(5).Trim();

            if (Guid.TryParse(val, out var userGuid))
            {
                try
                {
                    using var cmd = new SqlCommand(@"
                        SELECT TBL016.EMail 
                        FROM TBL016 
                        JOIN TBL013 ON TBL013.RelatedAgent = TBL016.CardGuide 
                        WHERE TBL013.UsGuide = @uid", conn);
                    cmd.Parameters.AddWithValue("@uid", userGuid);
                    var res = cmd.ExecuteScalar();
                    if (res != null && !string.IsNullOrEmpty(res.ToString()))
                        return (res.ToString(), "من TBL016 عبر UsGuide");
                }
                catch { }
                return (null, "لم يُعثر على إيميل لـ " + userGuid);
            }

            return (null, "قيمة غير معروفة");
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
        }

        public class StepReviewDto
        {
            public int StepOrder { get; set; }
            public string StepName { get; set; }
            public string ActionType { get; set; }
            public string SelectedValue { get; set; }
            public string StepCondition { get; set; }
            public string ResolvedEmail { get; set; }
            public string EmailNote { get; set; }
        }
    }
}
