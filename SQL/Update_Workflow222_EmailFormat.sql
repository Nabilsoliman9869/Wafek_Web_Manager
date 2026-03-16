-- =============================================================================
-- تحديث ورك فلو 222 — إدخال استعلام طباعة السند في شكل الإرسال
-- =============================================================================

-- استعلام يعتمد على TBL012 مباشرة (لا يحتاج TBL011 أو Posted)
-- يعمل لسندات الموافقة قبل الترحيل
-- TBL038 = تفاصيل السند (MainGuide = TBL010.CardGuide)
UPDATE WF_Definitions
SET EmailFormatQuery = N'
SELECT
    dbo.TBL004.AccountName AS Account,
    SUM(ISNULL(d.DebitRate,0)) AS Debit,
    SUM(ISNULL(d.CreditRate,0)) AS Credit,
    MAX(ISNULL(d.TruncatedNotes,d.Notes)) AS Notes
FROM dbo.TBL038 d
LEFT JOIN dbo.TBL004 ON d.AccountGuide = dbo.TBL004.CardGuide
WHERE d.MainGuide = @CardGuide
GROUP BY dbo.TBL004.AccountName
'
WHERE Id = 2;

-- التحقق (ورك فلو "222" له Id = 2)
SELECT Id, Name, SourceTable, LEFT(EmailFormatQuery, 100) AS EmailFormatQueryPreview
FROM WF_Definitions
WHERE Id = 2;
