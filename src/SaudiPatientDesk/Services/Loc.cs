using System.Globalization;
using System.Windows;

namespace SaudiPatientDesk.Services;

/// <summary>واجهة عربية ثابتة باتجاه من اليمين إلى اليسار وتقويم ميلادي فقط.</summary>
public static class Loc
{
    public const string Code = "ar";

    public static void Load(SettingsService settings)
    {
        settings.Set("ui_language", Code);
        ApplyThreadCulture();
    }

    public static void Save(SettingsService settings)
    {
        settings.Set("ui_language", Code);
        ApplyThreadCulture();
    }

    public static void ApplyToWindow(Window window)
    {
        window.FlowDirection = FlowDirection.RightToLeft;
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage("ar-SA");
    }

    public static string T(string key) => Ar.TryGetValue(key, out var value) ? value : key;

    private static void ApplyThreadCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar(GregorianCalendarTypes.Localized);
        culture.DateTimeFormat.ShortDatePattern = "yyyy/MM/dd";
        culture.DateTimeFormat.LongDatePattern = "yyyy/MM/dd";
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    private static readonly Dictionary<string, string> Ar = new()
    {
        ["app.title"] = "نظام سجلات المرضى",
        ["nav.dashboard"] = "لوحة التحكم",
        ["nav.patients"] = "المرضى",
        ["nav.appointments"] = "المواعيد",
        ["nav.search"] = "البحث الشامل",
        ["nav.settings"] = "الإعدادات",
        ["metric.patients"] = "إجمالي المرضى",
        ["metric.today"] = "مواعيد اليوم",
        ["metric.alerts"] = "تنبيهات يومين",
        ["metric.inactive"] = "لم يراجع منذ 10 سنوات",
        ["dash.timeline"] = "الجدول الزمني لليوم",
        ["dash.todaylist"] = "قائمة اليوم — إجراءات سريعة",
        ["dash.available"] = "متاح",
        ["dash.booked"] = "محجوز",
        ["dash.break"] = "استراحة",
        ["dash.now"] = "الآن",
        ["dash.walkin"] = "حضور اليوم بدون موعد",
        ["act.checkin"] = "حضر",
        ["act.waiting"] = "انتظار",
        ["act.noshow"] = "لم يحضر",
        ["act.cancel"] = "إلغاء",
        ["act.followup"] = "حجز متابعة؟",
        ["act.followup.body"] = "تم تسجيل الحضور. هل تريد حجز موعد مجدّد للمتابعة؟",
        ["set.language"] = "لغة الواجهة",
        ["set.language.ar"] = "العربية (من اليمين إلى اليسار)",
        ["set.saved"] = "تم حفظ الإعدادات.",
        ["col.time"] = "الوقت",
        ["col.patient"] = "اسم المراجع",
        ["col.file"] = "رقم الملف",
        ["col.phone"] = "الجوال",
        ["col.clinician"] = "المعالج",
        ["col.flag"] = "تنبيه",
        ["col.status"] = "الحالة",
        ["col.actions"] = "إجراءات",
        ["status.waiting"] = "في الانتظار",
        ["status.scheduled"] = "مجدول",
        ["status.completed"] = "مكتمل",
        ["status.cancelled"] = "ملغي",
        ["status.missed"] = "لم يحضر"
    };
}
