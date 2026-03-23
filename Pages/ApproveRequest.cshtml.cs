using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;
using Wafek_Web_Manager.Services;

namespace Wafek_Web_Manager.Pages
{
    public class ApproveRequestModel : PageModel
    {
        private readonly Wafek_Web_Manager.Services.ResponseActionExecutor? _responseExecutor;

        public ApproveRequestModel(Wafek_Web_Manager.Services.ResponseActionExecutor? responseExecutor = null)
        {
            _responseExecutor = responseExecutor;
        }

        private string GetConnectionString()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (System.IO.File.Exists(configPath))
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var s = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    var server = s.GetProperty("DbServer").GetString();
                    var db = s.GetProperty("DbName").GetString();
                    var user = s.GetProperty("DbUser").GetString();
                    var pass = s.GetProperty("DbPassword").GetString();
                    return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=False;";
                }
            }
            catch { }
            return "";
        }

        public long LogId { get; set; }
        public ApproveRequestDto? Request { get; set; }
        public string RecipientName { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsSuccess { get; set; }

        public IActionResult OnGet(long? id, string? action)
        {
            if (id == null || id == 0)
            {
                LogId = 0;
                Request = null;
                return Page();
            }
            LogId = id.Value;
            if (!string.IsNullOrWhiteSpace(action))
            {
                var responseType = action.Trim() switch
                {
                    "Approved" or "موافق" or "1" => "Approved",
                    "Rejected" or "رفض" or "2" => "Rejected",
                    "Postponed" or "يؤجل" or "3" => "Postponed",
                    _ => null
                };
                if (responseType != null)
                {
                    ProcessResponse(LogId, responseType, null);
                    LoadRequest(true); // Ignore status check when processing action
                    Message = responseType == "Approved" ? "تمت الموافقة بنجاح." : responseType == "Rejected" ? "تم الرفض." : "تم التأجيل.";
                    IsSuccess = responseType != "Rejected";
                    return Page();
                }
            }
            LoadRequest();
            return Page();
        }

        public IActionResult OnPostApprove(long logId)
        {
            LogId = logId;
            ProcessResponse(logId, "Approved", null);
            LoadRequest(true);
            Message = "تمت الموافقة بنجاح.";
            IsSuccess = true;
            return Page();
        }

        public IActionResult OnPostReject(long logId)
        {
            LogId = logId;
            ProcessResponse(logId, "Rejected", null);
            LoadRequest(true);
            Message = "تم الرفض.";
            IsSuccess = false;
            return Page();
        }

        public IActionResult OnPostPostpone(long logId)
        {
            LogId = logId;
            ProcessResponse(logId, "Postponed", null);
            LoadRequest(true);
            Message = "تم التأجيل. سيُعاد عرض الطلب لاحقاً.";
            IsSuccess = true;
            return Page();
        }

        /// <summary>حفظ حسب الحالة المختارة — نمط وافق الأصلي (قائمة منسدلة + حفظ)</summary>
        public IActionResult OnPostSave(long logId, string state, string? responseText)
        {
            LogId = logId;
            var responseType = state?.Trim() switch
            {
                "Approved" or "موافق" or "1" => "Approved",
                "Rejected" or "غير موفق" or "إرجاع" or "2" => "Rejected",
                "Postponed" or "يؤجل" or "انتظار" or "3" => "Postponed",
                _ => "Postponed"
            };
            ProcessResponse(logId, responseType, responseText);
            LoadRequest(true);
            Message = responseType switch
            {
                "Approved" => "تمت الموافقة بنجاح.",
                "Rejected" => "تم الرفض.",
                _ => "تم التأجيل. سيُعاد عرض الطلب لاحقاً."
            };
            IsSuccess = responseType != "Rejected";
            return Page();
        }

        private void LoadRequest(bool ignoreStatus = false)
        {
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return;

            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();

                var statusCondition = ignoreStatus ? "" : "AND L.Status IN ('Pending', 'WaitingForResponse')";
                
                using var cmd = new SqlCommand($@"
                    SELECT L.Id, L.SourceRecordId, L.WorkflowDefinitionId, L.CurrentStepOrder,
                           D.SourceTable, S.SelectedValue
                    FROM WF_Logs L
                    JOIN WF_Definitions D ON D.Id = L.WorkflowDefinitionId
                    LEFT JOIN WF_Steps S ON S.WorkflowDefinitionId = L.WorkflowDefinitionId AND S.StepOrder = L.CurrentStepOrder
                    WHERE L.Id = @id {statusCondition}", conn);
                cmd.Parameters.AddWithValue("@id", LogId);
                
                Guid sourceId;
                string sourceTable;
                string selectedValue;
                
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return;
                    sourceId = r.GetGuid(1);
                    sourceTable = r.GetString(4);
                    selectedValue = r.IsDBNull(5) ? "" : r.GetString(5);
                }
                
                // IMPORTANT: Close the connection here before calling EmailBodyBuilder, 
                // because EmailBodyBuilder uses the same connection string and might 
                // cause "There is already an open DataReader" if MARS is not enabled.
                conn.Close();

                var builder = new EmailBodyBuilder(connStr);
                var docData = builder.GetDocumentData(sourceId, sourceTable);

                RecipientName = ResolveRecipientName(conn, selectedValue);
                Request = new ApproveRequestDto
                {
                    CardName = docData.CardName ?? sourceTable,
                    CardNumber = docData.CardNumber ?? "",
                    SenderName = docData.SenderName ?? "",
                    CompanyName = docData.CompanyName ?? "",
                    CompanyPhone = docData.CompanyPhone ?? "",
                    Notes = docData.Notes ?? "",
                    BondDate = docData.BondDate != default ? docData.BondDate.ToString("yyyy-MM-dd") : "",
                    BranchName = docData.BranchName ?? "",
                    CostCenterName = docData.CostCenterName ?? "",
                    ProjectName = docData.ProjectName ?? "",
                    CurrencyName = docData.CurrencyName ?? "",
                    AccountName = docData.AccountName ?? "",
                    TotalAmount = docData.TotalAmount ?? "",
                    BondPrintHtml = docData.SourceTable == "TBL010" ? EmailBodyBuilder.BuildBondPrintBlock(docData)
                    : docData.SourceTable == "TBL022" ? EmailBodyBuilder.BuildInvoicePrintBlock(docData) : ""
                };
            }
            catch { }
        }

        private static string ResolveRecipientName(SqlConnection conn, string rule)
        {
            if (string.IsNullOrEmpty(rule)) return "";
            var val = rule.Trim();
            if (val.StartsWith("User:", StringComparison.OrdinalIgnoreCase)) val = val.Substring(5).Trim();
            if (!Guid.TryParse(val, out var g)) return "";
            try
            {
                var cmd = new SqlCommand("SELECT UserName FROM TBL013 WHERE UsGuide = @u", conn);
                cmd.Parameters.AddWithValue("@u", g);
                var r = cmd.ExecuteScalar();
                if (r != null && !string.IsNullOrEmpty(r.ToString())) return r.ToString();
                cmd.CommandText = "SELECT TBL016.AgentName FROM TBL016 JOIN TBL013 ON TBL013.RelatedAgent = TBL016.CardGuide WHERE TBL013.UsGuide = @u";
                r = cmd.ExecuteScalar();
                return r?.ToString() ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// معالجة الرد — مسار SQL أولاً (نفس الرد بالميل): Approve_ProcessResponse → Approve_OnApproved/OnRejected.
        /// إن فشل الإجراء (مثلاً غير منشور) يُستدعى ResponseActionExecutor ثم احتياطي UPDATE.
        /// </summary>
        private void ProcessResponse(long logId, string responseType, string? responseText)
        {
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return;

            // 1) مسار SQL — روابط ريندر والرد بالميل يستخدمان نفس المنطق
            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();
                var cmd = new SqlCommand("EXEC Approve_ProcessResponse @LogId, @ResponseType", conn);
                cmd.CommandTimeout = 30;
                cmd.Parameters.AddWithValue("@LogId", logId);
                cmd.Parameters.AddWithValue("@ResponseType", responseType);
                cmd.ExecuteNonQuery();
                return;
            }
            catch { }

            // 2) احتياطي C#
            if (_responseExecutor != null)
            {
                try
                {
                    _responseExecutor.ProcessResponseAsync(logId, responseType, connStr).GetAwaiter().GetResult();
                    return;
                }
                catch { }
            }

            // 3) احتياطي UPDATE بسيط على WF_Logs
            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();
                var msg = responseType switch
                {
                    "Approved" => "تمت الموافقة من الرابط",
                    "Rejected" => "تم الرفض من الرابط",
                    "Postponed" => "تم التأجيل من الرابط",
                    _ => responseType
                };
                if (!string.IsNullOrWhiteSpace(responseText))
                    msg = msg + " | " + responseText.Trim();
                var cmd = new SqlCommand("UPDATE WF_Logs SET Status = @s, LastActionLog = @m, LastUpdatedDate = GETDATE() WHERE Id = @id", conn);
                cmd.Parameters.AddWithValue("@s", responseType);
                cmd.Parameters.AddWithValue("@m", msg);
                cmd.Parameters.AddWithValue("@id", logId);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public class ApproveRequestDto
        {
            public string CardName { get; set; } = "";
            public string CardNumber { get; set; } = "";
            public string SenderName { get; set; } = "";
            public string CompanyName { get; set; } = "";
            public string CompanyPhone { get; set; } = "";
            public string Notes { get; set; } = "";
            public string BondDate { get; set; } = "";
            public string BranchName { get; set; } = "";
            public string CostCenterName { get; set; } = "";
            public string ProjectName { get; set; } = "";
            public string CurrencyName { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string TotalAmount { get; set; } = "";
            /// <summary>كتلة طباعة السند (سند قبض) للعرض والطباعة</summary>
            public string BondPrintHtml { get; set; } = "";
        }
    }
}
