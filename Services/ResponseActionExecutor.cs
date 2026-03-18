using System.Text.Json;
using System.Data.SqlClient;

namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// تنفيذ إجراءات الرد من ActionConfigJson (onApprove, onReject, onPostpone)
    /// </summary>
    public class ResponseActionExecutor
    {
        private readonly IEnumerable<IWorkflowActionHandler> _handlers;

        public ResponseActionExecutor(IEnumerable<IWorkflowActionHandler> handlers)
        {
            _handlers = handlers ?? Array.Empty<IWorkflowActionHandler>();
        }

        public async Task ProcessResponseAsync(long logId, string responseType, string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return;

            int wfId; Guid sourceId; string sourceTable; int currentStep; string actionConfigJson;
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT L.WorkflowDefinitionId, L.SourceRecordId, L.CurrentStepOrder,
                           D.SourceTable, S.ActionConfigJson
                    FROM WF_Logs L
                    JOIN WF_Definitions D ON D.Id = L.WorkflowDefinitionId
                    JOIN WF_Steps S ON S.WorkflowDefinitionId = L.WorkflowDefinitionId AND S.StepOrder = L.CurrentStepOrder
                    WHERE L.Id = @id", conn);
                cmd.Parameters.AddWithValue("@id", logId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return;
                wfId = r.GetInt32(0);
                sourceId = r.GetGuid(1);
                currentStep = r.GetInt32(2);
                sourceTable = r.GetString(3);
                actionConfigJson = r.IsDBNull(4) ? null : r.GetString(4);
            }

            var key = responseType switch { "Approved" => "onApprove", "Rejected" => "onReject", "Postponed" => "onPostpone", _ => null };
            if (string.IsNullOrEmpty(key)) return;

            var actions = ParseActions(actionConfigJson, key);
            var ctx = new WorkflowActionContext
            {
                LogId = logId,
                WorkflowId = wfId,
                StepOrder = currentStep,
                SourceRecordId = sourceId,
                SourceTable = sourceTable,
                ResponseType = responseType,
                ConnectionString = connectionString
            };

            foreach (var a in actions)
            {
                ctx.SelectedValue = a.Value;
                ctx.Params = a.Params;
                var handler = _handlers.FirstOrDefault(h => h.ActionType == a.ActionType);
                handler?.ExecuteAsync(ctx).GetAwaiter().GetResult();
            }

            await UpdateWorkflowAndAdvance(logId, responseType, wfId, sourceId, currentStep, connectionString);
        }

        static List<(string ActionType, string Value, Dictionary<string, string> Params)> ParseActions(string json, string key)
        {
            var list = new List<(string, string, Dictionary<string, string>)>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("responseActions", out var ra)) return list;
                if (!ra.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
                foreach (var item in arr.EnumerateArray())
                {
                    var at = item.TryGetProperty("actionType", out var atp) ? atp.GetString() : null;
                    var v = item.TryGetProperty("selectedValue", out var vp) ? vp.GetString() : "";
                    var p = new Dictionary<string, string>();
                    if (item.TryGetProperty("params", out var pr) && pr.ValueKind == JsonValueKind.Object)
                        foreach (var prop in pr.EnumerateObject()) p[prop.Name] = prop.Value.GetString() ?? "";
                    if (!string.IsNullOrEmpty(at)) list.Add((at, v ?? "", p));
                }
            }
            catch { }
            return list;
        }

        async Task UpdateWorkflowAndAdvance(long logId, string responseType, int wfId, Guid sourceId, int currentStep, string connStr)
        {
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            if (responseType == "Approved")
            {
                object next;
                using (var cmdNext = new SqlCommand("SELECT MIN(StepOrder) FROM WF_Steps WHERE WorkflowDefinitionId = @wf AND StepOrder > @cur", conn))
                {
                    cmdNext.Parameters.AddWithValue("@wf", wfId);
                    cmdNext.Parameters.AddWithValue("@cur", currentStep);
                    next = cmdNext.ExecuteScalar();
                }
                if (next != null && next != DBNull.Value && int.TryParse(next.ToString(), out var nextStep))
                {
                    using (var u = new SqlCommand("UPDATE WF_Logs SET CurrentStepOrder = @s, Status = 'Pending', LastActionLog = N'انتقال للخطوة التالية', LastUpdatedDate = GETDATE() WHERE Id = @id", conn))
                    {
                        u.Parameters.AddWithValue("@s", nextStep);
                        u.Parameters.AddWithValue("@id", logId);
                        u.ExecuteNonQuery();
                    }
                    return;
                }
                using (var u2 = new SqlCommand("UPDATE WF_Logs SET Status = 'Approved', LastActionLog = N'تمت الموافقة — اكتمال', LastUpdatedDate = GETDATE() WHERE Id = @id", conn))
                {
                    u2.Parameters.AddWithValue("@id", logId);
                    u2.ExecuteNonQuery();
                }
            }
            else if (responseType == "Rejected")
            {
                using (var u = new SqlCommand("UPDATE WF_Logs SET Status = 'Rejected', LastActionLog = N'تم الرفض', LastUpdatedDate = GETDATE() WHERE Id = @id", conn))
                {
                    u.Parameters.AddWithValue("@id", logId);
                    u.ExecuteNonQuery();
                }
            }
            else if (responseType == "Postponed")
            {
                using (var u = new SqlCommand("UPDATE WF_Logs SET Status = 'Postponed', LastActionLog = N'تم التأجيل', LastUpdatedDate = GETDATE() WHERE Id = @id", conn))
                {
                    u.Parameters.AddWithValue("@id", logId);
                    u.ExecuteNonQuery();
                }
            }
        }
    }
}
