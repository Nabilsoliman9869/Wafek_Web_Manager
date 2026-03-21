using System;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Text.Json;

namespace Wafek_Web_Manager.Services
{
    public class WorkflowEngineWorker : BackgroundService
    {
        private readonly ILogger<WorkflowEngineWorker> _logger;
        private string _connectionString = "";
        
        // SMTP Settings
        private string _smtpServer = "";
        private int _smtpPort = 587;
        private string _senderEmail = "";
        private string _senderPassword = "";
        private string _approveBaseUrl = "";
        private string _approveUrlOverride = ""; // اختياري: نموذج كامل مثل https://example.com/Approve?ref={logId}

        public WorkflowEngineWorker(ILogger<WorkflowEngineWorker> logger)
        {
            _logger = logger;
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (System.IO.File.Exists(configPath))
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<JsonElement>(json);

                    var server = settings.GetProperty("DbServer").GetString();
                    var db = settings.GetProperty("DbName").GetString();
                    var user = settings.GetProperty("DbUser").GetString();
                    var pass = settings.GetProperty("DbPassword").GetString();
                    _connectionString = $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=30;";

                    if (settings.TryGetProperty("SmtpServer", out var s)) _smtpServer = s.GetString();
                    if (settings.TryGetProperty("SmtpPort", out var p)) _smtpPort = p.GetInt32();
                    if (settings.TryGetProperty("SenderEmail", out var e)) _senderEmail = e.GetString();
                    if (settings.TryGetProperty("SenderPassword", out var sp)) _senderPassword = (sp.GetString() ?? "").Replace(" ", "").Trim();
                    if (settings.TryGetProperty("ApproveBaseUrl", out var url)) _approveBaseUrl = url.GetString() ?? "";
                    if (settings.TryGetProperty("ApproveUrlOverride", out var ov)) _approveUrlOverride = ov.GetString() ?? "";
                }
                // Environment Variables — DB (override or fallback if file missing)
                var envDbServer = Environment.GetEnvironmentVariable("DbServer");
                var envDbName   = Environment.GetEnvironmentVariable("DbName");
                var envDbUser   = Environment.GetEnvironmentVariable("DbUser");
                var envDbPass   = Environment.GetEnvironmentVariable("DbPassword");
                if (!string.IsNullOrWhiteSpace(envDbServer) && !string.IsNullOrWhiteSpace(envDbUser))
                    _connectionString = $"Server={envDbServer};Database={envDbName};User Id={envDbUser};Password={envDbPass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=30;";

                // Environment Variables — SMTP (override or fallback if file missing)
                var envSmtp = Environment.GetEnvironmentVariable("SmtpServer");
                if (!string.IsNullOrWhiteSpace(envSmtp)) _smtpServer = envSmtp.Trim();
                var envPort = Environment.GetEnvironmentVariable("SmtpPort");
                if (!string.IsNullOrWhiteSpace(envPort) && int.TryParse(envPort, out int parsedPort)) _smtpPort = parsedPort;
                var envEmail = Environment.GetEnvironmentVariable("SenderEmail");
                if (!string.IsNullOrWhiteSpace(envEmail)) _senderEmail = envEmail.Trim();
                var envPass2 = Environment.GetEnvironmentVariable("SenderPassword");
                if (!string.IsNullOrWhiteSpace(envPass2)) _senderPassword = envPass2.Replace(" ", "").Trim();

                // Environment Variables — URLs
                var envUrl = Environment.GetEnvironmentVariable("APPROVE_BASE_URL");
                if (!string.IsNullOrWhiteSpace(envUrl)) _approveBaseUrl = envUrl.Trim();
                if (string.IsNullOrEmpty(_approveBaseUrl))
                    _approveBaseUrl = (Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL") ?? "").Trim();
                var envOverride = Environment.GetEnvironmentVariable("APPROVE_URL_OVERRIDE");
                if (!string.IsNullOrWhiteSpace(envOverride)) _approveUrlOverride = envOverride;

                _logger.LogInformation($"Settings loaded — DB: {(string.IsNullOrEmpty(_connectionString) ? "MISSING" : "OK")}, SMTP: {_smtpServer}:{_smtpPort}, Email: {_senderEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load settings");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Workflow Engine Worker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (string.IsNullOrEmpty(_connectionString))
                {
                    LoadSettings(); // Try to reload if failed
                    await Task.Delay(10000, stoppingToken);
                    continue;
                }

                try
                {
                    await ProcessPendingSteps();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing workflow steps");
                }

                await Task.Delay(10000, stoppingToken); // Check every 10 seconds
            }
        }

        private async Task ProcessPendingSteps()
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                // 1. Get Pending Steps (SendEmail) + StepCondition + EmailFormatQuery للتقييم قبل الإرسال
                var sqlWithFormat = @"
                    SELECT TOP 10 L.Id, L.WorkflowDefinitionId, L.SourceRecordId, L.CurrentStepOrder, 
                           S.ActionType, S.SelectedValue, D.SourceTable, S.StepCondition,
                           D.EmailFormatQuery
                    FROM WF_Logs L
                    JOIN WF_Definitions D ON L.WorkflowDefinitionId = D.Id
                    JOIN WF_Steps S ON L.WorkflowDefinitionId = S.WorkflowDefinitionId AND L.CurrentStepOrder = S.StepOrder
                    WHERE L.Status = 'Pending' AND S.ActionType = 'SendEmail'";

                try
                {
                    using (var cmd = new SqlCommand(sqlWithFormat, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long logId = reader.GetInt64(0);
                            Guid sourceId = reader.GetGuid(2);
                            string selectedValue = reader.IsDBNull(5) ? "" : reader.GetString(5);
                            string sourceTable = reader.GetString(6);
                            string stepCondition = reader.IsDBNull(7) ? "" : reader.GetString(7);
                            string? emailFormatQuery = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null;

                            if (!string.IsNullOrWhiteSpace(stepCondition) && !EvaluateStepCondition(conn, sourceTable, sourceId, stepCondition))
                            {
                                AdvanceOrFailStep(logId, reader.GetInt32(1), reader.GetInt32(3));
                                continue;
                            }

                            await SendEmailForStep(logId, sourceId, selectedValue, sourceTable, emailFormatQuery);
                        }
                    }
                }
                catch (Microsoft.Data.SqlClient.SqlException)
                {
                    var sqlWithoutFormat = @"
                    SELECT TOP 10 L.Id, L.WorkflowDefinitionId, L.SourceRecordId, L.CurrentStepOrder,
                           S.ActionType, S.SelectedValue, D.SourceTable, S.StepCondition
                    FROM WF_Logs L
                    JOIN WF_Definitions D ON L.WorkflowDefinitionId = D.Id
                    JOIN WF_Steps S ON L.WorkflowDefinitionId = S.WorkflowDefinitionId AND L.CurrentStepOrder = S.StepOrder
                    WHERE L.Status = 'Pending' AND S.ActionType = 'SendEmail'";
                    using (var cmd = new SqlCommand(sqlWithoutFormat, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            long logId = reader.GetInt64(0);
                            Guid sourceId = reader.GetGuid(2);
                            string selectedValue = reader.IsDBNull(5) ? "" : reader.GetString(5);
                            string sourceTable = reader.GetString(6);
                            string stepCondition = reader.IsDBNull(7) ? "" : reader.GetString(7);

                            if (!string.IsNullOrWhiteSpace(stepCondition) && !EvaluateStepCondition(conn, sourceTable, sourceId, stepCondition))
                            {
                                AdvanceOrFailStep(logId, reader.GetInt32(1), reader.GetInt32(3));
                                continue;
                            }

                            await SendEmailForStep(logId, sourceId, selectedValue, sourceTable, null);
                        }
                    }
                }
            }
        }

        private async Task SendEmailForStep(long logId, Guid sourceId, string recipientRule, string sourceTable, string? emailFormatQuery = null)
        {
            try
            {
                string recipientEmail = ResolveRecipientEmail(recipientRule, sourceId, sourceTable);
                
                if (string.IsNullOrEmpty(recipientEmail))
                {
                    UpdateLogStatus(logId, "Failed", "No recipient email found");
                    return;
                }

                string recipientName = ResolveRecipientName(recipientRule, sourceId, sourceTable);
                bool useArabic = GetRecipientLanguage(recipientRule) == 1;

                var builder = new EmailBodyBuilder(_connectionString);
                var docData = builder.GetDocumentData(sourceId, sourceTable);

                // المستند الكامل داخل الميل — رأس السند + جدول الحسابات (من الاستعلام)
                string? documentBlock = null;
                if (sourceTable == "TBL010")
                {
                    documentBlock = EmailBodyBuilder.BuildBondPrintBlock(docData);
                    var tableBlock = !string.IsNullOrWhiteSpace(emailFormatQuery)
                        ? EmailBodyBuilder.ExecuteFormatQuery(_connectionString, emailFormatQuery, sourceId)
                        : (docData.BondDetails?.Count > 0 ? EmailBodyBuilder.BondDetailsToHtmlTable(docData) : null);
                    if (!string.IsNullOrWhiteSpace(tableBlock))
                        documentBlock = (documentBlock ?? "") + "<div style=\"margin-top:12px\">" + tableBlock + "</div>";
                }
                else if (sourceTable == "TBL022")
                    documentBlock = EmailBodyBuilder.BuildInvoicePrintBlock(docData);
                if (string.IsNullOrWhiteSpace(documentBlock) && !string.IsNullOrWhiteSpace(emailFormatQuery))
                    documentBlock = EmailBodyBuilder.ExecuteFormatQuery(_connectionString, emailFormatQuery, sourceId);

                string approveLink = null;
                if (!string.IsNullOrEmpty(_approveUrlOverride))
                    approveLink = _approveUrlOverride.Replace("{logId}", logId.ToString()).Replace("{id}", logId.ToString());
                else if (!string.IsNullOrEmpty(_approveBaseUrl))
                    approveLink = _approveBaseUrl.TrimEnd('/') + "/ApproveRequest/" + logId;

                var cardLabel = docData.CardName ?? docData.CardNameLatin ?? sourceTable;
                var senderLabel = docData.SenderName ?? docData.SenderNameLatin ?? "";
                string subject = useArabic
                    ? $"طلب الموافقة على {cardLabel}، المرسل {senderLabel}"
                    : $"Request for Approval of {docData.CardNameLatin ?? docData.CardName ?? sourceTable}";
                string body = useArabic
                    ? builder.BuildBodyArabic(docData, recipientName, approveLink, documentBlock)
                    : builder.BuildBodyEnglish(docData, recipientName, approveLink, documentBlock);

                var message = new MimeMessage();
                message.MessageId = $"<wafek-{logId}@wafek>";
                message.From.Add(new MailboxAddress("Wafek", _senderEmail));
                message.To.Add(new MailboxAddress("", recipientEmail));
                message.Subject = subject;

                var bodyBuilder = new MimeKit.BodyBuilder();
                bodyBuilder.HtmlBody = body;
                message.Body = bodyBuilder.ToMessageBody();

                using var client = new MailKit.Net.Smtp.SmtpClient();
                var secureOptions = _smtpPort == 587 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
                client.Connect(_smtpServer, _smtpPort, secureOptions);
                client.Authenticate(_senderEmail, _senderPassword);
                client.Send(message);
                client.Disconnect(true);

                UpdateLogStatus(logId, "WaitingForResponse", $"Email sent to {recipientEmail}");
                _logger.LogInformation($"Email sent successfully to {recipientEmail} for LogId {logId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email for LogId {logId}");
                UpdateLogStatus(logId, "Error", "Email failed: " + ex.Message + " (Inner: " + (ex.InnerException?.Message ?? "None") + ")");
            }
        }

        private string ResolveRecipientEmail(string rule, Guid sourceId, string sourceTable)
        {
            if (string.IsNullOrEmpty(rule)) return null;
            var val = rule.Trim();

            // إيميل مباشر
            if (val.Contains("@")) return val;

            // استخراج Guid من صيغة User:Guid
            if (val.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
                val = val.Substring(5).Trim();

            if (Guid.TryParse(val, out Guid userGuid))
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new SqlCommand(@"
                        SELECT TBL016.EMail 
                        FROM TBL016 
                        JOIN TBL013 ON TBL013.RelatedAgent = TBL016.CardGuide 
                        WHERE TBL013.UsGuide = @uid", conn);
                    cmd.Parameters.AddWithValue("@uid", userGuid);
                    var res = cmd.ExecuteScalar();
                    if (res != null && !string.IsNullOrEmpty(res.ToString()))
                        return res.ToString();
                }
            }

            return null;
        }

        private string ResolveRecipientName(string rule, Guid sourceId, string sourceTable)
        {
            if (string.IsNullOrEmpty(rule)) return "";
            var val = rule.Trim();
            if (val.StartsWith("User:", StringComparison.OrdinalIgnoreCase)) val = val.Substring(5).Trim();
            if (!Guid.TryParse(val, out var userGuid)) return "";
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                var cmd = new SqlCommand("SELECT UserName FROM TBL013 WHERE UsGuide = @uid", conn);
                cmd.Parameters.AddWithValue("@uid", userGuid);
                var r = cmd.ExecuteScalar();
                if (r != null && !string.IsNullOrEmpty(r.ToString())) return r.ToString();
                cmd.CommandText = "SELECT TBL016.AgentName FROM TBL016 JOIN TBL013 ON TBL013.RelatedAgent = TBL016.CardGuide WHERE TBL013.UsGuide = @uid";
                r = cmd.ExecuteScalar();
                return r?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private int GetRecipientLanguage(string rule)
        {
            if (string.IsNullOrEmpty(rule)) return 1;
            var val = rule.Trim();
            if (val.StartsWith("User:", StringComparison.OrdinalIgnoreCase)) val = val.Substring(5).Trim();
            if (!Guid.TryParse(val, out var userGuid)) return 1;
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                var cmd = new SqlCommand("SELECT UserLanguage FROM TBL013 WHERE UsGuide = @uid", conn);
                cmd.Parameters.AddWithValue("@uid", userGuid);
                var r = cmd.ExecuteScalar();
                if (r != null && r != DBNull.Value && int.TryParse(r.ToString(), out var lang)) return lang;
                return 1;
            }
            catch { return 1; }
        }

        /// <summary>
        /// تقييم شرط الخطوة ضد المستند — StepCondition مثل "NetTotal > 5000" أو "BondAmount > 10000"
        /// </summary>
        private bool EvaluateStepCondition(SqlConnection conn, string sourceTable, Guid sourceId, string stepCondition)
        {
            if (string.IsNullOrWhiteSpace(stepCondition)) return true;
            try
            {
                var safeTable = new string(sourceTable.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                if (string.IsNullOrEmpty(safeTable) || safeTable != sourceTable) return false;
                using var cmd = new SqlCommand($"SELECT 1 FROM [{safeTable}] WHERE CardGuide = @id AND ({stepCondition})", conn);
                cmd.Parameters.AddWithValue("@id", sourceId);
                return cmd.ExecuteScalar() != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// عند فشل الشرط: الانتقال للخطوة التالية أو إنهاء الورك فلو
        /// </summary>
        private void AdvanceOrFailStep(long logId, int wfId, int currentStep)
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using (var cmd = new SqlCommand("SELECT MIN(StepOrder) FROM WF_Steps WHERE WorkflowDefinitionId = @wf AND StepOrder > @cur", conn))
                {
                    cmd.Parameters.AddWithValue("@wf", wfId);
                    cmd.Parameters.AddWithValue("@cur", currentStep);
                    var next = cmd.ExecuteScalar();
                    if (next != null && next != DBNull.Value && int.TryParse(next.ToString(), out var nextStep))
                    {
                        using var u = new SqlCommand("UPDATE WF_Logs SET CurrentStepOrder = @s, LastActionLog = N'تخطي خطوة — الشرط لم يتحقق', LastUpdatedDate = GETDATE() WHERE Id = @id", conn);
                        u.Parameters.AddWithValue("@s", nextStep);
                        u.Parameters.AddWithValue("@id", logId);
                        u.ExecuteNonQuery();
                    }
                    else
                    {
                        UpdateLogStatus(logId, "Skipped", "لا توجد خطوة مناسبة — انتهى الورك فلو");
                    }
                }
            }
            catch { }
        }

        private void UpdateLogStatus(long logId, string status, string message)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new SqlCommand("UPDATE WF_Logs SET Status = @s, LastActionLog = @m, LastUpdatedDate = GETDATE() WHERE Id = @id", conn);
                    cmd.Parameters.AddWithValue("@s", status);
                    cmd.Parameters.AddWithValue("@m", message);
                    cmd.Parameters.AddWithValue("@id", logId);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { }
        }
    }
}
