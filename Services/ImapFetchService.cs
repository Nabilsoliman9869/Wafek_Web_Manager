using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Data.SqlClient;
using MimeKit;

namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// خدمة جلب الميل من IMAP وتخزينه في WF_EmailLogs — تُستخدم من الـ Worker والصفحات
    /// </summary>
    public class ImapFetchService
    {
        private string _connectionString = "";
        private string _imapServer = "";
        private int _imapPort = 993;
        private string _senderEmail = "", _senderPassword = "";

        public void LoadSettings()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (!System.IO.File.Exists(configPath)) return;
                var json = System.IO.File.ReadAllText(configPath);
                var s = JsonSerializer.Deserialize<JsonElement>(json);

                var server = s.GetProperty("DbServer").GetString();
                var db = s.GetProperty("DbName").GetString();
                var user = s.GetProperty("DbUser").GetString();
                var pass = s.GetProperty("DbPassword").GetString();
                _connectionString = $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=30;";

                if (s.TryGetProperty("ImapServer", out var im)) _imapServer = im.GetString() ?? "";
                if (s.TryGetProperty("ImapPort", out var ip)) _imapPort = ip.GetInt32();
                if (s.TryGetProperty("SenderEmail", out var se)) _senderEmail = se.GetString() ?? "";
                if (s.TryGetProperty("SenderPassword", out var pw)) _senderPassword = (pw.GetString() ?? "").Replace(" ", "").Trim();
            }
            catch { }
        }

        /// <summary>
        /// جلب يدوي من الصفحة — يتصل ويجلب ويقطع الاتصال
        /// </summary>
        public async Task<(int TotalScanned, int Stored, string? SkipDetail, string? Error)> FetchAndStoreAsync()
        {
            LoadSettings();
            if (string.IsNullOrEmpty(_imapServer) || string.IsNullOrEmpty(_senderEmail) || string.IsNullOrEmpty(_senderPassword) || string.IsNullOrEmpty(_connectionString))
                return (0, 0, null, "إعدادات IMAP ناقصة.");
            try
            {
                using var client = new ImapClient();
                var secure = _imapPort == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                await client.ConnectAsync(_imapServer, _imapPort, secure);
                await client.AuthenticateAsync(_senderEmail, _senderPassword);
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadWrite);
                var result = await DoFetchFromInboxAsync(inbox);
                await client.DisconnectAsync(true);
                return result;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? " | " + ex.InnerException.Message : "";
                return (0, 0, null, ex.Message + inner);
            }
        }

        /// <summary>
        /// يجلب الميول من صندوق مفتوح — يُستخدم من IDLE (بدون إنشاء اتصال جديد)
        /// </summary>
        public async Task<(int TotalScanned, int Stored, string? SkipDetail, string? Error)> FetchAndStoreFromInboxAsync(IMailFolder inbox)
        {
            LoadSettings();
            if (string.IsNullOrEmpty(_connectionString))
                return (0, 0, null, "إعدادات الاتصال ناقصة.");
            try
            {
                return await DoFetchFromInboxAsync(inbox);
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? " | " + ex.InnerException.Message : "";
                return (0, 0, null, ex.Message + inner);
            }
        }

        private async Task<(int TotalScanned, int Stored, string? SkipDetail, string? Error)> DoFetchFromInboxAsync(IMailFolder inbox)
        {
            var all = await inbox.SearchAsync(SearchQuery.All);
            var uids = all.Count > 100 ? all.TakeLast(100).ToArray() : all.ToArray();
            int stored = 0, skipDup = 0, skipNoSender = 0;
            string? firstError = null;
            foreach (var uid in uids)
            {
                try
                {
                    var msg = await inbox.GetMessageAsync(uid);
                    var msgId = msg.MessageId ?? "";
                    var subject = msg.Subject ?? "";
                    var senderEmail = msg.From?.Mailboxes?.FirstOrDefault()?.Address?.Trim() ?? "";
                    if (string.IsNullOrEmpty(senderEmail))
                        senderEmail = ExtractEmailAddress(msg.From?.ToString() ?? "");
                    if (string.IsNullOrEmpty(senderEmail)) { skipNoSender++; continue; }

                    var bodyText = msg.TextBody ?? msg.HtmlBody ?? "";
                    var detectedCommand = ExtractCommandFromText(subject);
                    if (string.IsNullOrEmpty(detectedCommand)) detectedCommand = ExtractCommandFromText(bodyText);
                    var approvalResp = ExtractApprovalResponse(subject + " " + bodyText);
                    var inReplyTo = msg.InReplyTo ?? "";
                    var (inserted, wasDuplicate) = await TryInsertAsync(senderEmail, subject, detectedCommand, msgId, inReplyTo, approvalResp);
                    if (inserted) stored++;
                    else if (wasDuplicate) skipDup++;
                }
                catch (Exception ex)
                {
                    if (firstError == null) firstError = ex.Message + (ex.InnerException != null ? " | " + ex.InnerException.Message : "");
                    skipNoSender++;
                }
            }
            var detail = (skipNoSender > 0 ? $"{skipNoSender} تخطي" : "") + (skipDup > 0 ? (skipNoSender > 0 ? "، " : "") + $"{skipDup} مكرر" : "");
            if (stored == 0 && firstError != null)
                return (uids.Length, 0, detail, "أول خطأ: " + firstError);
            return (uids.Length, stored, string.IsNullOrEmpty(detail) ? null : detail, null);
        }

        private async Task<(bool Inserted, bool WasDuplicate)> TryInsertAsync(string senderEmail, string subject, string detectedCommand, string messageId, string inReplyTo = "", string? approvalResponse = null)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            // فحص التكرار: نفس الميل (ImapMessageId) لا يُخزّن مرتين — يمنع المعالجة المزدوجة
            if (!string.IsNullOrEmpty(messageId))
            {
                try
                {
                    using var checkCmd = new SqlCommand("SELECT 1 FROM WF_EmailLogs WHERE ImapMessageId = @mid", conn);
                    checkCmd.Parameters.AddWithValue("@mid", messageId);
                    if (await checkCmd.ExecuteScalarAsync() != null)
                        return (false, true);
                }
                catch { /* العمود قد لا يوجد */ }
            }

            try
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO WF_EmailLogs (SenderEmail, Subject, DetectedCommand, ExecutionStatus, ImapMessageId, InReplyTo, ApprovalResponse)
                    VALUES (@e, @s, @c, 'Pending', @mid, @irt, @ar)", conn);
                cmd.Parameters.AddWithValue("@e", senderEmail.Trim());
                cmd.Parameters.AddWithValue("@s", subject ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@c", string.IsNullOrEmpty(detectedCommand) ? (object)DBNull.Value : detectedCommand);
                cmd.Parameters.AddWithValue("@mid", string.IsNullOrEmpty(messageId) ? (object)DBNull.Value : messageId);
                cmd.Parameters.AddWithValue("@irt", string.IsNullOrEmpty(inReplyTo) ? (object)DBNull.Value : inReplyTo.Trim());
                cmd.Parameters.AddWithValue("@ar", string.IsNullOrEmpty(approvalResponse) ? (object)DBNull.Value : approvalResponse);
                await cmd.ExecuteNonQueryAsync();
                return (true, false);
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (sqlEx.Number == 207 || sqlEx.Number == 8152)
            {
                await InsertWithoutInReplyToAsync(senderEmail, subject, detectedCommand, messageId);
                return (true, false);
            }
        }

        private async Task InsertWithoutInReplyToAsync(string senderEmail, string subject, string detectedCommand, string messageId)
        {
            using var conn2 = new SqlConnection(_connectionString);
            await conn2.OpenAsync();
            try
            {
                using var cmd = new SqlCommand(@"
                    INSERT INTO WF_EmailLogs (SenderEmail, Subject, DetectedCommand, ExecutionStatus, ImapMessageId)
                    VALUES (@e, @s, @c, 'Pending', @mid)", conn2);
                cmd.Parameters.AddWithValue("@e", senderEmail.Trim());
                cmd.Parameters.AddWithValue("@s", subject ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@c", string.IsNullOrEmpty(detectedCommand) ? (object)DBNull.Value : detectedCommand);
                cmd.Parameters.AddWithValue("@mid", string.IsNullOrEmpty(messageId) ? (object)DBNull.Value : messageId);
                await cmd.ExecuteNonQueryAsync();
            }
            catch
            {
                using var cmd = new SqlCommand(@"INSERT INTO WF_EmailLogs (SenderEmail, Subject, DetectedCommand, ExecutionStatus) VALUES (@e, @s, @c, 'Pending')", conn2);
                cmd.Parameters.AddWithValue("@e", senderEmail.Trim());
                cmd.Parameters.AddWithValue("@s", subject ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@c", string.IsNullOrEmpty(detectedCommand) ? (object)DBNull.Value : detectedCommand);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        static string ExtractEmailAddress(string fromHeader)
        {
            if (string.IsNullOrWhiteSpace(fromHeader)) return "";
            var m = Regex.Match(fromHeader, @"[\w\.\-\+]+@[\w\.\-]+\.\w+");
            return m.Success ? m.Value.Trim() : "";
        }

        static string ExtractCommandFromText(string text)
        {
            var m = Regex.Match(text, @"\*(\d+)\*?");
            return m.Success ? "*" + m.Groups[1].Value + "*" : "";
        }

        /// <summary>استخراج رد الموافقة من نص الميل — #1#/موافق، #2#/مرفوض، #3#/يؤجل</summary>
        static string? ExtractApprovalResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            var tl = t.ToLowerInvariant();
            // الصيغة الصريحة #1# #2# #3# (أولوية)
            if (tl.Contains("#1#")) return "Approved";
            if (tl.Contains("#2#")) return "Rejected";
            if (tl.Contains("#3#")) return "Postponed";
            // القيم الرقمية والنصية
            if (Regex.IsMatch(t, @"\b1\b") || tl.Contains("موافق") || tl.Contains("approved") || tl.Contains("*#approve")) return "Approved";
            if (Regex.IsMatch(t, @"\b2\b") || tl.Contains("رفض") || tl.Contains("غير موافق") || tl.Contains("مرفوض") || tl.Contains("rejected") || tl.Contains("*#reject")) return "Rejected";
            if (Regex.IsMatch(t, @"\b3\b") || tl.Contains("يؤجل") || tl.Contains("postponed") || tl.Contains("*#postpone")) return "Postponed";
            return null;
        }
    }
}
