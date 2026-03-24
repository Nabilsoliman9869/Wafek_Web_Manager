using System;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using System.Collections.Generic;

namespace Wafek_Web_Manager.Services
{
    public class WorkflowEngineWorker : BackgroundService
    {
        private readonly ILogger<WorkflowEngineWorker> _logger;
        private string _connectionString = "";
        
        // SMTP/API Settings
        private string _senderEmail = "";
        private string _brevoApiKey = ""; // New API Key variable
        private string _approveBaseUrl = "";
        private string _approveUrlOverride = ""; // اختياري: نموذج كامل مثل https://example.com/Approve?ref={logId}
        private static readonly HttpClient _httpClient = new HttpClient(); // For Brevo API

        public WorkflowEngineWorker(ILogger<WorkflowEngineWorker> logger)
        {
            _logger = logger;
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                // First try environment variables
                var envServer = Environment.GetEnvironmentVariable("DbServer");
                var envDb = Environment.GetEnvironmentVariable("DbName");
                var envUser = Environment.GetEnvironmentVariable("DbUser");
                var envPass = Environment.GetEnvironmentVariable("DbPassword");
                
                if (!string.IsNullOrEmpty(envServer) && !string.IsNullOrEmpty(envUser))
                {
                    _connectionString = $"Server={envServer};Database={envDb};User Id={envUser};Password={envPass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=30;";
                }
                
                var configPath = ConfigHelper.GetConfigFilePath();
                if (System.IO.File.Exists(configPath))
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<JsonElement>(json);

                    // If not set by env vars, read from file
                    if (string.IsNullOrEmpty(_connectionString))
                    {
                        var server = settings.GetProperty("DbServer").GetString();
                        var db = settings.GetProperty("DbName").GetString();
                        var user = settings.GetProperty("DbUser").GetString();
                        var pass = settings.GetProperty("DbPassword").GetString();
                        _connectionString = $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=30;";
                    }

                    if (settings.TryGetProperty("SenderEmail", out var e)) _senderEmail = e.GetString() ?? "";
                    if (settings.TryGetProperty("BrevoApiKey", out var key)) _brevoApiKey = key.GetString() ?? "";
                    if (settings.TryGetProperty("ApproveBaseUrl", out var url)) _approveBaseUrl = url.GetString() ?? "";
                    if (settings.TryGetProperty("ApproveUrlOverride", out var ov)) _approveUrlOverride = ov.GetString() ?? "";
                }
                
                var envEmail = Environment.GetEnvironmentVariable("SenderEmail");
                if (!string.IsNullOrWhiteSpace(envEmail)) _senderEmail = envEmail.Trim();
                
                // Read Brevo API Key from Environment
                var envApiKey = Environment.GetEnvironmentVariable("BrevoApiKey");
                if (!string.IsNullOrWhiteSpace(envApiKey)) _brevoApiKey = envApiKey.Trim();

                var envUrl = Environment.GetEnvironmentVariable("APPROVE_BASE_URL");
                if (!string.IsNullOrWhiteSpace(envUrl)) _approveBaseUrl = envUrl.Trim();
                if (string.IsNullOrEmpty(_approveBaseUrl))
                    _approveBaseUrl = (Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL") ?? "").Trim();
                var envOverride = Environment.GetEnvironmentVariable("APPROVE_URL_OVERRIDE");
                if (!string.IsNullOrWhiteSpace(envOverride)) _approveUrlOverride = envOverride;
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
                try
                {
                    if (string.IsNullOrEmpty(_connectionString))
                    {
                        LoadSettings();
                        if (string.IsNullOrEmpty(_connectionString))
                        {
                            _logger.LogWarning("Connection string is empty. Waiting for configuration...");
                            await Task.Delay(10000, stoppingToken);
                            continue;
                        }
                    }

                    _logger.LogInformation($"Polling for pending workflow steps... Connection string starts with: {_connectionString.Substring(0, Math.Min(20, _connectionString.Length))}...");
                    await ProcessPendingSteps();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Workflow Engine Worker.");
                }

                await Task.Delay(10000, stoppingToken); // Check every 10 seconds
            }
        }

        private async Task ProcessPendingSteps()
        {
            _logger.LogInformation("Connecting to database to check for Pending steps...");
            
            var pendingLogs = new List<PendingLogDto>();

            // Phase 1: Collect
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    _logger.LogInformation("Database connected successfully.");

                    var sqlWithFormat = @"
                        SELECT TOP 10 L.Id, L.WorkflowDefinitionId, L.SourceRecordId, L.CurrentStepOrder, 
                               S.ActionType, S.SelectedValue, D.SourceTable, S.StepCondition,
                               D.EmailFormatQuery
                        FROM WF_Logs L
                        JOIN WF_Definitions D ON L.WorkflowDefinitionId = D.Id
                        JOIN WF_Steps S ON L.WorkflowDefinitionId = S.WorkflowDefinitionId AND L.CurrentStepOrder = S.StepOrder
                        WHERE L.Status = 'Pending' AND S.ActionType = 'SendEmail'";

                    using (var cmd = new SqlCommand(sqlWithFormat, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            pendingLogs.Add(new PendingLogDto
                            {
                                LogId = reader.GetInt64(0),
                                WorkflowDefinitionId = reader.GetInt32(1),
                                SourceId = reader.GetGuid(2),
                                CurrentStepOrder = reader.GetInt32(3),
                                SelectedValue = reader.IsDBNull(5) ? "" : reader.GetString(5),
                                SourceTable = reader.GetString(6),
                                StepCondition = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                EmailFormatQuery = reader.FieldCount > 8 && !reader.IsDBNull(8) ? reader.GetString(8) : null
                            });
                        }
                    }
                } // SqlConnection and SqlDataReader are completely closed and disposed here
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error collecting pending steps.");
                return;
            }

            if (!pendingLogs.Any())
            {
                _logger.LogInformation("No Pending SendEmail steps found.");
                return;
            }

            // Phase 2: Process
            foreach (var log in pendingLogs)
            {
                _logger.LogInformation($"Processing Pending Step: LogId={log.LogId}, SourceTable={log.SourceTable}, ActionType=SendEmail");

                bool conditionPassed = true;
                if (!string.IsNullOrWhiteSpace(log.StepCondition))
                {
                    using (var conn = new SqlConnection(_connectionString))
                    {
                        await conn.OpenAsync();
                        conditionPassed = EvaluateStepCondition(conn, log.SourceTable, log.SourceId, log.StepCondition);
                    }
                }

                if (!conditionPassed)
                {
                    _logger.LogInformation($"Condition failed for LogId={log.LogId}. Skipping...");
                    AdvanceOrFailStep(log.LogId, log.WorkflowDefinitionId, log.CurrentStepOrder);
                    continue;
                }

                await SendEmailForStep(log.LogId, log.SourceId, log.SelectedValue, log.SourceTable, log.EmailFormatQuery);
            }
        }

        private class PendingLogDto
        {
            public long LogId { get; set; }
            public int WorkflowDefinitionId { get; set; }
            public Guid SourceId { get; set; }
            public int CurrentStepOrder { get; set; }
            public string SelectedValue { get; set; } = "";
            public string SourceTable { get; set; } = "";
            public string StepCondition { get; set; } = "";
            public string? EmailFormatQuery { get; set; }
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

                EmailBodyBuilder.DocumentEmailData docData = new EmailBodyBuilder.DocumentEmailData();
                try
                {
                    // يجب أن نستخدم اتصالاً مستقلاً تماماً لبناء الإيميل لمنع تداخل DataReader
                    using var conn = new SqlConnection(_connectionString);
                    conn.Open();
                    docData = EmailBodyBuilder.GetDocumentData(conn, sourceId, sourceTable);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get document data for Email");
                }

                // المستند الكامل داخل الميل — رأس السند + جدول الحسابات (من الاستعلام)
                string? documentBlock = null;
                if (sourceTable == "TBL010" || sourceTable == "TBL012")
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
                    ? EmailBodyBuilder.BuildBodyArabic(docData, recipientName, approveLink, documentBlock)
                    : EmailBodyBuilder.BuildBodyEnglish(docData, recipientName, approveLink, documentBlock);

                _logger.LogInformation($"Attempting to send email via Brevo HTTP API to {recipientEmail}...");

                var payload = new
                {
                    sender = new { name = "TelleWork", email = _senderEmail },
                    to = new[] { new { email = recipientEmail } },
                    subject = subject,
                    htmlContent = body,
                    headers = new Dictionary<string, string>
                    {
                        { "Message-Id", $"<wafek-{logId}@wafek>" },
                        { "In-Reply-To", $"<wafek-{logId}@wafek>" },
                        { "References", $"<wafek-{logId}@wafek>" }
                    }
                };

                var jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                request.Headers.Add("api-key", _brevoApiKey);
                request.Headers.Add("accept", "application/json");
                request.Content = content;

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    UpdateLogStatus(logId, "WaitingForResponse", $"Email sent to {recipientEmail}");
                    _logger.LogInformation($"Email sent successfully to {recipientEmail} via Brevo API for LogId {logId}");
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Brevo API Error: {response.StatusCode} - {responseBody}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email for LogId {logId}");
                string errorMsg = ex.Message;
                if (ex.InnerException != null) errorMsg += " | Inner: " + ex.InnerException.Message;
                UpdateLogStatus(logId, "Error", "Email failed: " + errorMsg);
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
