using System.Diagnostics;
using System.Reflection;

namespace CSI.OpenBase.Desktop;

internal sealed class AboutDialog : Form
{
    internal const string Author = "CSI OpenBase contributors";
    internal const string GitHubUrl = "https://github.com/CSI-OpenBase/winform";

    private static readonly Color TextColor = Color.FromArgb(31, 35, 40);
    private static readonly Color MutedTextColor = Color.FromArgb(87, 96, 106);
    private static readonly Color BorderColor = Color.FromArgb(216, 222, 228);
    private static readonly Color AccentColor = Color.FromArgb(9, 105, 218);

    public AboutDialog()
    {
        Text = "关于 CSI OpenBase";
        AccessibleName = "关于 CSI OpenBase";
        BackColor = Color.White;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 330);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var applicationInfo = ReadApplicationInfo();
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 24, 28, 22),
            RowCount = 3,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 22),
        };
        heading.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = TextColor,
            Text = "CSI OpenBase",
        });
        heading.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = MutedTextColor,
            Margin = new Padding(0, 5, 0, 0),
            Text = "开放的创作者数据采集、归档与基础分析工具",
        });
        layout.Controls.Add(heading, 0, 0);

        var details = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
            RowCount = 4,
        };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88F));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        AddDetail(details, 0, "作者", Author);
        AddGitHubDetail(details, 1);
        AddDetail(details, 2, "版本", applicationInfo.Version);
        AddDetail(details, 3, "更新日期", applicationInfo.UpdateDate);
        layout.Controls.Add(details, 0, 1);

        var closeButton = new Button
        {
            Anchor = AnchorStyles.Right,
            BackColor = AccentColor,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            Height = 32,
            Margin = new Padding(0, 18, 0, 0),
            Text = "关闭",
            UseVisualStyleBackColor = false,
            Width = 88,
        };
        closeButton.FlatAppearance.BorderColor = AccentColor;
        closeButton.FlatAppearance.BorderSize = 1;
        layout.Controls.Add(closeButton, 0, 2);

        AcceptButton = closeButton;
        CancelButton = closeButton;
        Controls.Add(layout);
    }

    internal static (string Version, string UpdateDate) ReadApplicationInfo()
    {
        var assembly = typeof(AboutDialog).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString(3) ?? "unknown";
        }

        var executablePath = Environment.ProcessPath;
        var updateTime = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(executablePath), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;
        var updateDate = BeijingTime.Convert(updateTime).ToString("yyyy-MM-dd");
        return (version, updateDate);
    }

    private static void AddDetail(
        TableLayoutPanel details,
        int row,
        string label,
        string value)
    {
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
        details.Controls.Add(CreateDetailLabel(label, MutedTextColor), 0, row);
        details.Controls.Add(CreateDetailLabel(value, TextColor), 1, row);
    }

    private static void AddGitHubDetail(TableLayoutPanel details, int row)
    {
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
        details.Controls.Add(CreateDetailLabel("GitHub", MutedTextColor), 0, row);
        var link = new LinkLabel
        {
            AccessibleDescription = "在默认浏览器中打开 CSI OpenBase WinForms GitHub 仓库",
            ActiveLinkColor = AccentColor,
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            LinkColor = AccentColor,
            Text = GitHubUrl,
            VisitedLinkColor = AccentColor,
        };
        link.LinkClicked += (_, _) => OpenGitHub();
        details.Controls.Add(link, 1, row);
    }

    private static Label CreateDetailLabel(string text, Color color)
    {
        return new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = color,
            Text = text,
        };
    }

    private static void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法打开 GitHub。\r\n\r\n{GitHubUrl}\r\n\r\n{exception.Message}",
                "打开 GitHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
