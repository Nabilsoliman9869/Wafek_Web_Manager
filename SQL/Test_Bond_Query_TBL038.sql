-- =============================================================================
-- اختبار استعلام السند من TBL038 — استبدل الـ GUID برقم السند
-- =============================================================================

DECLARE @CardGuide UNIQUEIDENTIFIER = '24F727B8-47B1-4940-9630-F776F3D42B1F';

-- تفاصيل مجمعة حسب الحساب (للإيميل)
SELECT
    TBL004.AccountName AS [الحساب],
    SUM(ISNULL(d.DebitRate,0)) AS [مدين],
    SUM(ISNULL(d.CreditRate,0)) AS [دائن],
    MAX(ISNULL(d.TruncatedNotes,d.Notes)) AS [البيان]
FROM TBL038 d
LEFT JOIN TBL004 ON d.AccountGuide = TBL004.CardGuide
WHERE d.MainGuide = @CardGuide
GROUP BY TBL004.AccountName;
