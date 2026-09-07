# معمارية الإصدار الحديث 3.0

## منصة التشغيل

- Windows 10 الإصدار 1809 فأحدث، وWindows 11.
- .NET 10.
- WinUI 3 وWindows App SDK 2.4 (القناة المستقرة).
- حزمة MSIX أصلية تُثبت من Windows App Installer بواجهة مرئية، أو من Microsoft Store. لا يوجد تثبيت صامت ولا Inno Setup أو WiX MSI في مسار الإصدار الحديث.

## طبقات البرنامج

- `PatientRecordsSaudi.Modern.Core`: المجال، التحقق السعودي، ترقيم الملفات، كشف التكرار، تعارض المواعيد، الصلاحيات، تشفير LiteDB، التدقيق والنسخ الاحتياطي.
- `PatientRecordsSaudi.WinUI`: واجهة WinUI 3 عربية RTL تعمل فوق النواة دون تكرار قواعد العمل.
- `PatientRecordsSaudi.Modern.Tests`: نفس اختبارات السعة 10,000 والهوية والمواعيد والصلاحيات ولكن على .NET 10.

## التوقيع

يُنتج CI حزمة Store Submission غير موقعة بشهادة محلية. بعد ربط هوية التطبيق المحجوزة في Partner Center ورفع الحزمة، تفحص Microsoft التطبيق ثم تعيد توقيع MSIX بشهادة Microsoft الموثوقة. للتوزيع المباشر خارج المتجر يلزم بدلًا من ذلك شراء شهادة Code Signing فردية من جهة موثوقة ومطابقة Subject الشهادة مع Publisher في البيان.
