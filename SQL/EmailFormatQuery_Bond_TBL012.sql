-- =============================================================================
-- استعلام سند القيد للطباعة في ميل الموافقة (بدون الاعتماد على TBL011)
-- الربط: TBL012.MainGuide = TBL010.CardGuide (السند قبل أو بعد الترحيل)
-- المعامل: @CardGuide = CardGuide السند من TBL010 (نفس SourceRecordId)
-- =============================================================================
-- يعمل لسندات لم تُرحّل بعد (لا يوجد TBL011) — مناسب لطلبات الموافقة
-- =============================================================================

-- خيار 1: مجمع حسب الحساب
SELECT
    dbo.TBL004.AccountName AS [الحساب],
    SUM(dbo.TBL012.Debit) AS [مدين],
    SUM(dbo.TBL012.Credit) AS [دائن],
    MAX(dbo.TBL012.Notes) AS [البيان]
FROM dbo.TBL012
LEFT JOIN dbo.TBL004 ON dbo.TBL012.AccountGuide = dbo.TBL004.CardGuide
WHERE dbo.TBL012.MainGuide = @CardGuide
GROUP BY dbo.TBL004.AccountName;
