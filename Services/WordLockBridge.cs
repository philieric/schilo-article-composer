using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace SchiloArticleComposer.Services;

// Contourne le verrou exclusif que Word (ou OneDrive en cours de synchronisation)
// pose sur un .docx en edition : FileShare.None cote Word signifie qu'aucun mode de
// partage cote lecteur ne permet d'ouvrir le fichier (teste : ReadWrite/Read/None
// echouent tous). Si le fichier cible est ouvert dans une instance Word active, cette
// classe pilote Word par COM pour copier son contenu ACTUEL (y compris les
// modifications non enregistrees) dans un nouveau document temporaire non verrouille,
// sans toucher au document original ni au presse-papier systeme.
//
// Passe par la Running Object Table (ROT) plutot que par
// Marshal.GetActiveObject("Word.Application") : avec plusieurs fenetres/process Word
// ouverts simultanement, GetActiveObject par ProgID ne renvoie qu'une instance
// arbitraire (parfois sans aucun document), alors que chaque document ouvert est
// enregistre individuellement dans la ROT et peut etre retrouve par son chemin. Pour
// les fichiers synchronises OneDrive, Word enregistre le document dans la ROT sous son
// URL cloud (https://d.docs.live.net/...) et non son chemin local — la comparaison se
// fait donc sur le nom de fichier final, pas le chemin complet.
public static class WordLockBridge
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

    public static string? TryGetUnlockedCopy(string targetPath)
    {
        var fileName = Path.GetFileName(targetPath);

        if (GetRunningObjectTable(0, out var rot) != 0 || CreateBindCtx(0, out var bindCtx) != 0)
        {
            return null;
        }

        rot.EnumRunning(out var enumMoniker);
        enumMoniker.Reset();

        var monikers = new IMoniker[1];
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            string displayName;
            try
            {
                monikers[0].GetDisplayName(bindCtx, null, out displayName);
            }
            catch
            {
                continue;
            }

            var decoded = Uri.TryCreate(displayName, UriKind.Absolute, out _)
                ? Uri.UnescapeDataString(displayName)
                : displayName;

            if (!decoded.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            object comObj;
            try
            {
                rot.GetObject(monikers[0], out comObj);
            }
            catch
            {
                continue;
            }

            var tempCopy = TryCopyViaWord(comObj);
            if (tempCopy != null)
            {
                return tempCopy;
            }
        }

        return null;
    }

    private static string? TryCopyViaWord(object comDocument)
    {
        dynamic doc = comDocument;
        var tempPath = Path.Combine(Path.GetTempPath(), $"SchiloArticleComposer-lock-{Guid.NewGuid():N}.docx");
        object? newDocObj = null;
        try
        {
            dynamic app = doc.Application;
            dynamic newDoc = app.Documents.Add();
            newDocObj = newDoc;
            newDoc.Range().FormattedText = doc.Range().FormattedText;
            newDoc.SaveAs2(tempPath);
            return tempPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (newDocObj != null)
            {
                try { ((dynamic)newDocObj).Close(false); } catch { /* best effort */ }
            }
        }
    }
}
