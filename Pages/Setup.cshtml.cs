using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Wafek_Web_Manager.Pages
{
    public class SetupModel : PageModel
    {
        [BindProperty]
        public string DbServer { get; set; } = "xtra.webhop.me,1411";
        [BindProperty]
        public string DbName { get; set; } = "La7_ahmedsalman2026";
        [BindProperty]
        public string DbUser { get; set; } = "LA7";
        [BindProperty]
        public string DbPassword { get; set; } = string.Empty;
        /// <summary>استخدام تشفير SSL للاتصال بقاعدة البيانات. الافتراضي false لتجنب خطأ handshake على بعض السيرفرات.</summary>
        [BindProperty]
        public bool DbEncrypt { get; set; } = false;

        [BindProperty]
        public string SmtpServer { get; set; } = "smtp.gmail.com";
        [BindProperty]
        public int SmtpPort { get; set; } = 587;
        [BindProperty]
        public string SenderEmail { get; set; } = "sirconsultpro@gmail.com";
        [BindProperty]
        public string SenderPassword { get; set; } = string.Empty;
        [BindProperty]
        public bool EnableSsl { get; set; } = true;
        [BindProperty]
        public string TestRecipientEmail { get; set; } = string.Empty;
        [BindProperty]
        public string ApproveBaseUrl { get; set; } = ""; // الرابط العام لـ Wafek_Web_Manager
        [BindProperty]
        public string ApproveUrlOverride { get; set; } = ""; // اختياري: رابط كامل مثل https://app.com/Approve?ref={logId}

        // IMAP — استقبال الميل الوارد
        [BindProperty]
        public bool ImapEnabled { get; set; } = false;
        [BindProperty]
        public string ImapServer { get; set; } = "imap.gmail.com";
        [BindProperty]
        public int ImapPort { get; set; } = 993;

        public string Message { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }

        public void OnGet()
        {
            // 1) Environment Variables (highest priority)
            var envServer = Environment.GetEnvironmentVariable("DbServer");
            var envDb     = Environment.GetEnvironmentVariable("DbName");
            var envUser   = Environment.GetEnvironmentVariable("DbUser");
            var envPass   = Environment.GetEnvironmentVariable("DbPassword");
            if (!string.IsNullOrEmpty(envServer)) DbServer = envServer;
            if (!string.IsNullOrEmpty(envDb))     DbName   = envDb;
            if (!string.IsNullOrEmpty(envUser))   DbUser   = envUser;
            if (!string.IsNullOrEmpty(envPass))   DbPassword = envPass;

            // 2) appsettings.custom.json (only fills remaining empty fields)
            var configPath = ConfigHelper.GetConfigFilePath();
            if (System.IO.File.Exists(configPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);

                    // استرجاع القيم لملء النموذج (لا تستبدل ما جاء من Environment Variables)
                    if (string.IsNullOrEmpty(envServer)) { if (settings.TryGetProperty("DbServer",   out System.Text.Json.JsonElement ds)) DbServer   = ds.GetString() ?? DbServer; }
                    if (string.IsNullOrEmpty(envDb))     { if (settings.TryGetProperty("DbName",     out System.Text.Json.JsonElement dn)) DbName     = dn.GetString() ?? DbName; }
                    if (string.IsNullOrEmpty(envUser))   { if (settings.TryGetProperty("DbUser",     out System.Text.Json.JsonElement du)) DbUser     = du.GetString() ?? DbUser; }
                    if (string.IsNullOrEmpty(envPass))   { if (settings.TryGetProperty("DbPassword", out System.Text.Json.JsonElement dp)) DbPassword = dp.GetString() ?? DbPassword; }

                    // استرجاع إعدادات الإيميل أيضاً
                    if (settings.TryGetProperty("SmtpServer", out System.Text.Json.JsonElement smtp)) SmtpServer = smtp.GetString();
                    if (settings.TryGetProperty("SmtpPort", out System.Text.Json.JsonElement port)) SmtpPort = port.GetInt32();
                    if (settings.TryGetProperty("SenderEmail", out System.Text.Json.JsonElement email)) SenderEmail = email.GetString();
                    if (settings.TryGetProperty("SenderPassword", out System.Text.Json.JsonElement pass)) SenderPassword = pass.GetString();
                    if (settings.TryGetProperty("ApproveBaseUrl", out System.Text.Json.JsonElement url)) ApproveBaseUrl = url.GetString() ?? "";
                    if (settings.TryGetProperty("ApproveUrlOverride", out System.Text.Json.JsonElement ov)) ApproveUrlOverride = ov.GetString() ?? "";
                    if (settings.TryGetProperty("ImapEnabled", out System.Text.Json.JsonElement ie)) ImapEnabled = ie.GetBoolean();
                    if (settings.TryGetProperty("ImapServer", out System.Text.Json.JsonElement im)) ImapServer = im.GetString() ?? "imap.gmail.com";
                    if (settings.TryGetProperty("ImapPort", out System.Text.Json.JsonElement ip)) ImapPort = ip.GetInt32();
                    if (settings.TryGetProperty("DbEncrypt", out System.Text.Json.JsonElement de)) DbEncrypt = de.GetBoolean();
                    if (string.IsNullOrEmpty(ApproveBaseUrl) && HttpContext?.Request != null)
                        ApproveBaseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
                }
                catch { }
            }
            else if (string.IsNullOrEmpty(ApproveBaseUrl) && HttpContext?.Request != null)
                ApproveBaseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        }

        public void OnPostTestConnection()
        {
            var cs = BuildConnectionString();
            try
            {
                using (var cnn = new SqlConnection(cs))
                {
                    cnn.Open();
                    Message = "Database Connection Successful! 🎉 Server: " + cnn.DataSource;
                    IsSuccess = true;
                }
            }
            catch (Exception ex)
            {
                Message = "DB Connection Failed: " + ex.Message;
                IsSuccess = false;
            }
        }

        public void OnPostInitialize()
        {
            var cs = BuildConnectionString();
            string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "Wafek_Workflow_Schema.sql");

            if (!System.IO.File.Exists(scriptPath))
            {
                Message = "Schema file not found: " + scriptPath;
                IsSuccess = false;
                return;
            }

            try
            {
                string script = System.IO.File.ReadAllText(scriptPath);

                using (var cnn = new SqlConnection(cs))
                {
                    cnn.Open();
                    // Execute SQL script intelligently by splitting on 'GO'
                    var batches = script.Split(new[] { "\r\nGO", "\nGO", "\r\nGO\r\n", "\nGO\n", "GO\r\n", "GO\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var batch in batches)
                    {
                        if (string.IsNullOrWhiteSpace(batch)) continue;
                        using var cmd = new SqlCommand(batch, cnn);
                        cmd.ExecuteNonQuery();
                    }
                }
                Message = "System Initialized Successfully! 🚀 Tables created.";
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                Message = "Initialization Failed: " + ex.Message;
                IsSuccess = false;
            }
        }

        public void OnPostSaveSettings()
        {
            // الاحتفاظ بكلمة مرور البريد إن لم تُدخل جديدة (حقل كلمة المرور غالباً فارغ عند الحفظ)
            var passwordToSave = SenderPassword;
            var configPath = ConfigHelper.GetConfigFilePath();
            if (string.IsNullOrWhiteSpace(passwordToSave) && System.IO.File.Exists(configPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var s = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    if (s.TryGetProperty("SenderPassword", out var sp) && sp.ValueKind != System.Text.Json.JsonValueKind.Null)
                        passwordToSave = sp.GetString() ?? "";
                }
                catch { }
            }

            var settings = new { DbServer, DbName, DbUser, DbPassword, DbEncrypt, SmtpServer, SmtpPort, SenderEmail, SenderPassword = passwordToSave ?? "", EnableSsl, ApproveBaseUrl, ApproveUrlOverride, ImapEnabled, ImapServer, ImapPort };
            var jsonOut = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(configPath, jsonOut);

            Message = "تم حفظ الإعدادات بنجاح. يمكنك تغيير قاعدة البيانات أو السيرفر من هنا في أي وقت.";
            IsSuccess = true;
        }

        public void OnPostTestEmail()
        {
            // زر Test Email في نموذج منفصل — نجلب الإعدادات دائماً من الملف
            LoadEmailSettingsFromFile();

            if (string.IsNullOrEmpty(SmtpServer) || string.IsNullOrEmpty(SenderEmail))
            {
                Message = "Please enter SMTP Server and Sender Email first.";
                IsSuccess = false;
                return;
            }

            if (string.IsNullOrEmpty(SenderPassword))
            {
                Message = "Email Password is required. Please enter it and save settings first, then try again.";
                IsSuccess = false;
                return;
            }

            // إزالة المسافات من App Password (Gmail يظهرها بمسافات)
            var password = SenderPassword.Replace(" ", "").Trim();
            if (string.IsNullOrEmpty(password)) password = SenderPassword;

            var recipient = string.IsNullOrWhiteSpace(TestRecipientEmail) ? SenderEmail : TestRecipientEmail.Trim();

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Wafek", SenderEmail));
                message.To.Add(new MailboxAddress("", recipient));
                message.Subject = "Wafek Workflow - ميل اختبار " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                message.Body = new TextPart(MimeKit.Text.TextFormat.Html)
                {
                    Text = @"<h2>نجاح! ✅</h2>
<p>هذا ميل اختبار من نظام وافق (Wafek Workflow Manager).</p>
<p><strong>التوقيت:</strong> " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"</p>
<hr><p style='color:#888;font-size:12px'>إن وصلك هذا الميل، فالإعدادات صحيحة.</p>"
                };

                using var client = new MailKit.Net.Smtp.SmtpClient();
                var secureSocketOptions = EnableSsl && SmtpPort == 587
                    ? SecureSocketOptions.StartTls
                    : (EnableSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None);

                client.Connect(SmtpServer, SmtpPort, secureSocketOptions);
                client.Authenticate(SenderEmail, password);
                client.Send(message);
                client.Disconnect(true);

                Message = $"تم الإرسال إلى {recipient}. تحقق من البريد الوارد والـ Spam.";
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : "";
                Message = "Email Failed: " + ex.Message + innerMsg;
                IsSuccess = false;
            }
        }

        public void OnPostTestImap()
        {
            LoadEmailSettingsFromFile();
            LoadImapSettingsFromFile();

            if (string.IsNullOrEmpty(ImapServer) || string.IsNullOrEmpty(SenderEmail))
            {
                Message = "أدخل خادم IMAP والبريد أولاً.";
                IsSuccess = false;
                return;
            }
            if (string.IsNullOrEmpty(SenderPassword))
            {
                Message = "كلمة مرور البريد غير محفوظة. أدخل كلمة المرور في حقل «Email Password» أعلاه واضغط «Save Settings»، ثم جرّب اختبار IMAP.";
                IsSuccess = false;
                return;
            }

            var password = SenderPassword.Replace(" ", "").Trim();
            try
            {
                using var client = new ImapClient();
                var secure = ImapPort == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                client.Connect(ImapServer, ImapPort, secure);
                client.Authenticate(SenderEmail, password);

                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadOnly);
                var since = DateTime.UtcNow.AddDays(-1);
                var uids = inbox.Search(SearchQuery.DeliveredAfter(since));
                var count = uids.Count;

                client.Disconnect(true);
                Message = $"✓ اتصال IMAP ناجح! وُجد {count} ميل في آخر 24 ساعة. إن لم يُخزَّن: تأكد تفعيل IMAP في Gmail (إعدادات → إعادة توجيه وIMAP).";
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? " | " + ex.InnerException.Message : "";
                Message = "فشل IMAP: " + ex.Message + inner + " — تفعّل IMAP في Gmail واستخدم App Password عند تفعيل التحقق الثنائي.";
                IsSuccess = false;
            }
        }

        private void LoadImapSettingsFromFile()
        {
            var configPath = ConfigHelper.GetConfigFilePath();
            if (!System.IO.File.Exists(configPath)) return;
            try
            {
                var json = System.IO.File.ReadAllText(configPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                if (settings.TryGetProperty("ImapServer", out var im)) ImapServer = im.GetString() ?? "imap.gmail.com";
                if (settings.TryGetProperty("ImapPort", out var ip)) ImapPort = ip.GetInt32();
            }
            catch { }
        }

        /// <summary>
        /// تحميل إعدادات الإيميل من appsettings.custom.json (لزر Test Email)
        /// </summary>
        private void LoadEmailSettingsFromFile()
        {
            var configPath = ConfigHelper.GetConfigFilePath();
            if (!System.IO.File.Exists(configPath)) return;
            try
            {
                var json = System.IO.File.ReadAllText(configPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                if (settings.TryGetProperty("SmtpServer", out var s)) SmtpServer = s.GetString() ?? "";
                if (settings.TryGetProperty("SmtpPort", out var p)) SmtpPort = p.GetInt32();
                if (settings.TryGetProperty("SenderEmail", out var e)) SenderEmail = e.GetString() ?? "";
                if (settings.TryGetProperty("SenderPassword", out var sp)) SenderPassword = sp.GetString() ?? "";
                if (settings.TryGetProperty("EnableSsl", out var ssl)) EnableSsl = ssl.GetBoolean();
                if (settings.TryGetProperty("ApproveBaseUrl", out var url)) ApproveBaseUrl = url.GetString() ?? "";
                if (settings.TryGetProperty("ApproveUrlOverride", out var ov)) ApproveUrlOverride = ov.GetString() ?? "";
            }
            catch { }
        }

        private string BuildConnectionString()
        {
            var server = !string.IsNullOrEmpty(DbServer) ? DbServer : Environment.GetEnvironmentVariable("DbServer");
            var db     = !string.IsNullOrEmpty(DbName)   ? DbName   : Environment.GetEnvironmentVariable("DbName");
            var user   = !string.IsNullOrEmpty(DbUser)   ? DbUser   : Environment.GetEnvironmentVariable("DbUser");
            var pass   = !string.IsNullOrEmpty(DbPassword) ? DbPassword : Environment.GetEnvironmentVariable("DbPassword");
            return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=False;Connect Timeout=30";
        }
    }
}