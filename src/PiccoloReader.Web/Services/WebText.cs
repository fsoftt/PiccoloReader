using System.Globalization;

namespace PiccoloReader.Web.Services;

// Strings of web-only features (offline, backup, linked folder). Everything shared with the
// Android app comes from Core's AppStrings; these follow the same UI culture (en / es).
public static class WebText
{
    private static bool Es => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";

    private static string T(string en, string es) => Es ? es : en;

    public static string UpdateAvailable => T("A new version is available", "Hay una nueva versión disponible");
    public static string Reload => T("Reload", "Recargar");
    public static string IosInstallTitle => T("Install on iPhone or iPad", "Instalar en iPhone o iPad");
    public static string IosInstallStep1 => T("Tap the Share button in the Safari toolbar", "Toca el botón Compartir en la barra de Safari");
    public static string IosInstallStep2 => T("Scroll down and choose Add to Home Screen", "Desplázate y elige Añadir a pantalla de inicio");
    public static string IosInstallStep3 => T("Tap Add. Piccolo Reader opens full screen, like an app", "Toca Añadir. Piccolo Reader se abre a pantalla completa, como una app");
    public static string IosInstallHint => T("Install Piccolo Reader: Share, then Add to Home Screen", "Instala Piccolo Reader: Compartir y Añadir a pantalla de inicio");
    public static string IosInstallShow => T("Show me", "Cómo");
    public static string GotIt => T("Got it", "Entendido");
    public static string InstallApp => T("Install app", "Instalar app");
    public static string InstallAppDescription => T("Add Piccolo Reader to your home screen or desktop", "Añade Piccolo Reader a tu pantalla de inicio o escritorio");
    public static string Installed => T("Installed", "Instalada");

    public static string BrowserStorage => T("Browser storage", "Almacenamiento del navegador");
    public static string StorageUsage(string used, string quota) => string.Format(T("{0} used of {1} available", "{0} usados de {1} disponibles"), used, quota);
    public static string StoragePersisted => T("Protected: the browser will not clear it automatically", "Protegido: el navegador no lo borrará automáticamente");
    public static string StorageNotPersisted => T("Not protected: the browser may clear it when space runs low. Tap to protect.", "Sin proteger: el navegador puede borrarlo si falta espacio. Toca para protegerlo.");
    public static string StorageProtected => T("Storage protected", "Almacenamiento protegido");
    public static string StorageNotGranted => T("The browser did not grant protection. Installing the app usually helps.", "El navegador no concedió la protección. Instalar la app suele ayudar.");

    public static string BackupSection => T("Backup", "Copia de seguridad");
    public static string ExportLibrary => T("Export library", "Exportar biblioteca");
    public static string ExportLibraryDescription => T("Save PDFs, annotations and bookmarks as a .zip file", "Guarda PDF, anotaciones y marcadores en un archivo .zip");
    public static string ImportLibrary => T("Import library", "Importar biblioteca");
    public static string ImportLibraryDescription => T("Merge a .zip backup into this library", "Combina una copia .zip con esta biblioteca");
    public static string Exporting(int current, int total) => string.Format(T("Exporting your library… {0}/{1}", "Exportando tu biblioteca… {0}/{1}"), current, total);
    public static string Importing(int current, int total) => string.Format(T("Importing… {0}/{1}", "Importando… {0}/{1}"), current, total);
    public static string Reading => T("Reading the backup…", "Leyendo la copia…");
    public static string ExportDone => T("Library exported", "Biblioteca exportada");
    public static string ExportEmpty => T("The library is empty", "La biblioteca está vacía");
    public static string ImportFailed => T("That file is not a valid backup", "Ese archivo no es una copia válida");

    public static string LinkedFolder => T("Linked folder", "Carpeta vinculada");
    public static string LinkedFolderDescription => T("Keep a copy of your library in a folder on this computer", "Guarda una copia de tu biblioteca en una carpeta de este equipo");
    public static string LinkedFolderChoose => T("Choose folder", "Elegir carpeta");
    public static string LinkedFolderActive(string name) => string.Format(T("Saving to \"{0}\"", "Guardando en \"{0}\""), name);
    public static string LinkedFolderNeedsAccess(string name) => string.Format(T("\"{0}\" needs permission again. Tap to allow.", "\"{0}\" necesita permiso otra vez. Toca para permitir."), name);
    public static string LinkedFolderUnlink => T("Unlink folder", "Desvincular carpeta");
    public static string LinkedFolderUnlinkMessage => T("The files already in the folder are kept.", "Los archivos que ya están en la carpeta se conservan.");
    public static string LinkedFolderSyncing(int current, int total) => string.Format(T("Saving your library to the folder… {0}/{1}", "Guardando tu biblioteca en la carpeta… {0}/{1}"), current, total);
    public static string LinkedFolderSaved => T("Library saved to the folder", "Biblioteca guardada en la carpeta");
    public static string LinkedFolderDenied => T("Access to the folder was not granted", "No se concedió acceso a la carpeta");

    public static string DropPdfs => T("Drop PDFs to import", "Suelta PDF para importar");
    public static string Imported(int count) => count == 1 ? T("1 PDF imported", "1 PDF importado") : string.Format(T("{0} PDFs imported", "{0} PDF importados"), count);

    public static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return (i == 0 ? bytes.ToString("0", CultureInfo.InvariantCulture) : bytes.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[i];
    }
}
