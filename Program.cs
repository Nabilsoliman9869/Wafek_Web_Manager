using Microsoft.EntityFrameworkCore;
using Wafek_Web_Manager.Data;
using Wafek_Web_Manager.Services;
using Microsoft.AspNetCore.DataProtection;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

static string GetWafekConnectionString(IConfiguration config)
{
    var customPath = Wafek_Web_Manager.ConfigHelper.GetConfigFilePath();
    if (File.Exists(customPath))
    {
        try
        {
            var json = File.ReadAllText(customPath);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var server = root.TryGetProperty("DbServer", out var s) ? s.GetString() : "";
            var db = root.TryGetProperty("DbName", out var d) ? d.GetString() : "";
            var user = root.TryGetProperty("DbUser", out var u) ? u.GetString() : "";
            var pass = root.TryGetProperty("DbPassword", out var p) ? p.GetString() : "";
            if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(user))
                return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt=True;";
        }
        catch { }
    }
    return config.GetConnectionString("WafekDb") ?? "";
}

// Fix for DataProtection Key persistence issue in some environments
var keysFolder = Path.Combine(builder.Environment.ContentRootPath, "keys");
if (!Directory.Exists(keysFolder)) Directory.CreateDirectory(keysFolder);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysFolder))
    .SetApplicationName("WafekWorkflowManager");

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Add Database Context - prefer appsettings.custom.json for DB credentials
var connStr = GetWafekConnectionString(builder.Configuration);
builder.Services.AddDbContext<WafekDbContext>(options =>
    options.UseSqlServer(connStr));

// Register the Workflow Engine Worker (Background Service)
builder.Services.AddHostedService<WorkflowEngineWorker>();

// Response Action Handlers + Executor
builder.Services.AddSingleton<IWorkflowActionHandler, ChangeSecurityHandler>();
builder.Services.AddSingleton<IWorkflowActionHandler, IncrementSecurityHandler>();
builder.Services.AddSingleton<IWorkflowActionHandler, ExecuteProcHandler>();
builder.Services.AddSingleton<IWorkflowActionHandler, ChangeStageHandler>();
builder.Services.AddSingleton<IWorkflowActionHandler, UpdateTableHandler>();
builder.Services.AddSingleton<ResponseActionExecutor>();
builder.Services.AddSingleton<ImapFetchService>();
builder.Services.AddHostedService<InboundEmailCommandWorker>();
builder.Services.AddHostedService<ImapInboundEmailFetcher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
