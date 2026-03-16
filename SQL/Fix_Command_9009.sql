-- تحديث الأمر *9009 بإضافة نص الاستعلام
UPDATE WF_EmailCommands
SET ExecutionContent = N'
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
WHERE dbo.TBL011.Posted = 1
GROUP BY
    dbo.TBL004.AccountName,
    dbo.TBL001.CurrencyName,
    dbo.TBL005.CostCenter,
    dbo.TBL050.BronchName,
    dbo.TBL049.ProjectName
'
WHERE CommandCode = '*9009' AND Id = 1;

-- في حال كان الكود *9009* (مع النجمة الأخيرة) استخدم:
-- WHERE CommandCode = '*9009*' AND Id = 1;
