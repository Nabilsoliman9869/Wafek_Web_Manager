using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Wafek_Web_Manager.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Wafek_Web_Manager.Pages
{
    public class EmailCommandsModel : PageModel
    {
        private readonly ImapFetchService _imapFetch;

        public EmailCommandsModel(ImapFetchService imapFetch) => _imapFetch = imapFetch;

        public List<CommandItem> Commands { get; set; } = new List<CommandItem>();
        public List<EmailLogItem> EmailLogs { get; set; } = new List<EmailLogItem>();
        public string? SimulateMessage { get; set; }
        public bool SimulateSuccess { get; set; }

        private string GetConnectionString()
        {
            try
            {
                if (System.IO.File.Exists("appsettings.custom.json"))
                {
                    var json = System.IO.File.ReadAllText("appsettings.custom.json");
                    var s = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    return $"Server={s.GetProperty("DbServer").GetString()};Database={s.GetProperty("DbName").GetString()};User Id={s.GetProperty("DbUser").GetString()};Password={s.GetProperty("DbPassword").GetString()};TrustServerCertificate=True;Encrypt=True;";
                }
            }
            catch { }
            return "";
        }

        public void OnGet()
        {
            LoadCommands();
            LoadEmailLogs();
        }

        public IActionResult OnPostSimulateInbound(string senderEmail, string commandCode)
        {
            var cs = GetConnectionString();
            if (string.IsNullOrEmpty(cs)) { SimulateMessage = "لا توجد إعدادات الاتصال"; SimulateSuccess = false; LoadCommands(); return Page(); }
            if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(commandCode)) { SimulateMessage = "أدخل البريد والكود"; SimulateSuccess = false; LoadCommands(); return Page(); }

            try
            {
                using var conn = new SqlConnection(cs);
                conn.Open();
                using var cmd = new SqlCommand("INSERT INTO WF_EmailLogs (SenderEmail, Subject, DetectedCommand, ExecutionStatus) VALUES (@e, @s, @c, 'Pending')", conn);
                cmd.Parameters.AddWithValue("@e", senderEmail.Trim());
                cmd.Parameters.AddWithValue("@s", "تلقائي: " + commandCode.Trim());
                cmd.Parameters.AddWithValue("@c", commandCode.Trim());
                cmd.ExecuteNonQuery();
                SimulateMessage = "تم إدراج الميل الوارد. سيعالجه الـ Worker خلال ~30 ثانية ويرسل الرد.";
                SimulateSuccess = true;
            }
            catch (Exception ex) { SimulateMessage = "خطأ: " + ex.Message; SimulateSuccess = false; }
            LoadCommands();
            LoadEmailLogs();
            return Page();
        }

        public async Task<IActionResult> OnPostFetchImapAsync()
        {
            var (total, stored, skipDetail, err) = await _imapFetch.FetchAndStoreAsync();
            LoadCommands();
            LoadEmailLogs();
            if (err != null)
            {
                SimulateMessage = "❌ " + err;
                SimulateSuccess = false;
            }
            else
            {
                SimulateMessage = $"✓ تم فحص {total} ميل، خُزّن {stored} جديد" + (string.IsNullOrEmpty(skipDetail) ? "" : $" — تخطي: {skipDetail}") + ".";
                SimulateSuccess = true;
            }
            return Page();
        }

        public IActionResult OnPostAdd(string code, string desc, string type, string content)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(content)) return Page();

            var cs = GetConnectionString();
            if (string.IsNullOrEmpty(cs)) return Page();

            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    conn.Open();
                    var sql = "INSERT INTO WF_EmailCommands (CommandCode, Description, ActionType, ExecutionContent) VALUES (@c, @d, @t, @e)";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@c", code);
                        cmd.Parameters.AddWithValue("@d", desc ?? "");
                        cmd.Parameters.AddWithValue("@t", type);
                        cmd.Parameters.AddWithValue("@e", content);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
            return RedirectToPage();
        }

        public IActionResult OnPostDelete(int id)
        {
            var cs = GetConnectionString();
            if (string.IsNullOrEmpty(cs)) return RedirectToPage();
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    conn.Open();
                    var sql = "DELETE FROM WF_EmailCommands WHERE Id = @id";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { }
            return RedirectToPage();
        }

        private void LoadEmailLogs()
        {
            var cs = GetConnectionString();
            if (string.IsNullOrEmpty(cs)) return;
            try
            {
                using var conn = new SqlConnection(cs);
                conn.Open();
                using var cmd = new SqlCommand(@"
                    SELECT TOP 20 Id, SenderEmail, Subject, DetectedCommand, ExecutionStatus, 
                           CONVERT(varchar, ReceivedDate, 120) as ReceivedDate, ResultMessage
                    FROM WF_EmailLogs ORDER BY ReceivedDate DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    EmailLogs.Add(new EmailLogItem
                    {
                        Id = r.GetInt64(0),
                        SenderEmail = r.GetString(1),
                        Subject = r.IsDBNull(2) ? "" : r.GetString(2),
                        DetectedCommand = r.IsDBNull(3) ? "" : r.GetString(3),
                        ExecutionStatus = r.GetString(4),
                        ReceivedDate = r.IsDBNull(5) ? "" : r.GetString(5),
                        ResultMessage = r.IsDBNull(6) ? "" : r.GetString(6)
                    });
                }
            }
            catch { }
        }

        private void LoadCommands()
        {
            var cs = GetConnectionString();
            if (string.IsNullOrEmpty(cs)) return;
            try
            {
                using (var conn = new SqlConnection(cs))
                {
                    conn.Open();
                    var sql = "SELECT * FROM WF_EmailCommands ORDER BY CommandCode";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                Commands.Add(new CommandItem
                                {
                                    Id = (int)r["Id"],
                                    Code = r["CommandCode"].ToString(),
                                    Description = r["Description"].ToString(),
                                    Type = r["ActionType"].ToString(),
                                    Content = r["ExecutionContent"].ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public class CommandItem
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Description { get; set; }
            public string Type { get; set; }
            public string Content { get; set; }
        }

        public class EmailLogItem
        {
            public long Id { get; set; }
            public string SenderEmail { get; set; } = "";
            public string Subject { get; set; } = "";
            public string DetectedCommand { get; set; } = "";
            public string ExecutionStatus { get; set; } = "";
            public string ReceivedDate { get; set; } = "";
            public string ResultMessage { get; set; } = "";
        }
    }
}