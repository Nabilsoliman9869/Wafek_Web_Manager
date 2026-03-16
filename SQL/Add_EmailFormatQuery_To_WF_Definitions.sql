-- =============================================
-- إضافة عمود شكل الإرسال (EmailFormatQuery) إلى WF_Definitions
-- يستوعب استعلام SQL يعرض المستند في ميل الموافقة (كما في *9009)
-- الاستعلام يستخدم @CardGuide أو @SourceId للربط بالمستند
-- =============================================

IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WF_Definitions]') AND type in (N'U'))
   AND COL_LENGTH('dbo.WF_Definitions', 'EmailFormatQuery') IS NULL
BEGIN
    ALTER TABLE [dbo].[WF_Definitions] ADD [EmailFormatQuery] [nvarchar](max) NULL;
    PRINT 'تمت إضافة عمود EmailFormatQuery (شكل الإرسال) إلى WF_Definitions';
END
ELSE
BEGIN
    PRINT 'العمود EmailFormatQuery موجود مسبقاً أو الجدول غير موجود.';
END
GO

-- =============================================
-- مثال استعلام لسند (TBL010 + TBL012)
-- =============================================
/*
SELECT 
    acc.AccountName AS [الحساب],
    d.Debit AS [مدين],
    d.Credit AS [دائن],
    d.Notes AS [ملاحظات]
FROM TBL012 d
LEFT JOIN TBL004 acc ON acc.CardGuide = d.AccountGuide
WHERE d.MainGuide IN (SELECT CardGuide FROM TBL010 WHERE CardGuide = @CardGuide)
*/

-- =============================================
-- مثال استعلام لفاتورة (TBL022 + TBL023)
-- =============================================
/*
SELECT 
    ISNULL(p.ItemName, p.LatinName) AS [الصنف],
    d.Quantity AS [الكمية],
    d.UnitPrice AS [السعر],
    d.TotalValue AS [الإجمالي]
FROM TBL023 d
LEFT JOIN TBL007 p ON p.CardGuide = d.ProductGuide
WHERE d.MainGuide IN (SELECT CardGuide FROM TBL022 WHERE CardGuide = @CardGuide)
*/
