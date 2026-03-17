using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;

namespace Wafek_Web_Manager.Pages
{
    public class WorkflowMasterModel : PageModel
    {
        private string GetConnectionString()
        {
            try
            {
                var configPath = ConfigHelper.GetConfigFilePath();
                if (System.IO.File.Exists(configPath))
                {
                    var json = System.IO.File.ReadAllText(configPath);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<dynamic>(json);
                    string server = settings.GetProperty("DbServer").GetString();
                    string db = settings.GetProperty("DbName").GetString();
                    string user = settings.GetProperty("DbUser").GetString();
                    string pass = settings.GetProperty("DbPassword").GetString();
                    var encrypt = settings.TryGetProperty("DbEncrypt", out var enc) ? enc.GetBoolean() : true;
                    return $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Encrypt={encrypt};Connect Timeout=30;";
                }
            }
            catch { }
            return ""; // Fail gracefully
        }

        [BindProperty]
        public string WorkflowName { get; set; } = string.Empty;
        [BindProperty]
        public string Description { get; set; } = string.Empty;
        [BindProperty]
        public string SourceTable { get; set; } = string.Empty;
        [BindProperty]
        public string TriggerEvent { get; set; } = string.Empty;
        [BindProperty]
        public string ConditionSql { get; set; } = string.Empty;
        [BindProperty]
        public string SpecificDocTypeGuid { get; set; } = string.Empty;

        // القوائم (Lists)
        public List<SelectListItem> SourceTables { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> DocumentTypes { get; set; } = new List<SelectListItem>();

        public List<dynamic> ExistingWorkflows { get; set; } = new List<dynamic>();

        public void OnGet()
        {
            LoadSourceTables();
            LoadExistingWorkflows();
        }

        private void LoadExistingWorkflows()
        {
            try
            {
                using (var conn = new SqlConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("SELECT Id, Name, SourceTable, Description FROM WF_Definitions ORDER BY Id DESC", conn))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                ExistingWorkflows.Add(new 
                                { 
                                    Id = r["Id"], 
                                    Name = r["Name"], 
                                    SourceTable = r["SourceTable"],
                                    Description = r["Description"] 
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void LoadSourceTables()
        {
            SourceTables = new List<SelectListItem>();
            try
            {
                using (var conn = new SqlConnection(GetConnectionString()))
                {
                    conn.Open();
                    // جلب جميع الجداول الحقيقية من قاعدة البيانات
                    using (var cmd = new SqlCommand("SELECT name FROM sys.tables ORDER BY name", conn))
                    {
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                string tableName = r["name"].ToString();
                                SourceTables.Add(new SelectListItem { Value = tableName, Text = tableName });
                            }
                        }
                    }
                }
            }
            catch 
            {
                // Fallback is handled in UI now
            }
        }

        public JsonResult OnGetDocTypes(string table)
        {
            var list = new List<object>();
            try
            {
                using (var conn = new SqlConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        // استعلامات ذكية ومخصصة بناءً على طلبك
                        // TBL010: جدول الحركة، لكن الأنواع في TBL009
                        if (table.ToUpper() == "TBL010" || table.ToUpper() == "TBL009")
                        {
                            cmd.CommandText = "SELECT CardGuide, EntryName FROM TBL009";
                        }
                        // TBL021: جدول الحركة، لكن الأنواع في TBL020
                        else if (table.ToUpper() == "TBL021" || table.ToUpper() == "TBL020")
                        {
                            cmd.CommandText = "SELECT CardGuide, InvoiceName FROM TBL020";
                        }
                        else if (table.ToUpper() == "TBL085")
                        {
                            // الاستعلام المعقد للأرشيف
                            cmd.CommandText = "SELECT TBL085.CardGuide, TBL084.CardName FROM TBL085 JOIN TBL084 ON TBL084.CardGuide = TBL085.TypeGuide";
                        }
                        else 
                        {
                            // البحث عن عمود مناسب لباقي الجداول
                            string nameCol = "Name";
                            cmd.CommandText = $"SELECT TOP 1 COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME IN ('Name', 'CardName', 'ArName', 'LatinName', 'Description', 'UserName', 'AgentName')";
                            var res = cmd.ExecuteScalar();
                            if (res != null) nameCol = res.ToString();
                            
                            cmd.CommandText = $"SELECT TOP 100 CardGuide, {nameCol} FROM {table}";
                        }

                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                list.Add(new { value = r[0].ToString(), text = r[1].ToString() });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
            return new JsonResult(list);
        }
        
        public JsonResult OnGetUsers()
        {
            var list = new List<object>();
            try
            {
                using (var conn = new SqlConnection(GetConnectionString()))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT UsGuide, UserName FROM TBL013 WHERE NotActive = 0";
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                list.Add(new { value = "User:" + r["UsGuide"].ToString(), text = "مستخدم: " + r["UserName"].ToString() });
                            }
                        }
                    }
                }
            }
            catch { }
            return new JsonResult(list);
        }
        
        public class WorkflowData
        {
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string SourceTable { get; set; } = string.Empty;
            public string SpecificDocTypeGuid { get; set; } = string.Empty;
            public string TriggerEvent { get; set; } = string.Empty;
            public string ConditionSql { get; set; } = string.Empty;
            public List<StepData> Steps { get; set; } = new List<StepData>();
        }

        public class StepData
        {
            public int StepOrder { get; set; }
            public string StepName { get; set; } = string.Empty;
            public string ActionType { get; set; } = string.Empty;
            public string SelectedValue { get; set; } = string.Empty;
            public string StepCondition { get; set; } = string.Empty;
        }

        public IActionResult OnPostSaveWorkflow([FromBody] WorkflowData data)
        {
            if (data == null) return new JsonResult(new { success = false, error = "No data received" });

            try
            {
                using (var conn = new SqlConnection(GetConnectionString()))
                {
                    conn.Open();
                    
                    // 1. Insert Header
                    var sqlHeader = "INSERT INTO WF_Definitions (Name, Description, SourceTable, SpecificDocTypeGuid, TriggerEvent, ConditionSql) OUTPUT INSERTED.Id VALUES (@n, @d, @s, @g, @t, @c)";
                    int wfId;
                    using (var cmd = new SqlCommand(sqlHeader, conn))
                    {
                        cmd.Parameters.AddWithValue("@n", data.Name ?? "Untitled Workflow");
                        cmd.Parameters.AddWithValue("@d", (object)data.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@s", data.SourceTable ?? "");
                        
                        if (string.IsNullOrEmpty(data.SpecificDocTypeGuid))
                            cmd.Parameters.AddWithValue("@g", DBNull.Value);
                        else
                            cmd.Parameters.AddWithValue("@g", Guid.Parse(data.SpecificDocTypeGuid));

                        cmd.Parameters.AddWithValue("@t", data.TriggerEvent ?? "After Insert");
                        cmd.Parameters.AddWithValue("@c", (object)data.ConditionSql ?? DBNull.Value);
                        
                        wfId = (int)cmd.ExecuteScalar();
                    }

                    // 2. Insert Steps
                    if (data.Steps != null)
                    {
                        foreach (var step in data.Steps)
                        {
                            var sqlStep = "INSERT INTO WF_Steps (WorkflowDefinitionId, StepOrder, StepName, ActionType, SelectedValue, StepCondition) VALUES (@wid, @so, @sn, @at, @sv, @sc)";
                            using (var cmd = new SqlCommand(sqlStep, conn))
                            {
                                cmd.Parameters.AddWithValue("@wid", wfId);
                                cmd.Parameters.AddWithValue("@so", step.StepOrder);
                                cmd.Parameters.AddWithValue("@sn", "Step " + step.StepOrder);
                                cmd.Parameters.AddWithValue("@at", step.ActionType ?? "");
                                cmd.Parameters.AddWithValue("@sv", (object)step.SelectedValue ?? DBNull.Value);
                                cmd.Parameters.AddWithValue("@sc", (object)step.StepCondition ?? DBNull.Value);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }
    }
}