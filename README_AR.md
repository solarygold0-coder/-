# نظام سجلات المرضى — الجيل السادس

هذا مشروع جديد مستقل، وليس ترقية لأي إصدار سابق.

## الضمانات المعمارية

- يبدأ البرنامج مباشرة بلوحة التحكم دون شاشة دخول.
- لا توجد جداول مستخدمين أو أدوار أو كلمات مرور.
- هوية التنفيذ `SaudiPatientDesk` ومسار البيانات `SaudiPatientDesk/Generation6` جديدان تماماً.
- قاعدة SQLite باسم `patient-records-v6.sqlite3` لا تفتح قواعد الإصدارات السابقة.
- أرقام الملفات متسلسلة ولا يعاد استخدام الرقم المحذوف.
- اكتشاف التكرار بالهوية أو رقم الجوال قبل إنشاء السجل.
- منع تعارض المواعيد ومنع حجز الجمعة والسبت.
- تنبيه المواعيد القادمة خلال يومين في لوحة التحكم.
- البحث بالاسم ورقم الملف والهوية والجوال.
- حذف آمن للسجلات مع الاحتفاظ بتسلسل الأرقام.

## النظام المستهدف

Windows 10 وWindows 11، بمعمارية x64. يعتمد التطبيق على .NET 10 وWPF وSQLite.

## البناء

```powershell
dotnet restore SaudiPatientDesk.sln
dotnet build SaudiPatientDesk.sln -c Release
dotnet publish src/SaudiPatientDesk/SaudiPatientDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

ينتج مسار البناء الآلي ملف ZIP يحتوي التطبيق المستقل. التوقيع الموثوق يتطلب شهادة Code Signing حقيقية ولا يمكن اصطناعها داخل المصدر.
