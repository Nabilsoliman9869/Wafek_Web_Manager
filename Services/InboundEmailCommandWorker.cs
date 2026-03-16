using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// معالج الأوامر السحرية — يستقبل سجلات الميل الوارد، ينفذ الأمر، ويرد على المرسل
    /// التدفق: (IMAP أو إدراج يدوي) → WF_EmailLogs بـ ExecutionStatus='Pending' → هذا الـ Worker ينفذ ويرد
    /// </summary>
    public class InboundEmailCommandWorker : BackgroundService
    {
        private readonly ILogger<InboundEmailCommandWorker> _logger;
        private readonly ResponseActionExecutor? _responseExecutor;
        private string _connectionString = "";
        private string _smtpServer = "", _senderEmail = "", _senderPassword = "";
        private int _smtpPort = 587;

        public InboundEmailCommandWorker(ILogger<InboundEmailCommandWorker> logger, ResponseActionExecutor? responseExecutor = null)
        {
            _logger = logger;
            _responseExecutor = responseExecutor;
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                if (!System.IO.File.Exists("appsettings.custom.json")) return;
                var json = System.IO.File.ReadAllText("appsettings.custom.json");
                var s = JsonSerializer.Deserialize<JsonElement>(json);
                var server = s.GetProperty("DbServer").GetString();
                var db = s.GetProperty("DbName").GetString();
                var user = s.GetProperty("DbUser").GetString();
                var pass = s.GetProperty("DbPassword").GetString();
                _connectionString = $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=True;Connect Timeout=30;";
                if (s.TryGetProperty("SmtpServer", out var ss)) _smtpServer = ss.GetString() ?? "";
                if (s.TryGetProperty("SmtpPort", out var sp)) _smtpPort = sp.GetInt32();
                if (s.TryGetProperty("SenderEmail", out var se)) _senderEmail = se.GetString() ?? "";
                if (s.TryGetProperty("SenderPassword", out var pw)) _senderPassword = (pw.GetString() ?? "").Replace(" ", "").Trim();
            }
            catch { }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Inbound Email Command Worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!string.IsNullOrEmpty(_connectionString))
                {
                    try { await ProcessPendingCommandsAsync(); }
                    catch (Exception ex) { _logger.LogError(ex, "Error processing inbound commands"); }
                }

                await Task.Delay(30000, stoppingToken); // كل 30 ثانية
            }
        }

        private async Task ProcessPendingCommandsAsync()
        {
            LoadSettings();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var sql = @"SELECT TOP 5 Id, SenderEmail, Subject, DetectedCommand, InReplyTo, ApprovalResponse
                FROM WF_EmailLogs 
                WHERE ExecutionStatus = 'Pending'
                ORDER BY ReceivedDate";
            try
            {
                using var testCmd = new SqlCommand("SELECT TOP 1 Id, InReplyTo, ApprovalResponse FROM WF_EmailLogs", conn);
                testCmd.ExecuteScalar();
            }
            catch { sql = @"SELECT TOP 5 Id, SenderEmail, Subject, DetectedCommand FROM WF_EmailLogs WHERE ExecutionStatus = 'Pending' ORDER BY ReceivedDate"; }

            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var rows = new List<(long Id, string SenderEmail, string? Subject, string? Command, string? InReplyTo, string? ApprovalResponse)>();
            while (await reader.ReadAsync())
            {
                var inReplyTo = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetString(4) : null;
                var approvalResp = reader.FieldCount > 5 && !reader.IsDBNull(5) ? reader.GetString(5) : null;
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), inReplyTo, approvalResp));
            }

            foreach (var (id, senderEmail, subject, cmdCode, inReplyTo, approvalResponse) in rows)
            {
                string status;
                string resultMessage;

                if (!string.IsNullOrEmpty(approvalResponse) && !string.IsNullOrEmpty(inReplyTo) && inReplyTo.Contains("wafek-"))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(inReplyTo, @"wafek-(\d+)@");
                    if (m.Success && long.TryParse(m.Groups[1].Value, out var logId))
                    {
                        var (ok, msg) = await ProcessApprovalReplyAsync(logId, approvalResponse);
                        status = ok ? "Success" : "Failed";
                        resultMessage = msg;
                        if (ok && !string.IsNullOrEmpty(senderEmail))
                            await SendReplyAsync(senderEmail, resultMessage, $"رد الموافقة ({approvalResponse})");
                    }
                    else
                    {
                        status = "Failed";
                        resultMessage = "تعذر ربط الرد بطلب الموافقة.";
                    }
                }
                else
                {
                    var code = cmdCode?.Trim();
                    if (string.IsNullOrEmpty(code)) code = ExtractCommandFromText(subject ?? "");

                    if (string.IsNullOrEmpty(code))
                    {
                        status = "NoCommand";
                        resultMessage = "لم يُكتشف كود أمر في الميل.";
                    }
                    else
                    {
                        var (execStatus, result) = await ExecuteCommandAsync(code);
                        status = execStatus;
                        resultMessage = result;
                    }

                    if (status == "Success" && !string.IsNullOrEmpty(senderEmail))
                        await SendReplyAsync(senderEmail, resultMessage ?? "", code ?? "");
                }

                using var uConn = new SqlConnection(_connectionString);
                uConn.Open();
                using var updateCmd = new SqlCommand("UPDATE WF_EmailLogs SET ExecutionStatus = @s, ResultMessage = @m WHERE Id = @id", uConn);
                updateCmd.Parameters.AddWithValue("@s", status);
                updateCmd.Parameters.AddWithValue("@m", resultMessage ?? (object)DBNull.Value);
                updateCmd.Parameters.AddWithValue("@id", id);
                updateCmd.ExecuteNonQuery();
            }
        }

        private async Task<(bool Ok, string Message)> ProcessApprovalReplyAsync(long logId, string responseType)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var check = new SqlCommand("SELECT 1 FROM WF_Logs WHERE Id = @id AND Status = 'WaitingForResponse'", conn);
                check.Parameters.AddWithValue("@id", logId);
                if (await check.ExecuteScalarAsync() == null)
                    return (false, "الطلب غير موجود أو تمت معالجته مسبقاً.");

                if (_responseExecutor != null)
                {
                    await _responseExecutor.ProcessResponseAsync(logId, responseType, _connectionString);
                    return (true, responseType == "Approved" ? "تمت الموافقة." : responseType == "Rejected" ? "تم الرفض." : "تم التأجيل.");
                }

                using var proc = new SqlCommand("EXEC Approve_ProcessResponse @LogId, @ResponseType", conn);
                proc.Parameters.AddWithValue("@LogId", logId);
                proc.Parameters.AddWithValue("@ResponseType", responseType);
                proc.ExecuteNonQuery();
                return (true, responseType == "Approved" ? "تمت الموافقة." : responseType == "Rejected" ? "تم الرفض." : "تم التأجيل.");
            }
            catch (Exception ex)
            {
                return (false, "خطأ: " + ex.Message);
            }
        }

        static string ExtractCommandFromText(string text)
        {
            // يدعم *9009* و *9009
            var m = Regex.Match(text, @"\*(\d+)\*?");
            return m.Success ? "*" + m.Groups[1].Value + "*" : "";
        }

        private async Task<(string Status, string? Result)> ExecuteCommandAsync(string commandCode)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();

                // يدعم *9009* و *9009 — البحث بكلتا الصيغتين
                var codeVar = commandCode.Trim();
                var codeAlt = codeVar.EndsWith("*") ? codeVar.TrimEnd('*') : codeVar + "*";
                using var cmd = new SqlCommand(@"
                    SELECT ActionType, ExecutionContent FROM WF_EmailCommands 
                    WHERE (CommandCode = @c1 OR CommandCode = @c2) AND IsActive = 1", conn);
                cmd.Parameters.AddWithValue("@c1", codeVar);
                cmd.Parameters.AddWithValue("@c2", codeAlt);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return ("NoCommand", $"الأمر {commandCode} غير مسجّل أو معطّل.");

                var actionType = r.GetString(0);
                var content = r.GetString(1);

                if (actionType == "StoredProc" || actionType == "StoredProcedure")
                {
                    using var execConn = new SqlConnection(_connectionString);
                    execConn.Open();
                    using var exec = new SqlCommand(content.Trim(), execConn) { CommandType = System.Data.CommandType.StoredProcedure, CommandTimeout = 120 };
                    using var ad = new Microsoft.Data.SqlClient.SqlDataAdapter(exec);
                    var dt = new System.Data.DataTable();
                    ad.Fill(dt);
                    return ("Success", DataTableToHtml(dt));
                }

                if (actionType == "SQLQuery" || actionType == "SQL")
                {
                    using var execConn = new SqlConnection(_connectionString);
                    execConn.Open();
                    using var exec = new SqlCommand(content.Trim(), execConn) { CommandTimeout = 120 };
                    using var ad = new Microsoft.Data.SqlClient.SqlDataAdapter(exec);
                    var dt = new System.Data.DataTable();
                    ad.Fill(dt);
                    return ("Success", DataTableToHtml(dt));
                }

                return ("Failed", $"نوع الإجراء {actionType} غير مدعوم.");
            }
            catch (Exception ex)
            {
                return ("Failed", "خطأ: " + ex.Message);
            }
        }

        static string DataTableToHtml(System.Data.DataTable dt)
        {
            if (dt.Rows.Count == 0) return "<p>لا توجد بيانات.</p>";
            var sb = new System.Text.StringBuilder("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;width:100%;font-size:13px'><tr style='background:#32b380;color:#fff'>");
            foreach (System.Data.DataColumn c in dt.Columns) sb.Append("<th style='padding:8px'>").Append(System.Net.WebUtility.HtmlEncode(c.ColumnName ?? "")).Append("</th>");
            sb.Append("</tr>");
            var alt = false;
            foreach (System.Data.DataRow row in dt.Rows)
            {
                sb.Append(alt ? "<tr style='background:#f9f9f9'>" : "<tr>");
                foreach (var v in row.ItemArray) sb.Append("<td style='padding:6px;border:1px solid #ddd'>").Append(System.Net.WebUtility.HtmlEncode(v?.ToString() ?? "")).Append("</td>");
                sb.Append("</tr>");
                alt = !alt;
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        private async Task SendReplyAsync(string toEmail, string bodyHtml, string commandCode)
        {
            if (string.IsNullOrEmpty(_smtpServer) || string.IsNullOrEmpty(_senderEmail) || string.IsNullOrEmpty(_senderPassword))
            {
                _logger.LogWarning("لا يمكن إرسال الرد: إعدادات SMTP ناقصة (SmtpServer, SenderEmail, SenderPassword)");
                return;
            }
            try
            {
                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress("Wafek", _senderEmail));
                msg.To.Add(new MailboxAddress("", toEmail));
                msg.Subject = $"رد على الأمر {commandCode}";
                var fullBody = EmailBodyBuilder.BuildCommandReplyBody(commandCode, bodyHtml);
                msg.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = fullBody };

                using var client = new SmtpClient();
                var secure = _smtpPort == 587 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
                await client.ConnectAsync(_smtpServer, _smtpPort, secure);
                await client.AuthenticateAsync(_senderEmail, _senderPassword);
                await client.SendAsync(msg);
                await client.DisconnectAsync(true);
                _logger.LogInformation("تم إرسال الرد إلى {Email}", toEmail);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "فشل إرسال الرد إلى {Email}", toEmail); }
        }
    }
}
