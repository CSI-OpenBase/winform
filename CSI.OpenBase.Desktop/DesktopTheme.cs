namespace CSI.OpenBase.Desktop;

internal static class DesktopTheme
{
    public static readonly Color Ink = Color.FromArgb(36, 33, 42);
    public static readonly Color Muted = Color.FromArgb(101, 94, 98);
    public static readonly Color Paper = Color.FromArgb(255, 248, 232);
    public static readonly Color Surface = Color.FromArgb(255, 253, 246);
    public static readonly Color Yellow = Color.FromArgb(255, 218, 69);
    public static readonly Color Coral = Color.FromArgb(255, 134, 110);
    public static readonly Color Lilac = Color.FromArgb(236, 228, 255);
    public static readonly Color Blue = Color.FromArgb(52, 84, 209);
    public static readonly Color Danger = Color.FromArgb(168, 45, 62);

    public static ToolStripRenderer CreateMenuRenderer() =>
        new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Paper;
        public override Color MenuBorder => Ink;
        public override Color MenuItemBorder => Ink;
        public override Color MenuItemSelected => Yellow;
        public override Color MenuItemSelectedGradientBegin => Yellow;
        public override Color MenuItemSelectedGradientEnd => Yellow;
        public override Color MenuItemPressedGradientBegin => Yellow;
        public override Color MenuItemPressedGradientMiddle => Yellow;
        public override Color MenuItemPressedGradientEnd => Yellow;
        public override Color ImageMarginGradientBegin => Paper;
        public override Color ImageMarginGradientMiddle => Paper;
        public override Color ImageMarginGradientEnd => Paper;
        public override Color CheckBackground => Yellow;
        public override Color CheckSelectedBackground => Yellow;
        public override Color SeparatorDark => Ink;
        public override Color SeparatorLight => Paper;
    }
}
