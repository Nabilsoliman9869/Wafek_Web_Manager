using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Wafek_Web_Manager.Data;
using Wafek_Web_Manager.Models;

namespace Wafek_Web_Manager.Pages
{
    public class WorkflowCreateModel : PageModel
    {
        private readonly WafekDbContext _context;

        public WorkflowCreateModel(WafekDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public WorkflowDefinition Workflow { get; set; }

        public List<SelectListItem> BondTypes { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> InvoiceTypes { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> ArchiveTypes { get; set; } = new List<SelectListItem>();

        public void OnGet()
        {
            LoadDocumentTypes();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                LoadDocumentTypes();
                return Page();
            }

            // Auto-generate Name if empty
            if (string.IsNullOrEmpty(Workflow.Name))
            {
                Workflow.Name = $"Workflow-{DateTime.Now.Ticks}";
            }

            _context.WorkflowDefinitions.Add(Workflow);
            await _context.SaveChangesAsync();

            return RedirectToPage("./WorkflowDesigner", new { id = Workflow.Id });
        }

        private void LoadDocumentTypes()
        {
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) conn.Open();

                using (var cmd = conn.CreateCommand())
                {
                    // 1. Bonds (TBL009)
                    cmd.CommandText = "SELECT CardGuide, EntryName FROM TBL009";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            BondTypes.Add(new SelectListItem
                            {
                                Value = reader["CardGuide"].ToString(),
                                Text = reader["EntryName"].ToString()
                            });
                        }
                    }

                    // 2. Invoices (TBL020)
                    cmd.CommandText = "SELECT CardGuide, InvoiceName FROM TBL020";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            InvoiceTypes.Add(new SelectListItem
                            {
                                Value = reader["CardGuide"].ToString(),
                                Text = reader["InvoiceName"].ToString()
                            });
                        }
                    }

                    // 3. Archive (TBL085 - assuming structure based on user input)
                    // Check if table exists first to avoid crash
                    cmd.CommandText = "SELECT count(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'TBL085') AND type in (N'U')";
                    var count = (int)cmd.ExecuteScalar();
                    if (count > 0)
                    {
                        cmd.CommandText = "SELECT CardGuide, CardName FROM TBL085"; // Verify column names if possible
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                ArchiveTypes.Add(new SelectListItem
                                {
                                    Value = reader["CardGuide"].ToString(),
                                    Text = reader["CardName"].ToString()
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error or show message (tables might not exist yet)
                Console.WriteLine("Error loading doc types: " + ex.Message);
            }
        }
    }
}