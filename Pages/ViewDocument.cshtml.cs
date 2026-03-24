using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;
using Wafek_Web_Manager.Services;

namespace Wafek_Web_Manager.Pages
{
    public class ViewDocumentModel : PageModel
    {
        public long LogId { get; set; }
        public string? DocumentHtml { get; set; }

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

        public IActionResult OnGet(long id)
        {
            LogId = id;
            var connStr = GetConnectionString();
            if (string.IsNullOrEmpty(connStr)) return Page();

            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();

                using var cmd = new SqlCommand(@"
                    SELECT L.SourceRecordId, D.SourceTable
                    FROM WF_Logs L
                    JOIN WF_Definitions D ON D.Id = L.WorkflowDefinitionId
                    WHERE L.Id = @id", conn);
                cmd.Parameters.AddWithValue("@id", LogId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return Page();

                var sourceId = r.GetGuid(0);
                var sourceTable = r.GetString(1);
                r.Close();

                var docData = EmailBodyBuilder.GetDocumentData(conn, sourceId, sourceTable);

                DocumentHtml = sourceTable == "TBL010"
                    ? EmailBodyBuilder.BuildBondPrintBlock(docData)
                    : sourceTable == "TBL022"
                        ? EmailBodyBuilder.BuildInvoicePrintBlock(docData)
                        : null;

                if (string.IsNullOrEmpty(DocumentHtml))
                    DocumentHtml = null;
            }
            catch { }

            return Page();
        }
    }
}
