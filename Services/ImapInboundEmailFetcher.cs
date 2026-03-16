using System.Text.Json;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// يراقب صندوق البريد عبر IMAP IDLE — عند وصول ميل جديد يُخزّنه مباشرة في WF_EmailLogs.
    /// </summary>
    public class ImapInboundEmailFetcher : BackgroundService
    {
        private readonly ILogger<ImapInboundEmailFetcher> _logger;
        private readonly ImapFetchService _fetchService;

        public ImapInboundEmailFetcher(ILogger<ImapInboundEmailFetcher> logger, ImapFetchService fetchService)
        {
            _logger = logger;
            _fetchService = fetchService;
        }

        private bool IsImapEnabled()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (!System.IO.File.Exists(configPath)) return false;
                var json = System.IO.File.ReadAllText(configPath);
                var s = JsonSerializer.Deserialize<JsonElement>(json);
                return s.TryGetProperty("ImapEnabled", out var ie) && ie.GetBoolean();
            }
            catch { return false; }
        }

        private (string? Host, int Port, SecureSocketOptions Secure, string? User, string? Pass) LoadImapSettings()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (!System.IO.File.Exists(configPath)) return (null, 993, SecureSocketOptions.SslOnConnect, null, null);
                var json = System.IO.File.ReadAllText(configPath);
                var s = JsonSerializer.Deserialize<JsonElement>(json);
                var host = s.TryGetProperty("ImapServer", out var h) ? h.GetString() : null;
                var port = s.TryGetProperty("ImapPort", out var p) ? p.GetInt32() : 993;
                var user = s.TryGetProperty("SenderEmail", out var u) ? u.GetString() : null;
                var pass = s.TryGetProperty("SenderPassword", out var pw) ? (pw.GetString() ?? "").Replace(" ", "").Trim() : null;
                var secure = port == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                return (host, port, secure, user, pass);
            }
            catch { return (null, 993, SecureSocketOptions.SslOnConnect, null, null); }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Imap Inbound Email Fetcher (IDLE) started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (!IsImapEnabled())
                {
                    await Task.Delay(30000, stoppingToken);
                    continue;
                }

                var (host, port, secure, user, pass) = LoadImapSettings();
                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
                {
                    _logger.LogWarning("IMAP settings incomplete; retrying in 60s.");
                    await Task.Delay(60000, stoppingToken);
                    continue;
                }

                try
                {
                    using var client = new ImapClient();
                    await client.ConnectAsync(host, port, secure, stoppingToken);
                    await client.AuthenticateAsync(user, pass, stoppingToken);
                    var inbox = client.Inbox;
                    await inbox.OpenAsync(MailKit.FolderAccess.ReadWrite, stoppingToken);

                    var inboxRef = inbox;
                    CancellationTokenSource? doneCts = null;
                    var messagesArrived = false;

                    inbox.CountChanged += (_, _) =>
                    {
                        messagesArrived = true;
                        try { doneCts?.Cancel(); } catch { }
                    };

                    var (tot, st, _, initErr) = await _fetchService.FetchAndStoreFromInboxAsync(inbox);
                    if (initErr != null)
                        _logger.LogWarning("IMAP initial fetch: {Err}", initErr);
                    else
                        _logger.LogInformation("IMAP IDLE: initial fetch done, stored {Stored}/{Total}", st, tot);

                    while (!stoppingToken.IsCancellationRequested && client.IsConnected)
                    {
                        try
                        {
                            messagesArrived = false;
                            doneCts?.Dispose();
                            doneCts = new CancellationTokenSource(TimeSpan.FromMinutes(9));
                            if (client.Capabilities.HasFlag(MailKit.Net.Imap.ImapCapabilities.Idle))
                                await client.IdleAsync(doneCts.Token, stoppingToken);
                            else
                                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                        catch (OperationCanceledException) when (messagesArrived) { }

                        if (messagesArrived || !client.Capabilities.HasFlag(MailKit.Net.Imap.ImapCapabilities.Idle))
                        {
                            try
                            {
                                var (total, stored, _, err) = await _fetchService.FetchAndStoreFromInboxAsync(inboxRef);
                                if (err != null)
                                    _logger.LogWarning("IMAP fetch: {Err}", err);
                                else if (stored > 0)
                                    _logger.LogInformation("IMAP: stored {Stored} of {Total} new emails", stored, total);
                            }
                            catch (Exception ex) { _logger.LogError(ex, "IMAP fetch after IDLE"); }
                        }
                    }

                    try { await client.DisconnectAsync(true, stoppingToken); } catch { }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IMAP IDLE error; reconnecting in 60s.");
                    await Task.Delay(60000, stoppingToken);
                }
            }
        }
    }
}
