using Bloom.Search;

namespace Bloom.App;

/// A path typed into the search line: the folder or file it names first, then what is in that folder (or what starts
/// with the last part). Tab fills the selected folder into the line, to go one level down.
internal sealed partial class Engine
{
    private Section? RootPath(string text)
    {
        if (PathInput.Read(text) is not { } p) return null;
        var s = new Section(Tr("경로", "Path"), PathInput.IsNetwork(p.Full) ? Tr("네트워크", "Network") : p.Children.Count > 0 ? Tr("Tab으로 들어가기", "Tab goes in") : null);
        if (p.Exists) s.Items.Add(OpenPathItem(p.Full, p.IsFolder));
        else if (PathInput.IsNetwork(p.Full)) s.Items.Add(OpenPathItem(p.Full, true));
        foreach (var (path, folder) in p.Children) s.Items.Add(FromPath(path, folder));
        if (s.Items.Count == 0)
            s.Items.Add(new ResultItem { Id = "path:none", Kind = ItemKind.Info, Title = Tr("경로 없음 · 경로를 확인하세요", "Path not found. Check the path."), Subtitle = p.Full, Icon = IconSpec.Of(G.Info) });
        return s;
    }

    /// The typed path itself, first in the list: Enter opens it (a folder in Explorer).
    private ResultItem OpenPathItem(string full, bool folder)
    {
        var item = FromPath(full, folder);
        item.Title = full;
        item.Subtitle = folder ? Tr("탐색기에서 열기", "Open in Explorer") : Tr("파일 열기", "Open the file");
        item.PrimaryLabel = folder ? Tr("폴더 열기", "Open folder") : Tr("열기", "Open");
        item.Score = int.MaxValue;
        if (PathInput.IsNetwork(full)) item.Detail = null; // a server may not answer: no preview
        return item;
    }

    /// Tab on a folder row of a typed path: the line becomes that folder (with its \), and its contents show.
    public static string? DigInto(string text, ResultItem? selected) =>
        PathInput.Looks(text) && selected is { Kind: ItemKind.Folder, Data: string path } && !PathInput.IsNetwork(path)
            ? (path.EndsWith('\\') ? path : path + "\\")
            : null;
}
