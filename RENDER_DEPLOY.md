# رفع Wafek_Web_Manager على Render

## خطوات النشر

### 1. رفع المشروع على GitHub
- أنشئ مستودعاً جديداً وارفع الكود.

### 2. إنشاء خدمة على Render
- ادخل إلى [dashboard.render.com](https://dashboard.render.com)
- **New** → **Blueprint** (أو **Web Service**)
- اربط المستودع

### 3. ضبط المتغيرات البيئية
في تبويب **Environment** أضف:

| المتغير | الوصف | مثال |
|---------|-------|------|
| `APPROVE_BASE_URL` | **مهم** — رابط التطبيق بعد النشر | `https://wafek-manager.onrender.com` |
| `DB_SERVER` | خادم SQL Server | `xtra.webhop.me,1411` |
| `DB_NAME` | اسم قاعدة البيانات | `La7_ahmedsalman2026` |
| `DB_USER` | اسم المستخدم | `LA7` |
| `DB_PASSWORD` | كلمة المرور | ( secret ) |
| `SENDER_EMAIL` | ميل الإرسال | `your@gmail.com` |
| `SENDER_PASSWORD` | كلمة مرور التطبيق (Gmail App Password) | ( secret ) |
| `SMTP_SERVER` | (اختياري) | `smtp.gmail.com` |
| `SMTP_PORT` | (اختياري) | `587` |
| `IMAP_SERVER` | (اختياري) | `imap.gmail.com` |
| `IMAP_PORT` | (اختياري) | `993` |

### 4. رابط الموافقة
بعد النشر، انسخ الرابط (مثال: `https://wafek-manager.onrender.com`) وضعه في `APPROVE_BASE_URL`.

بهذا سيعمل الرابط في الميل ويوجه المستلم إلى صفحة الأزرار (موافق / رفض / يؤجل).

### 5. الطبقة المجانية
- الخدمة قد تتوقف بعد 15 دقيقة من عدم الاستخدام.
- أول طلب بعد التوقف قد يأخذ 30–60 ثانية (تشغيل بارد).
- لاستخدام مستمر، يُفضّل الخطة المدفوعة.
