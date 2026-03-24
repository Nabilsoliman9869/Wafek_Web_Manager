using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Data.SqlClient;

namespace Wafek_Web_Manager.Services
{
    /// <summary>
    /// بناء جسم الميل كما في الكود الأصلي وافق (Approve_CreateFirstProcess)
    /// يدعم: TBL010 سند، TBL022 فاتورة، TBL085 أرشيف، TBL014 إجازة
    /// </summary>
    public class EmailBodyBuilder
    {
        private readonly string _connectionString;
        private const string ThemeColor = "#32b380";

        /// <summary>
        /// مسار اللوجو
        /// </summary>
        public static string GetLogoPath()
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "TELLEWORK.jpg");
        }
        /// <summary>
        /// الحصول على اللوجو كـ base64 — يدعم jpg/png — واضح في Gmail
        /// عند فشل القراءة يُستخدم رابط CDN احتياطي
        /// </summary>
        public static string GetLogoAsBase64DataUrl()
        {
            var dirs = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "images"),
                Path.Combine(Directory.GetCurrentDirectory(), "images"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "wwwroot", "images")),
            };
            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var name in new[] { "TELLEWORK.jpg", "TELLEWORK.png", "logo.jpg", "logo.png" })
                {
                    var path = Path.Combine(dir, name);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        var bytes = File.ReadAllBytes(path);
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        var mime = ext == ".png" ? "image/png" : "image/jpeg";
                        return $"data:{mime};base64," + Convert.ToBase64String(bytes);
                    }
                    catch { }
                }
            }
            return "";
        }

        /// <summary>رابط احتياطي للوجو عند فشل base64 (مثلاً على Render)</summary>
        public static string GetLogoFallbackUrl() => "https://raw.githubusercontent.com/Nabilsoliman9869/Wafek_Web_Manager/main/wwwroot/images/TELLEWORK.jpg";

        public EmailBodyBuilder(string connectionString)
        {
            _connectionString = connectionString ?? "";
        }

        /// <summary>
        /// جلب بيانات المستند حسب الجدول المصدر
        /// </summary>
        public static DocumentEmailData GetDocumentData(SqlConnection conn, Guid sourceId, string sourceTable)
        {
            var data = new DocumentEmailData { SourceTable = sourceTable, SourceId = sourceId };
            if (conn == null || conn.State != System.Data.ConnectionState.Open) return data;

            try
            {

                var table = (sourceTable ?? "").ToUpperInvariant();

                if (table == "TBL010") // سند
                {
                    try
                    {
                        var cmd = new SqlCommand(@"
                            SELECT t.BondNumber, t.MainGuide, t.Notes,
                                   (SELECT EntryName FROM TBL009 WHERE CardGuide = t.MainGuide) AS EntryName,
                                   (SELECT EntryLatinName FROM TBL009 WHERE CardGuide = t.MainGuide) AS EntryLatinName,
                                   u.UserName AS SenderName, a.AgentName AS SenderAgentName, a.LatinName AS SenderAgentLatin
                            FROM TBL010 t
                            LEFT JOIN TBL013 u ON u.UsGuide = t.ByUser
                            LEFT JOIN TBL016 a ON a.CardGuide = t.AgentGuide
                            WHERE t.CardGuide = @id", conn);
                        cmd.Parameters.AddWithValue("@id", sourceId);
                        using var r = cmd.ExecuteReader();
                        if (r.Read())
                        {
                            data.CardNumber = GetColVal(r, "BondNumber") ?? GetColVal(r, "BondNo") ?? "";
                            data.CardName = GetColVal(r, "EntryName") ?? "سند";
                            data.CardNameLatin = GetColVal(r, "EntryLatinName") ?? "";
                            data.Notes = GetColVal(r, "Notes") ?? "";
                            data.SenderName = GetColVal(r, "SenderName") ?? GetColVal(r, "SenderAgentName") ?? "";
                            data.SenderNameLatin = GetColVal(r, "SenderAgentLatin") ?? "";
                        }
                        else
                        {
                            LoadTbl010Fallback(conn, sourceId, data);
                        }
                        LoadTbl010ExtendedFromXml(conn, sourceId, data);
                        // احتياطي: الحساب من أول سطر تفاصيل إن كان الرأس فارغاً (سند 10+38)
                        if (string.IsNullOrEmpty(data.AccountName) && data.BondDetails?.Count > 0)
                            data.AccountName = data.BondDetails[0].AccountName ?? "";
                    }
                    catch
                    {
                        LoadTbl010Fallback(conn, sourceId, data);
                    }
                }
                else if (table == "TBL022") // فاتورة
                {
                    var cmd = new SqlCommand(@"
                        SELECT t.BillNumber, t.MainGuide, t.Notes, t.InDate,
                               t.NmbNetTotal, t.NetTotal,
                               (SELECT InvoiceName FROM TBL020 WHERE CardGuide = t.MainGuide) AS EntryName,
                               (SELECT LatinName FROM TBL020 WHERE CardGuide = t.MainGuide) AS EntryLatinName,
                               u.UserName AS SenderName, a.AgentName AS CustomerName
                        FROM TBL022 t
                        LEFT JOIN TBL013 u ON u.UsGuide = t.ByUser
                        LEFT JOIN TBL016 a ON a.CardGuide = t.AgentGuide
                        WHERE t.CardGuide = @id", conn);
                    cmd.Parameters.AddWithValue("@id", sourceId);
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        data.CardNumber = r["BillNumber"]?.ToString() ?? "";
                        data.CardName = r["EntryName"]?.ToString() ?? "";
                        data.CardNameLatin = r["EntryLatinName"]?.ToString() ?? "";
                        data.Notes = r["Notes"]?.ToString() ?? "";
                        data.SenderName = r["SenderName"]?.ToString() ?? "";
                        if (TryGetDateTime(r, "InDate", out var dt)) data.BondDate = dt;
                        data.AccountName = r["CustomerName"]?.ToString() ?? "";
                        var nt = GetDecimal(r, "NmbNetTotal");
                        if (nt == 0) nt = GetDecimal(r, "NetTotal");
                        if (nt != 0) data.TotalAmount = nt.ToString("#,##0.00");
                    }
                    try
                    {
                        var detCmd = new SqlCommand(@"
SELECT d.Quantity, d.UnitPrice, d.TotalValue,
       ISNULL(p.ItemName, p.LatinName) AS ItemName
FROM TBL023 d
LEFT JOIN TBL007 p ON p.CardGuide = d.ProductGuide
WHERE d.MainGuide = @id", conn);
                        detCmd.Parameters.AddWithValue("@id", sourceId);
                        using var rd = detCmd.ExecuteReader();
                        while (rd.Read())
                        {
                            data.InvoiceDetails.Add(new InvoiceDetailRow
                            {
                                ItemName = GetColVal(rd, "ItemName") ?? "",
                                Quantity = GetDecimal(rd, "Quantity"),
                                UnitPrice = GetDecimal(rd, "UnitPrice"),
                                TotalValue = GetDecimal(rd, "TotalValue")
                            });
                        }
                    }
                    catch { }
                }
                else if (table == "TBL085") // أرشيف
                {
                    var cmd = new SqlCommand(@"
                        SELECT t.CardNumber, t.TypeGuide,
                               (SELECT CardName FROM TBL084 WHERE CardGuide = t.TypeGuide) AS EntryName,
                               (SELECT LatinName FROM TBL084 WHERE CardGuide = t.TypeGuide) AS EntryLatinName
                        FROM TBL085 t WHERE t.CardGuide = @id", conn);
                    cmd.Parameters.AddWithValue("@id", sourceId);
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        data.CardNumber = r["CardNumber"]?.ToString() ?? "";
                        data.CardName = r["EntryName"]?.ToString() ?? "";
                        data.CardNameLatin = r["EntryLatinName"]?.ToString() ?? "";
                    }
                }
                else if (table == "TBL014") // إجازة
                {
                    var cmd = new SqlCommand(@"
                        SELECT t.IntValue01 AS CardNumber, t.Notes,
                               (SELECT CardName FROM TBL014 WHERE CardGuide = t.RelatedCard) AS EntryName,
                               (SELECT LatinName FROM TBL014 WHERE CardGuide = t.RelatedCard) AS EntryLatinName,
                               u.UserName AS SenderName, a.AgentName AS SenderAgentName, a.LatinName AS SenderAgentLatin
                        FROM TBL014 t
                        LEFT JOIN TBL013 u ON u.UsGuide = t.ByUser
                        LEFT JOIN TBL016 a ON a.CardGuide = t.AgentGuide
                        WHERE t.CardGuide = @id", conn);
                    cmd.Parameters.AddWithValue("@id", sourceId);
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        data.CardNumber = r["CardNumber"]?.ToString() ?? "";
                        data.CardName = r["EntryName"]?.ToString() ?? "";
                        data.CardNameLatin = r["EntryLatinName"]?.ToString() ?? "";
                        data.Notes = r["Notes"]?.ToString() ?? "";
                        data.SenderName = r["SenderName"]?.ToString() ?? r["SenderAgentName"]?.ToString() ?? "";
                        data.SenderNameLatin = r["SenderAgentLatin"]?.ToString() ?? "";
                    }
                }
                else
                {
                    data.CardNumber = sourceId.ToString("N")[..8];
                    data.CardName = sourceTable;
                }

                data.SendDate = DateTime.Now;
                data.CompanyName = GetCompanyName(conn);
                data.CompanyPhone = GetCompanyPhone(conn);
            }
            catch { }
            return data;
        }

        static string? GetColVal(SqlDataReader r, string col)
        {
            try
            {
                var i = r.GetOrdinal(col);
                var v = r.GetValue(i);
                return v == null || v == DBNull.Value ? null : v.ToString()?.Trim();
            }
            catch { return null; }
        }

        static bool TryGetDateTime(SqlDataReader r, string col, out DateTime dt)
        {
            dt = default;
            try
            {
                var i = r.GetOrdinal(col);
                if (r.IsDBNull(i)) return false;
                var v = r.GetValue(i);
                if (v is DateTime d) { dt = d; return true; }
                if (DateTime.TryParse(v?.ToString(), out var parsed)) { dt = parsed; return true; }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// إثراء بيانات السند — الفرع، مركز الكلفة، المشروع، العملة، الحساب، المجموع
        /// تفاصيل السند من TBL038 (MainGuide = TBL010.CardGuide)
        /// </summary>
        static void LoadTbl010ExtendedFromXml(SqlConnection conn, Guid sourceId, DocumentEmailData data)
        {
            try
            {
                var cmd = new SqlCommand(@"
SELECT TOP 1
    BronchName AS BranchName,
    CostCenter AS CostCenterName,
    ProjectName,
    AccountName
FROM QryApproveBondDetails
WHERE MainGuide = @id", conn);
                cmd.Parameters.AddWithValue("@id", sourceId);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    data.BranchName = GetColVal(r, "BranchName") ?? "";
                    data.CostCenterName = GetColVal(r, "CostCenterName") ?? "";
                    data.ProjectName = GetColVal(r, "ProjectName") ?? "";
                    data.AccountName = GetColVal(r, "AccountName") ?? "";
                }
                else
                {
                    // Fallback
                    r.Close();
                    var fcmd = new SqlCommand(@"
SELECT TOP 1
    br.BronchName AS BranchName,
    cc.CostCenter AS CostCenterName,
    p.ProjectName,
    cur.CurrencyName,
    acc.AccountName
FROM TBL010 h
LEFT JOIN TBL050 br ON br.CardGuide = h.Branch
LEFT JOIN TBL005 cc ON cc.CardGuide = h.CostCenter
LEFT JOIN TBL049 p ON p.CardGuide = h.Project
LEFT JOIN TBL001 cur ON cur.CardGuide = h.CurrencyGuide
LEFT JOIN TBL004 acc ON acc.CardGuide = h.AccountGuide
WHERE h.CardGuide = @id", conn);
                    fcmd.Parameters.AddWithValue("@id", sourceId);
                    using var r2 = fcmd.ExecuteReader();
                    if (r2.Read())
                    {
                        data.BranchName = GetColVal(r2, "BranchName") ?? "";
                        data.CostCenterName = GetColVal(r2, "CostCenterName") ?? "";
                        data.ProjectName = GetColVal(r2, "ProjectName") ?? "";
                        data.CurrencyName = GetColVal(r2, "CurrencyName") ?? "";
                        data.AccountName = GetColVal(r2, "AccountName") ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                // data.AccountName = "ERR: " + ex.Message;
            }

            try
            {
                var dtCmd = new SqlCommand("SELECT TOP 1 COALESCE(BondDate, InDate, DoneIn) FROM TBL010 WHERE CardGuide = @id", conn);
                dtCmd.Parameters.AddWithValue("@id", sourceId);
                var v = dtCmd.ExecuteScalar();
                if (v != null && v != DBNull.Value && DateTime.TryParse(v.ToString(), out var dt))
                    data.BondDate = dt;
            }
            catch { }
            if (data.BondDate == default)
            {
                try
                {
                    var dtCmd2 = new SqlCommand("SELECT TOP 1 InDate FROM TBL010 WHERE CardGuide = @id", conn);
                    dtCmd2.Parameters.AddWithValue("@id", sourceId);
                    var v2 = dtCmd2.ExecuteScalar();
                    if (v2 != null && v2 != DBNull.Value && DateTime.TryParse(v2.ToString(), out var dt2))
                        data.BondDate = dt2;
                }
                catch { }
            }

            try
            {
                var sumCmd = new SqlCommand("SELECT ISNULL(SUM(ISNULL(DebitRate,0)+ISNULL(CreditRate,0)),0) FROM TBL038 WHERE MainGuide = @id", conn);
                sumCmd.Parameters.AddWithValue("@id", sourceId);
                var tot = sumCmd.ExecuteScalar();
                if (tot != null && tot != DBNull.Value && decimal.TryParse(tot.ToString(), out var amt) && amt != 0)
                    data.TotalAmount = amt.ToString("N2");
                if (string.IsNullOrEmpty(data.TotalAmount))
                {
                    sumCmd = new SqlCommand("SELECT TOP 1 Value FROM TBL010 WHERE CardGuide = @id", conn);
                    sumCmd.Parameters.AddWithValue("@id", sourceId);
                    tot = sumCmd.ExecuteScalar();
                    if (tot != null && tot != DBNull.Value && decimal.TryParse(tot.ToString(), out amt) && amt != 0)
                        data.TotalAmount = amt.ToString("N2");
                }
            }
            catch { }

            // 1) TBL038 — المصدر الرئيسي: 10=رأس، 38=تفاصيل (MainGuide = TBL010.CardGuide)
            string errorLog = "";
            try
            {
                var detCmd = new SqlCommand(@"
SELECT '' AS AccountCode,
       AccountName AS [الحساب],
       Debit AS [مدين],
       Credit AS [دائن],
       Notes AS [البيان],
       CostCenter AS [مركز الكلفة]
FROM QryApproveBondDetails
WHERE MainGuide = @id", conn);
                detCmd.Parameters.AddWithValue("@id", sourceId);
                using var rd = detCmd.ExecuteReader();
                decimal sum = 0;
                while (rd.Read())
                {
                    var debit = GetDecimal(rd, "مدين");
                    var credit = GetDecimal(rd, "دائن");
                    sum += debit + credit;
                    data.BondDetails.Add(new BondDetailRow
                    {
                        AccountCode = GetColVal(rd, "AccountCode") ?? "",
                        AccountName = GetColVal(rd, "الحساب") ?? "",
                        Debit = debit,
                        Credit = credit,
                        Notes = GetColVal(rd, "البيان") ?? "",
                        CostCenterName = GetColVal(rd, "مركز الكلفة") ?? ""
                    });
                }
                if (data.BondDetails.Count > 0 && string.IsNullOrEmpty(data.TotalAmount) && sum != 0)
                    data.TotalAmount = sum.ToString("N2");
            }
            catch (Exception ex) { /* errorLog += "QryErr: " + ex.Message + " | "; */ }

            // إذا كان QryApproveBondDetails فارغاً، نحاول TBL038
            if (data.BondDetails.Count == 0)
            {
                try
                {
                    var detCmd = new SqlCommand(@"
SELECT '' AS AccountCode,
       acc.AccountName AS [الحساب],
       ISNULL(d.Debit,0) AS [مدين],
       ISNULL(d.Credit,0) AS [دائن],
       d.Notes AS [البيان],
       cc.CostCenter AS [مركز الكلفة]
FROM TBL038 d
LEFT JOIN TBL004 acc ON d.AccountGuide = acc.CardGuide
LEFT JOIN TBL005 cc ON d.CostCenter = cc.CardGuide
WHERE d.MainGuide = @id", conn);
                    detCmd.Parameters.AddWithValue("@id", sourceId);
                    using var rd = detCmd.ExecuteReader();
                    decimal sum = 0;
                    while (rd.Read())
                    {
                        var debit = GetDecimal(rd, "مدين");
                        var credit = GetDecimal(rd, "دائن");
                        sum += debit + credit;
                        data.BondDetails.Add(new BondDetailRow
                        {
                            AccountCode = "",
                            AccountName = GetColVal(rd, "الحساب") ?? "",
                            Debit = debit,
                            Credit = credit,
                            Notes = GetColVal(rd, "البيان") ?? "",
                            CostCenterName = GetColVal(rd, "مركز الكلفة") ?? ""
                        });
                    }
                    if (data.BondDetails.Count > 0 && string.IsNullOrEmpty(data.TotalAmount) && sum != 0)
                        data.TotalAmount = sum.ToString("N2");
                }
                catch (Exception ex) { /* errorLog += "FallbackErr: " + ex.Message; */ }
            }
            
            if (!string.IsNullOrEmpty(errorLog) && data.BondDetails.Count == 0)
            {
                // إزالة رسالة الخطأ من العرض للمستخدم النهائي بعد نجاح التطوير
                // data.BondDetails.Add(new BondDetailRow { AccountName = "ERROR", Notes = errorLog });
            }

            // 2) احتياطي: TBL011/TBL012 — عندما تستخدم المنظومة هذا الهيكل بدل TBL038
            if (data.BondDetails.Count == 0)
            {
                try
                {
                    var altCmd = new SqlCommand(@"
SELECT acc.AccountName,
    ISNULL(d.Debit,0)+ISNULL(d.DebitRate,0) AS DebitRate,
    ISNULL(d.Credit,0)+ISNULL(d.CreditRate,0) AS CreditRate,
    ISNULL(d.Description,d.Notes) AS Notes
FROM TBL011 h
INNER JOIN TBL012 d ON h.CardGuide = d.MainGuide
LEFT JOIN TBL004 acc ON acc.CardGuide = d.AccountGuide
WHERE h.BondGuide = @id", conn);
                    altCmd.Parameters.AddWithValue("@id", sourceId);
                    using var altRd = altCmd.ExecuteReader();
                    decimal sum = 0;
                    while (altRd.Read())
                    {
                        var debit = GetDecimal(altRd, "DebitRate");
                        var credit = GetDecimal(altRd, "CreditRate");
                        sum += debit + credit;
                        data.BondDetails.Add(new BondDetailRow
                        {
                            AccountCode = "",
                            AccountName = GetColVal(altRd, "AccountName") ?? "",
                            Debit = debit,
                            Credit = credit,
                            Notes = GetColVal(altRd, "Notes") ?? "",
                            CostCenterName = ""
                        });
                    }
                    if (data.BondDetails.Count > 0 && string.IsNullOrEmpty(data.TotalAmount) && sum != 0)
                        data.TotalAmount = sum.ToString("N2");
                }
                catch { }
            }

            // احتياطي نهائي: سند بسيط — من الرأس فقط (حساب واحد + مجموع)
            if (data.BondDetails.Count == 0 && !string.IsNullOrEmpty(data.AccountName) && !string.IsNullOrEmpty(data.TotalAmount) && decimal.TryParse(data.TotalAmount.Replace(",", ""), System.Globalization.NumberStyles.Any, null, out var simpleAmt) && simpleAmt != 0)
            {
                data.BondDetails.Add(new BondDetailRow
                {
                    AccountCode = "",
                    AccountName = data.AccountName,
                    Debit = 0,
                    Credit = simpleAmt,
                    Notes = data.Notes ?? "",
                    CostCenterName = data.CostCenterName ?? ""
                });
            }
        }

        static decimal GetDecimal(SqlDataReader r, string col)
        {
            try
            {
                var i = r.GetOrdinal(col);
                if (r.IsDBNull(i)) return 0;
                var v = r.GetValue(i);
                return Convert.ToDecimal(v);
            }
            catch { return 0; }
        }

        static decimal GetDecimalByOrdinal(SqlDataReader r, int i)
        {
            try
            {
                if (r.IsDBNull(i)) return 0;
                var v = r.GetValue(i);
                return Convert.ToDecimal(v);
            }
            catch { return 0; }
        }

        static void LoadTbl010Fallback(SqlConnection conn, Guid sourceId, DocumentEmailData data)
        {
            foreach (var sql in new[] {
                "SELECT BondNumber, Notes FROM TBL010 WHERE CardGuide = @id",
                "SELECT BondNo, Notes FROM TBL010 WHERE CardGuide = @id",
                "SELECT TOP 1 BondNumber FROM TBL010 WHERE CardGuide = @id" })
            {
                try
                {
                    var cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@id", sourceId);
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        data.CardNumber = !r.IsDBNull(0) ? r.GetValue(0)?.ToString() ?? "" : sourceId.ToString("N")[..8];
                        if (r.FieldCount > 1 && !r.IsDBNull(1)) data.Notes = r.GetValue(1)?.ToString() ?? "";
                        data.CardName = data.CardName ?? "سند";
                        return;
                    }
                }
                catch { }
            }
            data.CardNumber = sourceId.ToString("N")[..Math.Min(12, sourceId.ToString("N").Length)];
            data.CardName = "سند";
        }


        private static string GetCompanyName(SqlConnection conn)
        {
            foreach (var sql in new[] {
                "SELECT TOP 1 CompanyName FROM Approve_Email WHERE ID = 1",
                "SELECT TOP 1 CompanyName FROM Approve_Email",
                "SELECT TOP 1 CompanyName FROM TBL000" })
            {
                try
                {
                    var cmd = new SqlCommand(sql, conn);
                    var r = cmd.ExecuteScalar();
                    if (r != null && !string.IsNullOrWhiteSpace(r.ToString())) return r.ToString()!.Trim();
                }
                catch { }
            }
            return "";
        }

        private static string GetCompanyPhone(SqlConnection conn)
        {
            foreach (var sql in new[] {
                "SELECT TOP 1 CompanyPhone FROM Approve_Email WHERE ID = 1",
                "SELECT TOP 1 CompanyPhone FROM Approve_Email" })
            {
                try
                {
                    var cmd = new SqlCommand(sql, conn);
                    var r = cmd.ExecuteScalar();
                    if (r != null && !string.IsNullOrWhiteSpace(r.ToString())) return r.ToString()!.Trim();
                }
                catch { }
            }
            return "";
        }

        static string GetColumnDisplayName(string name)
        {
            return name?.Trim() switch { "Account" => "الحساب", "Debit" => "مدين", "Credit" => "دائن", "Notes" => "البيان", _ => name ?? "" };
        }

        /// <summary>
        /// تحويل DataTable إلى جدول HTML — كما في *9009
        /// </summary>
        public static string DataTableToHtml(DataTable dt)
        {
            if (dt == null || dt.Rows.Count == 0) return "";
            var sb = new System.Text.StringBuilder("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;width:100%;font-size:13px;direction:rtl'><tr style='background:#32b380;color:#fff'>");
            foreach (DataColumn c in dt.Columns)
                sb.Append("<th style='padding:8px'>").Append(System.Net.WebUtility.HtmlEncode(GetColumnDisplayName(c.ColumnName ?? ""))).Append("</th>");
            sb.Append("</tr>");
            var alt = false;
            foreach (DataRow row in dt.Rows)
            {
                sb.Append(alt ? "<tr style='background:#f9f9f9'>" : "<tr>");
                foreach (var v in row.ItemArray)
                    sb.Append("<td style='padding:6px;border:1px solid #ddd'>").Append(System.Net.WebUtility.HtmlEncode(v?.ToString() ?? "")).Append("</td>");
                sb.Append("</tr>");
                alt = !alt;
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        /// <summary>
        /// تنفيذ استعلام شكل الإرسال من WF_Definitions — يستخدم @CardGuide أو @SourceId أو @BondGuide
        /// </summary>
        public static string? ExecuteFormatQuery(string connectionString, string? queryText, Guid sourceId)
        {
            if (string.IsNullOrWhiteSpace(queryText) || string.IsNullOrEmpty(connectionString)) return null;
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand(queryText.Trim(), conn) { CommandTimeout = 60 };
                cmd.Parameters.AddWithValue("@CardGuide", sourceId);
                cmd.Parameters.AddWithValue("@SourceId", sourceId);
                cmd.Parameters.AddWithValue("@BondGuide", sourceId);
                using var ad = new SqlDataAdapter(cmd);
                var dt = new DataTable();
                ad.Fill(dt);
                return DataTableToHtml(dt);
            }
            catch { return null; }
        }

        /// <summary>
        /// بناء جدول HTML من BondDetails — احتياطي عند خلو نتيجة EmailFormatQuery
        /// </summary>
        public static string? BondDetailsToHtmlTable(DocumentEmailData docData)
        {
            if (docData?.BondDetails == null || docData.BondDetails.Count == 0) return null;
            var sb = new System.Text.StringBuilder("<table border='1' cellpadding='6' cellspacing='0' style='border-collapse:collapse;width:100%;font-size:13px;direction:rtl'><tr style='background:#32b380;color:#fff'><th style='padding:8px'>الحساب</th><th>مدين</th><th>دائن</th><th>البيان</th></tr>");
            foreach (var r in docData.BondDetails)
            {
                sb.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(r.AccountName ?? "")).Append("</td><td>").Append(r.Debit != 0 ? r.Debit.ToString("#,##0.00") : "").Append("</td><td>").Append(r.Credit != 0 ? r.Credit.ToString("#,##0.00") : "").Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(r.Notes ?? "")).Append("</td></tr>");
            }
            sb.Append("</table>");
            return sb.ToString();
        }

        /// <summary>
        /// بناء كتلة سند قبض/صرف — نموذج موحد للاثنين
        /// الجدول السفلي يعرض عمودين: مدين ودائن (سند قبض يملأ الدائن، سند صرف يملأ المدين)
        /// </summary>
        public static string BuildBondPrintBlock(DocumentEmailData d)
        {
            if (d.SourceTable != "TBL010") return "";

            var bondTitle = d.CardName ?? "سند";
            var company = d.CompanyName ?? "";
            var cardNum = d.CardNumber ?? "";
            var bondDate = d.BondDate != default ? d.BondDate.ToString("dddd : d-M-yyyy") : "";
            var branch = d.BranchName ?? "";
            var costCenter = d.CostCenterName ?? "";
            var project = d.ProjectName ?? "";
            var currency = d.CurrencyName ?? "";
            var account = d.AccountName ?? "";
            var notes = d.Notes ?? "";
            var total = d.TotalAmount ?? "";

            var sb = new System.Text.StringBuilder();
            foreach (var row in d.BondDetails)
            {
                var debit = row.Debit != 0 ? row.Debit.ToString("#,##0.00") : "";
                var credit = row.Credit != 0 ? row.Credit.ToString("#,##0.00") : "";
                sb.Append($@"<tr><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:right"">{row.AccountCode}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:right"">{row.AccountName}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:left"">{debit}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:left"">{credit}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:right"">{row.Notes}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:right"">{row.CostCenterName}</td></tr>");
            }
            var gridHtml = d.BondDetails.Count > 0 ? $@"
<table style=""width:100%;border-collapse:collapse;font-size:11px;direction:rtl;font-family:Tahoma"" cellpadding=""0"" cellspacing=""0"">
<tr style=""background:#d8d8d8;font-weight:bold;font-size:10px""><td style=""padding:8px;border:1px solid #333"">كود الحساب</td><td style=""padding:8px;border:1px solid #333"">اسم الحساب</td><td style=""padding:8px;border:1px solid #333"">مدين</td><td style=""padding:8px;border:1px solid #333"">دائن</td><td style=""padding:8px;border:1px solid #333"">ملاحظات</td><td style=""padding:8px;border:1px solid #333"">مركز الكلفة</td></tr>
{sb}
</table>" : "";

            return $@"
<div style=""margin:16px 0;padding:24px;background:#fff;border:2px solid #333;font-family:'Traditional Arabic',Tahoma,Arial;direction:rtl"" class=""bond-print-block"">
<div style=""text-align:center;font-size:16px;font-weight:bold;margin-bottom:20px;font-family:'Traditional Arabic'"" dir=""rtl"">{company}</div>
<div style=""text-align:center;font-size:14px;font-weight:bold;margin-bottom:16px;font-family:'Traditional Arabic'"" dir=""rtl"">{bondTitle}</div>
<table style=""width:100%;font-size:10px;font-family:Tahoma"" cellpadding=""6"" cellspacing=""0"">
<tr><td style=""width:70px;padding:4px 8px"">رقم:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{cardNum}</td><td style=""width:70px;padding:4px 8px"">الفرع:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{branch}</td></tr>
<tr><td style=""padding:4px 8px"">التاريخ:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{bondDate}</td><td style=""padding:4px 8px"">مركز الكلفة:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{costCenter}</td></tr>
<tr><td style=""padding:4px 8px"">المشروع:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{project}</td><td style=""padding:4px 8px"">العملة:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{currency}</td></tr>
<tr><td style=""padding:4px 8px;font-weight:bold"">الحساب:</td><td colspan=""3"" style=""border:1px solid #999;padding:8px;background:#fff;font-weight:bold;font-size:12px"">{account}</td></tr>
<tr><td style=""padding:4px 8px"">ملاحظات:</td><td colspan=""3"" style=""border:1px solid #999;padding:6px 8px;background:#fff"">{notes}</td></tr>
<tr><td style=""padding:4px 8px;font-weight:bold"">المبلغ:</td><td colspan=""3"" style=""border:1px solid #999;padding:6px 8px;background:#fff;font-weight:bold"">{total} {currency}</td></tr>
</table>
{gridHtml}
<div style=""margin-top:14px;padding:8px 12px;background:#d8d8d8;font-weight:bold;font-size:12px;display:flex;justify-content:space-between""><span>المجموع</span><span style=""font-family:Calibri,Tahoma"">{total}</span></div>
</div>";
        }

        /// <summary>
        /// بناء كتلة فاتورة — بتنسيق INVOICE PRINT بدون شركة أو هاتف
        /// </summary>
        public static string BuildInvoicePrintBlock(DocumentEmailData d)
        {
            if (d.SourceTable != "TBL022") return "";

            var invTitle = d.CardName ?? "فاتورة";
            var cardNum = d.CardNumber ?? "";
            var bondDate = d.BondDate != default ? d.BondDate.ToString("dddd : d-M-yyyy") : "";
            var customer = d.AccountName ?? "";
            var total = d.TotalAmount ?? "";

            var sb = new System.Text.StringBuilder();
            foreach (var row in d.InvoiceDetails)
            {
                var qty = row.Quantity != 0 ? row.Quantity.ToString("#,##0.##") : "";
                var up = row.UnitPrice != 0 ? row.UnitPrice.ToString("#,##0.00") : "";
                var tv = row.TotalValue != 0 ? row.TotalValue.ToString("#,##0.00") : "";
                sb.Append($@"<tr><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:right"">{row.ItemName}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:center"">{qty}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:center"">{up}</td><td style=""padding:6px 8px;border:1px solid #999;font-size:11px;text-align:left"">{tv}</td></tr>");
            }
            var gridHtml = d.InvoiceDetails.Count > 0 ? $@"
<table style=""width:100%;border-collapse:collapse;font-size:11px;direction:rtl;font-family:Tahoma"" cellpadding=""0"" cellspacing=""0"">
<tr style=""background:#e5e5e5;font-weight:bold;font-size:10px""><td style=""padding:8px;border:1px solid #333"">الصنف</td><td style=""padding:8px;border:1px solid #333"">الكمية</td><td style=""padding:8px;border:1px solid #333"">السعر</td><td style=""padding:8px;border:1px solid #333"">الإجمالي</td></tr>
{sb}
</table>" : "";

            return $@"
<div style=""margin:16px 0;padding:24px;background:#fff;border:2px solid #333;font-family:'Traditional Arabic',Tahoma,Arial;direction:rtl"" class=""invoice-print-block"">
<div style=""text-align:center;font-size:14px;font-weight:bold;margin-bottom:16px;font-family:'Traditional Arabic'"" dir=""rtl"">{invTitle}</div>
<table style=""width:100%;font-size:10px;font-family:Tahoma"" cellpadding=""6"" cellspacing=""0"">
<tr><td style=""width:70px;padding:4px 8px"">رقم الفاتورة:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{cardNum}</td><td style=""width:70px;padding:4px 8px"">التاريخ:</td><td style=""border:1px solid #999;padding:6px 8px;background:#fff"">{bondDate}</td></tr>
<tr><td style=""padding:4px 8px;font-weight:bold"">الى السادة:</td><td colspan=""3"" style=""border:1px solid #999;padding:8px;background:#fff;font-weight:bold;font-size:12px"">{customer}</td></tr>
</table>
{gridHtml}
<div style=""margin-top:14px;padding:8px 12px;background:#e5e5e5;font-weight:bold;font-size:12px;display:flex;justify-content:space-between""><span>المجموع الصافي</span><span style=""font-family:Calibri,Tahoma"">{total}</span></div>
</div>";
        }

        /// <summary>
        /// بناء جسم الميل (عربي) — ملخص + (جدول المستند إن وُجد من استعلام شكل الإرسال) + رابط الموافقة
        /// </summary>
        /// <param name="documentBlock">اختياري: HTML جدول المستند من EmailFormatQuery</param>
        public static string BuildBodyArabic(DocumentEmailData d, string recipientName, string approveLink, string? documentBlock = null)
        {
            var cardName = d.CardName ?? "";
            var cardNum = d.CardNumber ?? "";
            var sendDate = d.SendDate.ToString("yyyy-MM-dd HH:mm");
            var sender = d.SenderName ?? "";
            var company = d.CompanyName ?? "";
            var phone = d.CompanyPhone ?? "";
            var notes = d.Notes ?? "";
            var logoUrl = GetLogoFallbackUrl();

            var summaryBlock = $@"<p style=""background:#f1f1f1;text-align:right;margin:10px;direction:rtl"">إلى السيد: <b>{recipientName}</b><br/>نوع البطاقة: <b>{cardName}</b> | رقمها: <b>{cardNum}</b><br/>تاريخ إرسال الطلب: <b>{sendDate}</b><br/>المرسل: <b>{sender}</b><br/>الشركة: <b>{company}</b><br/>الهاتف: <b dir=""ltr"">{phone}</b></p>";
            if (!string.IsNullOrWhiteSpace(notes))
                summaryBlock += $@"<div style=""background:#fff9c4;padding:10px;margin:10px;border-right:4px solid #fbc02d;text-align:right;direction:rtl""><b>ملاحظات:</b> {notes}</div>";

            var docBlock = string.IsNullOrWhiteSpace(documentBlock) ? "" : $@"<div style=""margin:16px 0;padding:12px;background:#fff;border:1px solid #ddd;overflow-x:auto"">{documentBlock}</div>";
            var sep = approveLink?.Contains("?") == true ? "&" : "?";
            var replyInstruction = $@"<p style=""font-size:13px;font-weight:bold;margin-top:16px;text-align:center;background:#e8f5e9;padding:12px;border-radius:8px"">للرد: <b>أعد الإرسال (Reply)</b> واكتب <code>#1#</code> موافق | <code>#2#</code> مرفوض | <code>#3#</code> يؤجل</p>";
            
            var linkBlock = string.IsNullOrEmpty(approveLink)
                ? replyInstruction
                : $@"<p style=""font-size:14px;font-weight:bold;margin-top:20px;text-align:center"">اضغط للرد مباشرة:</p>
<table role=""presentation"" cellpadding=""0"" cellspacing=""10"" align=""center"" style=""margin:16px auto""><tr>
<td><a href=""{approveLink}{sep}action=Approved"" style=""display:inline-block;padding:14px 24px;background:#22c55e;color:#fff!important;font-weight:bold;font-size:15px;text-decoration:none;border-radius:10px"" target=""_blank"">✓ موافق</a></td>
<td><a href=""{approveLink}{sep}action=Rejected"" style=""display:inline-block;padding:14px 24px;background:#ef4444;color:#fff!important;font-weight:bold;font-size:15px;text-decoration:none;border-radius:10px"" target=""_blank"">✗ رفض</a></td>
<td><a href=""{approveLink}{sep}action=Postponed"" style=""display:inline-block;padding:14px 24px;background:#f59e0b;color:#fff!important;font-weight:bold;font-size:15px;text-decoration:none;border-radius:10px"" target=""_blank"">⏳ يؤجل</a></td>
</tr></table>
{replyInstruction}
<p style=""font-size:11px;text-align:center;color:#666"">أو أعد الإرسال واكتب #1# أو #2# أو #3#</p>";

            return $@"<!DOCTYPE html><html lang=""ar"" dir=""rtl""><head><meta charset=""UTF-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/><title>TelleWork System | طلب موافقة</title></head><body style=""margin:0;padding:0;background:#e8e8e8;font-family:Arial,sans-serif;color:#333"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background:#e8e8e8;padding:20px 0""><tr><td align=""center"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" width=""600"" style=""max-width:100%;background:#f1f1f1;border-radius:20px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08);border:8px solid {ThemeColor};margin:10px;text-align:center;max-width:500px;margin-left:auto;margin-right:auto"">
<tr><td style=""padding:0"">
{(string.IsNullOrEmpty(logoUrl) ? "" : $@"<img src=""{logoUrl}"" width=""280"" height=""80"" style=""max-width:280px;height:auto;margin:15px auto;display:block"" alt="""" />")}
</td></tr>
<tr><td style=""padding:20px"">
<h1 style=""font-weight:bold;margin:5px;font-size:25px;background:#f1f1f1"">طلب موافقة | <span style=""color:{ThemeColor}"">TelleWork System</span></h1>
{summaryBlock}
{docBlock}
{linkBlock}
</td></tr></table></td></tr></table></body></html>";
        }

        /// <summary>
        /// بناء جسم رد الأوامر السحرية — بنفس تنسيق البودي والحدود
        /// </summary>
        public static string BuildCommandReplyBody(string commandCode, string resultHtml)
        {
            const string ThemeColor = "#32b380";
            var logoImg = GetLogoAsBase64DataUrl();
            var content = string.IsNullOrEmpty(resultHtml) ? "<p>لا توجد بيانات.</p>" : resultHtml;

            return $@"<!DOCTYPE html><html lang=""ar"" dir=""rtl""><head><meta charset=""UTF-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/><title>وافق | رد على الأمر</title></head><body style=""margin:0;padding:0;background:#e8e8e8;font-family:Arial,sans-serif"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background:#e8e8e8;padding:20px 0""><tr><td align=""center"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" width=""600"" style=""max-width:100%;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08);border:4px solid {ThemeColor}"">
<tr><td style=""padding:0"">
{(string.IsNullOrEmpty(logoImg) ? "" : $@"<img src=""{logoImg}"" width=""600"" style=""display:block;width:100%;max-width:600px;height:auto"" alt="""" />")}
</td></tr>
<tr><td style=""padding:20px 24px;background:#fff"">
<h2 style=""margin:0 0 16px;font-size:20px;color:#333;border-bottom:3px solid {ThemeColor};padding-bottom:10px"">نتيجة تنفيذ الأمر <span style=""color:{ThemeColor}"">{commandCode}</span></h2>
<div style=""background:#f9f9f9;margin:12px 0;padding:15px;border-radius:8px;text-align:right;direction:rtl;overflow-x:auto;font-size:14px"">
{content}
</div>
<p style=""font-size:13px;color:#666;margin:10px 0"">لإعادة الطلب أرسل ميلاً يتضمن <b style=""color:{ThemeColor}"">{commandCode}</b></p>
</td></tr></table></td></tr></table></body></html>";
        }

        /// <summary>
        /// بناء جسم الميل بالإنجليزي — ملخص + (جدول المستند إن وُجد) + رابط الموافقة
        /// </summary>
        public static string BuildBodyEnglish(DocumentEmailData d, string recipientName, string approveLink, string? documentBlock = null)
        {
            var cardName = d.CardNameLatin ?? d.CardName ?? "";
            var cardNum = d.CardNumber ?? "";
            var sendDate = d.SendDate.ToString("yyyy-MM-dd HH:mm");
            var sender = d.SenderNameLatin ?? d.SenderName ?? "";
            var company = d.CompanyName ?? "";
            var phone = d.CompanyPhone ?? "";
            var notes = d.Notes ?? "";
            var logoUrl = GetLogoFallbackUrl();

            var summaryBlock = $@"<p style=""background:#f1f1f1;text-align:left;margin:10px"">To Mr: <b>{recipientName}</b><br/>Card Type: <b>{cardName}</b> | Card Number: <b>{cardNum}</b><br/>Request Send Date: <b>{sendDate}</b><br/>Sender: <b>{sender}</b><br/>Company: <b>{company}</b><br/>Phone: <b>{phone}</b></p>";
            if (!string.IsNullOrWhiteSpace(notes))
                summaryBlock += $@"<div style=""background:#fff9c4;padding:10px;margin:10px;border-left:4px solid #fbc02d;text-align:left""><b>Notes:</b> {notes}</div>";

            var docBlock = string.IsNullOrWhiteSpace(documentBlock) ? "" : $@"<div style=""margin:16px 0;padding:12px;background:#fff;border:1px solid #ddd;overflow-x:auto"">{documentBlock}</div>";
            var sep = approveLink?.Contains("?") == true ? "&" : "?";
            var replyInstruction = $@"<p style=""font-size:13px;font-weight:bold;margin-top:16px;text-align:center;background:#e8f5e9;padding:12px;border-radius:8px"">To reply: <b>Reply to this email</b> and type <code>#1#</code> Approved | <code>#2#</code> Rejected | <code>#3#</code> Postponed</p>";
            
            var linkBlock = string.IsNullOrEmpty(approveLink)
                ? replyInstruction
                : $@"<p style=""font-size:14px;font-weight:bold;margin-top:20px;text-align:center"">Click to reply directly:</p>
<table role=""presentation"" cellpadding=""0"" cellspacing=""10"" align=""center"" style=""margin:16px auto""><tr>
<td><a href=""{approveLink}{sep}action=Approved"" style=""display:inline-block;padding:14px 24px;background:#22c55e;color:#fff!important;font-weight:bold;font-size:15px;text-decoration:none;border-radius:10px"" target=""_blank"">✓ Approved</a></td>
<td><a href=""{approveLink}{sep}action=Rejected"" style=""display:inline-block;padding:14px 24px;background:#ef4444;color:#fff!important;font-weight:bold;font-size:15px;text-decoration:none;border-radius:10px"" target=""_blank"">✗ Rejected</a></td>
<td><a href=""{approveLink}{sep}action=Postponed"" style=""display:inline-block;padding:14px 24px;background:#f59e0b;color:#fff!important;font-weight:bold;font-size:15px;text-decoration:none;border-radius:10px"" target=""_blank"">⏳ Postponed</a></td>
</tr></table>
{replyInstruction}
<p style=""font-size:11px;text-align:center;color:#666"">Or reply and type #1# or #2# or #3#</p>";

            return $@"<!DOCTYPE html><html lang=""en"" dir=""ltr""><head><meta charset=""UTF-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/><title>TelleWork System | Approval Request</title></head><body style=""margin:0;padding:0;background:#e8e8e8;font-family:Arial,sans-serif;color:#333"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background:#e8e8e8;padding:20px 0""><tr><td align=""center"">
<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" width=""600"" style=""max-width:100%;background:#f1f1f1;border-radius:20px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08);border:8px solid {ThemeColor};margin:10px;text-align:center;max-width:500px;margin-left:auto;margin-right:auto"">
<tr><td style=""padding:0"">
{(string.IsNullOrEmpty(logoUrl) ? "" : $@"<img src=""{logoUrl}"" width=""280"" height=""80"" style=""max-width:280px;height:auto;margin:15px auto;display:block"" alt="""" />")}
</td></tr>
<tr><td style=""padding:20px"">
<h1 style=""font-weight:bold;margin:5px;font-size:25px;background:#f1f1f1"">Request for Approval | <span style=""color:{ThemeColor}"">TelleWork System</span></h1>
{summaryBlock}
{docBlock}
{linkBlock}
</td></tr></table></td></tr></table></body></html>";
        }

        public class DocumentEmailData
        {
            public string SourceTable { get; set; } = "";
            public Guid SourceId { get; set; }
            public string CardNumber { get; set; } = "";
            public string CardName { get; set; } = "";
            public string CardNameLatin { get; set; } = "";
            public string Notes { get; set; } = "";
            public string SenderName { get; set; } = "";
            public string SenderNameLatin { get; set; } = "";
            public string CompanyName { get; set; } = "";
            public string CompanyPhone { get; set; } = "";
            public DateTime SendDate { get; set; } = DateTime.Now;
            /// <summary>تاريخ السند (من InDate في سند قبض)</summary>
            public DateTime BondDate { get; set; }
            /// <summary>الفرع (من xtrSearchData3 / TBL050)</summary>
            public string BranchName { get; set; } = "";
            /// <summary>مركز الكلفة (من SrhCostCenter / TBL005)</summary>
            public string CostCenterName { get; set; } = "";
            /// <summary>المشروع (من xtrSearchData1 / TBL049)</summary>
            public string ProjectName { get; set; } = "";
            /// <summary>العملة (من SrhCurrency / TBL001)</summary>
            public string CurrencyName { get; set; } = "";
            /// <summary>الحساب (من SrhAccount / TBL004)</summary>
            public string AccountName { get; set; } = "";
            /// <summary>المجموع (من NmbCredit / TBL012)</summary>
            public string TotalAmount { get; set; } = "";
            /// <summary>تفاصيل السند للطباعة</summary>
            public List<BondDetailRow> BondDetails { get; set; } = new();
            /// <summary>تفاصيل الفاتورة للطباعة</summary>
            public List<InvoiceDetailRow> InvoiceDetails { get; set; } = new();
        }

        public class InvoiceDetailRow
        {
            public string ItemName { get; set; } = "";
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal TotalValue { get; set; }
        }

        public class BondDetailRow
        {
            public string AccountCode { get; set; } = "";
            public string AccountName { get; set; } = "";
            public decimal Debit { get; set; }
            public decimal Credit { get; set; }
            public string Notes { get; set; } = "";
            public string CostCenterName { get; set; } = "";
        }
    }
}
