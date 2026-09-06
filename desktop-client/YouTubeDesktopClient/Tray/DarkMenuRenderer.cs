// Tray/DarkMenuRenderer.cs
//
// The tray menu is a WinForms ContextMenuStrip (see TrayIconService) — WPF theming
// can't reach it, so in dark mode it rendered as system-white. This gives it a
// dark palette via a ProfessionalColorTable + a text-colour override. Light mode
// keeps the stock renderer. WinForms types are fully qualified here because the
// csproj removes the implicit System.Windows.Forms / System.Drawing usings.

using System.Drawing;
using System.Windows.Forms;

namespace YouTubeDesktopClient.Tray;

internal sealed class DarkMenuColorTable : ProfessionalColorTable
{
    private static readonly Color Surface = Color.FromArgb(0x1E, 0x1E, 0x1E);
    private static readonly Color Hover = Color.FromArgb(0x33, 0x33, 0x33);
    private static readonly Color Border = Color.FromArgb(0x2E, 0x2E, 0x2E);

    public DarkMenuColorTable() => UseSystemColors = false;

    public override Color ToolStripDropDownBackground => Surface;
    public override Color ImageMarginGradientBegin => Surface;
    public override Color ImageMarginGradientMiddle => Surface;
    public override Color ImageMarginGradientEnd => Surface;
    public override Color MenuBorder => Border;
    public override Color MenuItemBorder => Hover;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Surface;
    public override Color MenuItemPressedGradientEnd => Surface;
    public override Color SeparatorDark => Border;
    public override Color SeparatorLight => Border;
    public override Color CheckBackground => Hover;
    public override Color CheckSelectedBackground => Hover;
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Text = Color.FromArgb(0xED, 0xED, 0xED);
    private static readonly Color TextDisabled = Color.FromArgb(0x7A, 0x7A, 0x7A);

    public DarkMenuRenderer() : base(new DarkMenuColorTable()) => RoundedEdges = false;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : TextDisabled;
        base.OnRenderItemText(e);
    }
}

/// <summary>Applies a dark or default renderer to a menu, and re-applies on theme change.</summary>
internal static class TrayMenuTheme
{
    public static void Apply(ToolStrip menu)
    {
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.Renderer = Themes.ThemeManager.IsDark
            ? new DarkMenuRenderer()
            : new ToolStripProfessionalRenderer();
    }
}
