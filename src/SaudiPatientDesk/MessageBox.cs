using System.Windows;

namespace SaudiPatientDesk;

/// <summary>يوحّد جميع رسائل البرنامج باتجاه عربي من اليمين إلى اليسار.</summary>
internal static class MessageBox
{
    private const MessageBoxOptions ArabicOptions =
        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    public static MessageBoxResult Show(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image) =>
        System.Windows.MessageBox.Show(
            message,
            title,
            buttons,
            image,
            MessageBoxResult.None,
            ArabicOptions);
}
