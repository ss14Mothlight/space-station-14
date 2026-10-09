using Content.Client.Message;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Persistence.Factions.UI;

/// <summary>
/// One of a crew member's records on the ID card console. Editable for those allowed to edit records, read-only
/// otherwise, and printable either way.
/// </summary>
public sealed class FactionRecordEditor : BoxContainer
{
    public event Action<string>? OnSave;
    public event Action? OnPrint;

    private readonly TextEdit _edit;
    private readonly RichTextLabel _view;
    private readonly ScrollContainer _viewScroll;
    private readonly Button _save;
    private string _lastContent = string.Empty;

    public FactionRecordEditor()
    {
        Orientation = LayoutOrientation.Vertical;
        VerticalExpand = true;

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        _save = new Button { Text = Loc.GetString("faction-id-console-record-save") };
        _save.OnPressed += _ => OnSave?.Invoke(Rope.Collapse(_edit!.TextRope));
        var print = new Button { Text = Loc.GetString("faction-id-console-record-print") };
        print.OnPressed += _ => OnPrint?.Invoke();
        buttons.AddChild(_save);
        buttons.AddChild(print);
        AddChild(buttons);

        _edit = new TextEdit { VerticalExpand = true, HorizontalExpand = true, MinHeight = 120 };
        AddChild(_edit);

        _view = new RichTextLabel { VerticalAlignment = VAlignment.Top };
        _viewScroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, MinHeight = 120 };
        _viewScroll.AddChild(_view);
        AddChild(_viewScroll);
    }

    public void UpdateState(string content, bool editable)
    {
        _save.Visible = editable;
        _edit.Visible = editable;
        _viewScroll.Visible = !editable;

        // Don't throw away what someone is in the middle of writing.
        var dirty = Rope.Collapse(_edit.TextRope) != _lastContent;
        if (!dirty || content == _lastContent)
            _edit.TextRope = new Rope.Leaf(content);

        _view.SetMarkupPermissive(content);
        _lastContent = content;
    }
}
