-- =============================================================================
-- استعلام سند القيد للطباعة في ميل الموافقة
-- الربط: TBL010 (السند) ← TBL011.BondGuide = TBL010.CardGuide
--        TBL012 ← TBL004, TBL005, TBL016, TBL001, TBL049, TBL050
-- المعامل: @BondGuide = CardGuide السند من TBL010
-- =============================================================================
-- انسخ هذا الاستعلام إلى حقل "شكل الإرسال" في Workflow Designer
-- =============================================================================

-- خيار 1: مجمع حسب الحساب (أنسب للإيميل — ك*9009)
SELECT
    dbo.TBL004.AccountName AS [الحساب],
    SUM(dbo.TBL012.Debit) AS [مدين],
    SUM(dbo.TBL012.Credit) AS [دائن],
    SUM(dbo.TBL012.DebitRate) AS [تعادل المدين],
    SUM(dbo.TBL012.CreditRate) AS [تعادل الدائن],
    dbo.TBL001.CurrencyName AS [العملة],
    dbo.TBL005.CostCenter AS [مركز الكلفة],
    dbo.TBL050.BronchName AS [الفرع],
    dbo.TBL049.ProjectName AS [المشروع]
FROM dbo.TBL011
INNER JOIN dbo.TBL012 ON dbo.TBL011.CardGuide = dbo.TBL012.MainGuide
INNER JOIN dbo.TBL004 ON dbo.TBL012.AccountGuide = dbo.TBL004.CardGuide
INNER JOIN dbo.TBL001 ON dbo.TBL012.CurrencyGuide = dbo.TBL001.CardGuide
LEFT JOIN dbo.TBL005 ON dbo.TBL012.CostCenter = dbo.TBL005.CardGuide
LEFT JOIN dbo.TBL050 ON dbo.TBL012.Branch = dbo.TBL050.CardGuide
LEFT JOIN dbo.TBL049 ON dbo.TBL012.Project = dbo.TBL049.CardGuide
WHERE dbo.TBL011.BondGuide = @BondGuide
  AND dbo.TBL011.Posted = 1
GROUP BY dbo.TBL004.AccountName, dbo.TBL001.CurrencyName,
         dbo.TBL005.CostCenter, dbo.TBL050.BronchName, dbo.TBL049.ProjectName;

/* خيار 2: تفاصيل كل سطر (بدون تجميع) — للحصول عليه علّق خيار 1 وعلّق هذه الكتلة
SELECT
    d.ID,
    d.Debit             AS [مدين],
    d.Credit            AS [دائن],
    dbo.TBL004.AccountName AS [الحساب],
    d.Description       AS [البيان],
    dbo.TBL001.CurrencyName AS [العملة],
    d.DebitRate         AS [تعادل المدين],
    d.CreditRate        AS [تعادل الدائن],
    dbo.TBL005.CostCenter AS [مركز الكلفة],
    contra.AccountName  AS [الحساب المقابل],
    dbo.TBL049.ProjectName AS [المشروع],
    dbo.TBL050.BronchName AS [الفرع],
    dbo.TBL016.AgentName AS [العميل]
FROM dbo.TBL011 h
INNER JOIN dbo.TBL012 d ON h.CardGuide = d.MainGuide
INNER JOIN dbo.TBL004 ON d.AccountGuide = dbo.TBL004.CardGuide
INNER JOIN dbo.TBL001 ON d.CurrencyGuide = dbo.TBL001.CardGuide
LEFT JOIN dbo.TBL005 ON d.CostCenter = dbo.TBL005.CardGuide
LEFT JOIN dbo.TBL050 ON d.Branch = dbo.TBL050.CardGuide
LEFT JOIN dbo.TBL049 ON d.Project = dbo.TBL049.CardGuide
LEFT JOIN dbo.TBL016 ON d.AgentGuide = dbo.TBL016.CardGuide
LEFT JOIN dbo.TBL004 contra ON d.ContraAccount = contra.CardGuide
WHERE h.BondGuide = @BondGuide
ORDER BY d.ID;
*/
