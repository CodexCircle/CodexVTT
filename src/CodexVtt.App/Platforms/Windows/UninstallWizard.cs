using System.Runtime.InteropServices;
using CodexVtt.Core;
using CodexVtt.Core.Maintenance;
using CodexVtt.Core.Settings;

namespace CodexVtt.App.WinUI;

/// <summary>
/// Procedura guidata di disinstallazione (hook di Velopack, eseguito da Update.exe --uninstall):
/// due domande, con "No" come risposta predefinita, prima di eliminare i dati interni di CodexVTT.
/// La cronologia in Documenti non viene mai toccata.
/// </summary>
internal static class UninstallWizard
{
    private const uint MbYesNo = 0x4;
    private const uint MbIconQuestion = 0x20;
    private const uint MbDefButton2 = 0x100;
    private const int IdYes = 6;

    public static void Run()
    {
        try
        {
            var settings = new SettingsStore(Path.Combine(AppPaths.DefaultRoot, "settings.json")).Load();
            var root = AppPaths.DefaultRoot;
            var size = InternalDataRemover.Measure(root);

            if (Ask(NativeText.Get("uninstallStep1", settings).Replace("{size}", FormatSize(size)), settings) != IdYes)
                return;
            if (Ask(NativeText.Get("uninstallStep2", settings), settings) != IdYes)
                return;

            InternalDataRemover.Remove(root);
        }
        catch (Exception)
        {
            // La disinstallazione deve completarsi comunque: nessun errore dalla procedura guidata.
        }
    }

    private static int Ask(string text, AppSettings settings) =>
        MessageBox(IntPtr.Zero, text, NativeText.Get("uninstallTitle", settings), MbYesNo | MbIconQuestion | MbDefButton2);

    private static string FormatSize(long bytes) =>
        bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000.0:0.#} GB" :
        bytes >= 1_000_000 ? $"{bytes / 1_000_000.0:0.#} MB" :
        $"{bytes / 1_000.0:0} KB";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
