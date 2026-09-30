using Godot;

namespace VerticalDistrict.Game;

public partial class Main
{
    private const int DesignTextSize = 15;
    private const string OriginalFontSize = "district_original_font_size";
    private const string OriginalDialogSize = "district_original_dialog_size";
    private const string OriginalDialogMinimum = "district_original_dialog_minimum";
    private int _interfaceTextSize = DesignTextSize;
    private SceneTree? _textScaleTree;

    private void InitializeInterfaceTextScaling()
    {
        if (_textScaleTree == null)
        {
            _textScaleTree = GetTree();
            _textScaleTree.NodeAdded += ScaleAddedInterfaceNode;
        }
        ApplyInterfaceTextSize(_interfaceTextSize);
    }

    /// <summary>Scales original design sizes, never the previous scaled result.</summary>
    private void ApplyInterfaceTextSize(int size)
    {
        _interfaceTextSize = Math.Clamp(size, 14, 20);
        Theme.DefaultFontSize = _interfaceTextSize;
        foreach (var type in new[] { "Label", "Button", "LineEdit", "TextEdit", "CodeEdit", "OptionButton",
                     "CheckBox", "CheckButton", "LinkButton", "PopupMenu", "ItemList", "Tree", "TooltipLabel" })
            Theme.SetFontSize("font_size", type, _interfaceTextSize);
        foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size", "mono_font_size" })
            Theme.SetFontSize(key, "RichTextLabel", _interfaceTextSize);
        Theme.SetFontSize("title_font_size", "Window", _interfaceTextSize);
        ScaleInterfaceBranch(this);
    }

    private void ScaleAddedInterfaceNode(Node node)
    {
        if (IsAncestorOf(node)) ScaleInterfaceBranch(node);
    }

    private void ScaleInterfaceBranch(Node node)
    {
        if (node is Control control && control.HasThemeFontSizeOverride("font_size"))
        {
            if (!control.HasMeta(OriginalFontSize))
                control.SetMeta(OriginalFontSize, control.GetThemeFontSize("font_size"));
            var original = control.GetMeta(OriginalFontSize).AsInt32();
            control.AddThemeFontSizeOverride("font_size", ScaledTextSize(original));
        }
        if (node is AcceptDialog dialog)
        {
            // Windows can have their own theme boundary. Explicitly share the scaled theme.
            dialog.Theme = Theme;
            ScaleDialogBounds(dialog);
        }
        foreach (var child in node.GetChildren(includeInternal: true)) ScaleInterfaceBranch(child);
    }

    private int ScaledTextSize(int original) => Math.Max(1,
        (int)Math.Round(original * (double)_interfaceTextSize / DesignTextSize, MidpointRounding.AwayFromZero));

    private void ScaleDialogBounds(AcceptDialog dialog)
    {
        if (!dialog.HasMeta(OriginalDialogSize))
        {
            dialog.SetMeta(OriginalDialogSize, dialog.Size);
            dialog.SetMeta(OriginalDialogMinimum, dialog.MinSize);
        }
        var original = dialog.GetMeta(OriginalDialogSize).AsVector2I();
        var minimum = dialog.GetMeta(OriginalDialogMinimum).AsVector2I();
        var available = GetViewportRect().Size - new Vector2(32, 64);
        var maximumWidth = Math.Max(320, (int)available.X);
        var maximumHeight = Math.Max(240, (int)available.Y);
        dialog.MinSize = new Vector2I(Math.Min(maximumWidth, ScaledTextSize(minimum.X)),
            Math.Min(maximumHeight, ScaledTextSize(minimum.Y)));
        dialog.Size = new Vector2I(Math.Min(maximumWidth, ScaledTextSize(original.X)),
            Math.Min(maximumHeight, ScaledTextSize(original.Y)));
    }

    /// <summary>Viewport smoke helper: explicit labels, heading hierarchy, and new controls.</summary>
    private void VerifyInterfaceTextScaling()
    {
        var previous = _interfaceTextSize;
        ApplyInterfaceTextSize(20);
        if (Theme.DefaultFontSize != 20 || _funds.GetThemeFontSize("font_size") != 33
            || _summary.GetThemeFontSize("font_size") != 16)
            throw new InvalidOperationException("Text scaling did not resize existing overrides and preserve heading hierarchy.");
        var dynamicLabel = Text("Dynamic text scaling check", 14);
        _facilityBody.AddChild(dynamicLabel);
        if (dynamicLabel.GetThemeFontSize("font_size") != 19)
            throw new InvalidOperationException("Dynamically created interface text did not inherit the selected scale.");
        ApplyInterfaceTextSize(14);
        if (dynamicLabel.GetThemeFontSize("font_size") != 13 || _funds.GetThemeFontSize("font_size") != 23)
            throw new InvalidOperationException("Text scaling compounded rounding instead of preserving original sizes.");
        _facilityBody.RemoveChild(dynamicLabel);
        dynamicLabel.QueueFree();
        ApplyInterfaceTextSize(previous);
    }

    public override void _ExitTree()
    {
        if (_textScaleTree != null && GodotObject.IsInstanceValid(_textScaleTree))
            _textScaleTree.NodeAdded -= ScaleAddedInterfaceNode;
    }
}
