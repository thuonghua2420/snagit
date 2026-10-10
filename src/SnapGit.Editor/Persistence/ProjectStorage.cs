using SnapGit.Core.Library;

namespace SnapGit.Editor.Persistence;

/// <summary>Saves/loads an <see cref="EditorDocument"/> as a ".snapgit" project.</summary>
public static class ProjectStorage
{
    /// <param name="markSaved">false for autosave/recovery copies: the document stays "modified".</param>
    public static void Save(EditorDocument document, string path, bool markSaved = true)
    {
        var json = AnnotationJson.Serialize(document.Annotations);
        using var preview = document.Render();
        SnapProjectFile.Write(path, document.Image, json, preview);
        if (markSaved) document.MarkSaved();
    }

    public static EditorDocument Load(string path)
    {
        var contents = SnapProjectFile.Read(path);
        var document = new EditorDocument(contents.Image);
        document.LoadAnnotations(AnnotationJson.Deserialize(contents.DocumentJson));
        return document;
    }
}
