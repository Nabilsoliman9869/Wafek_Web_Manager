using System.Linq;
using Microsoft.Data.SqlClient;

namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// تغيير مستوى السرية في المستند
    /// </summary>
    public class ChangeSecurityHandler : IWorkflowActionHandler
    {
        public string ActionType => "ChangeSecurity";

        public Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ctx.ConnectionString) || string.IsNullOrEmpty(ctx.SelectedValue)) return Task.CompletedTask;
            try
            {
                var col = GetSecurityColumn(ctx.SourceTable);
                if (string.IsNullOrEmpty(col)) return Task.CompletedTask;
                using var conn = new SqlConnection(ctx.ConnectionString);
                conn.Open();
                var cmd = new SqlCommand($"UPDATE {ctx.SourceTable} SET [{col}] = @v WHERE CardGuide = @id", conn);
                cmd.Parameters.AddWithValue("@v", ctx.SelectedValue);
                cmd.Parameters.AddWithValue("@id", ctx.SourceRecordId);
                cmd.ExecuteNonQuery();
            }
            catch { }
            return Task.CompletedTask;
        }

        static string GetSecurityColumn(string table)
        {
            var t = (table ?? "").ToUpperInvariant();
            if (t == "TBL010" || t == "TBL022" || t == "TBL085") return "SecurityLevel";
            return "SecurityLevel";
        }
    }

    /// <summary>
    /// رفع الصلاحية درجة واحدة عند الموافقة — يزيد Security/SecurityLevel بـ 1
    /// </summary>
    public class IncrementSecurityHandler : IWorkflowActionHandler
    {
        public string ActionType => "IncrementSecurity";

        public Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(ctx.ConnectionString)) return Task.CompletedTask;
            try
            {
                var (table, col) = GetSecurityColumn(ctx.SourceTable);
                if (string.IsNullOrEmpty(col)) return Task.CompletedTask;
                using var conn = new SqlConnection(ctx.ConnectionString);
                conn.Open();
                var maxVal = ctx.Params.TryGetValue("max", out var m) && int.TryParse(m, out var mx) ? mx : 4;
                var sql = $@"UPDATE [{table}] SET [{col}] = CASE 
                    WHEN ISNULL(CAST([{col}] AS INT), 0) < {maxVal} THEN ISNULL(CAST([{col}] AS INT), 0) + 1 
                    ELSE {maxVal} END 
                    WHERE CardGuide = @id";
                var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", ctx.SourceRecordId);
                cmd.ExecuteNonQuery();
            }
            catch { }
            return Task.CompletedTask;
        }

        static (string table, string col) GetSecurityColumn(string sourceTable)
        {
            var t = (sourceTable ?? "").ToUpperInvariant();
            if (string.IsNullOrEmpty(t)) return ("", "");
            return (t, "Security");
        }
    }

    /// <summary>
    /// تشغيل إجراء مخزن
    /// </summary>
    public class ExecuteProcHandler : IWorkflowActionHandler
    {
        public string ActionType => "ExecuteProc";

        public Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ctx.ConnectionString) || string.IsNullOrEmpty(ctx.SelectedValue)) return Task.CompletedTask;
            try
            {
                using var conn = new SqlConnection(ctx.ConnectionString);
                conn.Open();
                var cmd = new SqlCommand(ctx.SelectedValue.Trim(), conn) { CommandType = System.Data.CommandType.StoredProcedure, CommandTimeout = 60 };
                cmd.Parameters.AddWithValue("@LogId", ctx.LogId);
                cmd.Parameters.AddWithValue("@SourceTable", ctx.SourceTable);
                cmd.Parameters.AddWithValue("@SourceId", ctx.SourceRecordId);
                cmd.Parameters.AddWithValue("@CardGuide", ctx.SourceRecordId);
                cmd.ExecuteNonQuery();
            }
            catch (SqlException)
            {
                try
                {
                    using var conn = new SqlConnection(ctx.ConnectionString);
                    conn.Open();
                    var cmd = new SqlCommand(ctx.SelectedValue.Trim(), conn) { CommandType = System.Data.CommandType.StoredProcedure, CommandTimeout = 60 };
                    cmd.Parameters.AddWithValue("@SourceId", ctx.SourceRecordId);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// تغيير مرحلة المستند
    /// </summary>
    public class ChangeStageHandler : IWorkflowActionHandler
    {
        public string ActionType => "ChangeStage";

        public Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ctx.ConnectionString) || string.IsNullOrEmpty(ctx.SelectedValue)) return Task.CompletedTask;
            try
            {
                var col = GetStageColumn(ctx.SourceTable);
                if (string.IsNullOrEmpty(col)) return Task.CompletedTask;
                if (!Guid.TryParse(ctx.SelectedValue.Trim(), out var stageGuid)) return Task.CompletedTask;
                using var conn = new SqlConnection(ctx.ConnectionString);
                conn.Open();
                var cmd = new SqlCommand($"UPDATE {ctx.SourceTable} SET [{col}] = @v WHERE CardGuide = @id", conn);
                cmd.Parameters.AddWithValue("@v", stageGuid);
                cmd.Parameters.AddWithValue("@id", ctx.SourceRecordId);
                cmd.ExecuteNonQuery();
            }
            catch { }
            return Task.CompletedTask;
        }

        static string GetStageColumn(string table)
        {
            var t = (table ?? "").ToUpperInvariant();
            if (t == "TBL022") return "StageGuide";
            if (t == "TBL085") return "StageGuide";
            return "StageGuide";
        }
    }

    /// <summary>
    /// تحديث حقل في جدول — params: column, value (واختياري: table)
    /// </summary>
    public class UpdateTableHandler : IWorkflowActionHandler
    {
        public string ActionType => "UpdateTable";

        public Task ExecuteAsync(WorkflowActionContext ctx, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ctx.ConnectionString)) return Task.CompletedTask;
            var table = ctx.Params.TryGetValue("table", out var t) && !string.IsNullOrEmpty(t) ? t : ctx.SourceTable;
            var column = ctx.Params.TryGetValue("column", out var c) ? c : ctx.SelectedValue;
            var value = ctx.Params.TryGetValue("value", out var v) ? v : "";
            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column)) return Task.CompletedTask;
            try
            {
                var safeTable = new string(table.Where(x => char.IsLetterOrDigit(x) || x == '_').ToArray());
                if (string.IsNullOrEmpty(safeTable) || string.IsNullOrEmpty(column)) return Task.CompletedTask;
                using var conn = new SqlConnection(ctx.ConnectionString);
                conn.Open();
                var colClean = column.Trim().Replace("[", "").Replace("]", "");
                var cmd = new SqlCommand($"UPDATE [{safeTable}] SET [{colClean}] = @v WHERE CardGuide = @id", conn);
                cmd.Parameters.AddWithValue("@id", ctx.SourceRecordId);
                var valueType = ctx.Params.TryGetValue("valueType", out var vt) ? vt : "";
                if (Guid.TryParse(value, out var gVal) && (string.IsNullOrEmpty(valueType) || valueType.Equals("guid", StringComparison.OrdinalIgnoreCase)))
                    cmd.Parameters.AddWithValue("@v", gVal);
                else if (valueType.Equals("int", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var iVal))
                    cmd.Parameters.AddWithValue("@v", iVal);
                else if (valueType.Equals("bit", StringComparison.OrdinalIgnoreCase))
                    cmd.Parameters.AddWithValue("@v", value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                else
                    cmd.Parameters.AddWithValue("@v", value ?? "");
                cmd.ExecuteNonQuery();
            }
            catch { }
            return Task.CompletedTask;
        }
    }
}
