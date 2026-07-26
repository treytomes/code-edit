using Terminal.Gui.ViewBase;

namespace CodeEdit.Presentation.Views;

public sealed class SidebarView : View
{
    private readonly FileTreeView _fileTree;

    public SidebarView(FileTreeView fileTree)
    {
        _fileTree        = fileTree;
        CanFocus         = true;
        _fileTree.X      = 0;
        _fileTree.Y      = 0;
        _fileTree.Width  = Dim.Fill();
        _fileTree.Height = Dim.Fill();
        Add(_fileTree);
    }

    public void SetFocusToActivePanel() => _fileTree.SetFocus();
}
