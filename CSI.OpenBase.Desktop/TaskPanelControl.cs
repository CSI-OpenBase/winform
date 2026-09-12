using System.Globalization;
using System.Text;

namespace CSI.OpenBase.Desktop;

internal sealed class TaskPanelControl : UserControl
{
    private static readonly Color TextColor = Color.FromArgb(31, 35, 40);
    private static readonly Color MutedTextColor = Color.FromArgb(87, 96, 106);
    private static readonly Color BorderColor = Color.FromArgb(216, 222, 228);
    private static readonly Color PanelColor = Color.FromArgb(246, 248, 250);
    private readonly Font _headerTitleFont = new("Segoe UI Semibold", 11F);
    private readonly Font _closeButtonFont = new("Segoe UI", 14F);
    private readonly Font _taskTitleFont = new("Segoe UI Semibold", 9F);
    private readonly Font _taskMetadataFont = new("Segoe UI", 8.25F);
    private readonly Label _summaryLabel;
    private readonly Label _placeholderLabel;
    private readonly Label _updatedLabel;
    private readonly Button _closeButton;
    private readonly BufferedFlowLayoutPanel _taskList;
    private readonly ToolTip _toolTip = new();
    private string? _taskFingerprint;

    public TaskPanelControl()
    {
        AccessibleName = "任务面板";
        AccessibleRole = AccessibleRole.Pane;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = PanelColor;
        Dock = DockStyle.Fill;
        MinimumSize = new Size(280, 0);

        var root = new TableLayoutPanel
        {
            BackColor = PanelColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            RowCount = 3,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

        var header = new TableLayoutPanel
        {
            BackColor = Color.White,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(16, 8, 10, 7),
            RowCount = 2,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));

        var title = new Label
        {
            AutoSize = true,
            Font = _headerTitleFont,
            ForeColor = TextColor,
            Margin = new Padding(0, 1, 0, 0),
            Text = "任务",
        };
        _summaryLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            Margin = new Padding(0),
            Text = "等待本地服务",
        };
        _closeButton = CreateQuietButton("×", 30);
        _closeButton.AccessibleName = "关闭任务面板";
        _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _closeButton.Font = _closeButtonFont;
        _closeButton.Margin = new Padding(4, 1, 0, 0);
        _closeButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        _toolTip.SetToolTip(_closeButton, "关闭任务面板");
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_summaryLabel, 0, 1);
        header.Controls.Add(_closeButton, 1, 0);
        header.SetRowSpan(_closeButton, 2);

        var content = new Panel
        {
            BackColor = PanelColor,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        _taskList = new BufferedFlowLayoutPanel
        {
            AccessibleName = "最近任务",
            AccessibleRole = AccessibleRole.List,
            AutoScroll = true,
            BackColor = PanelColor,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            Padding = new Padding(12, 12, 10, 4),
            TabStop = false,
            WrapContents = false,
        };
        _taskList.ClientSizeChanged += (_, _) => ResizeTaskItems();
        _placeholderLabel = new Label
        {
            AccessibleName = "任务面板状态",
            BackColor = PanelColor,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            Padding = new Padding(28),
            Text = "等待本地服务",
            TextAlign = ContentAlignment.MiddleCenter,
        };
        content.Controls.Add(_taskList);
        content.Controls.Add(_placeholderLabel);

        var footer = new TableLayoutPanel
        {
            BackColor = Color.White,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(14, 7, 14, 7),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _updatedLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = MutedTextColor,
            Text = "尚未更新",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        footer.Controls.Add(_updatedLabel, 0, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    public event EventHandler? CloseRequested;

    public void FocusContent()
    {
        var firstTask = _taskList.Controls.OfType<TaskItemControl>().FirstOrDefault();
        if (firstTask is not null)
        {
            firstTask.TabStop = true;
            if (firstTask.Focus())
            {
                _taskList.ScrollControlIntoView(firstTask);
                return;
            }
        }

        if (_closeButton.CanFocus)
        {
            _closeButton.Focus();
        }
    }

    public void ShowLoading(string message = "正在读取任务...")
    {
        _taskFingerprint = null;
        SetPanelStatus("正在连接", message);
        _updatedLabel.Text = "尚未更新";
        SetPlaceholder(message);
    }

    public void ShowDisconnected(string message)
    {
        _taskFingerprint = null;
        SetPanelStatus("本地服务未连接", message);
        _updatedLabel.Text = "尚未更新";
        SetPlaceholder(message);
    }

    public void ShowError(string message)
    {
        SetPanelStatus("任务状态暂不可用", message);
        _updatedLabel.Text = "更新失败";
        if (_taskList.Controls.Count == 0)
        {
            _taskFingerprint = null;
            SetPlaceholder(message);
        }
    }

    public void SetState(BackendTaskState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var summary = state.ActiveTaskCount > 0
            ? $"{state.ActiveTaskCount} 个进行中 · {state.Tasks.Count} 条记录"
            : $"无进行中任务 · {state.Tasks.Count} 条记录";
        SetPanelStatus(summary, "任务列表已更新");
        _updatedLabel.Text = $"更新于 {DateTime.Now:HH:mm:ss}";

        var fingerprint = BuildFingerprint(state);
        if (string.Equals(_taskFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _taskFingerprint = fingerprint;
        var scrollPosition = new Point(
            -_taskList.AutoScrollPosition.X,
            -_taskList.AutoScrollPosition.Y);
        _taskList.SuspendLayout();
        try
        {
            ReconcileTaskItems(state.Tasks);
        }
        finally
        {
            _taskList.ResumeLayout(performLayout: true);
        }

        if (state.Tasks.Count == 0)
        {
            SetPlaceholder("暂无任务记录");
        }
        else
        {
            _placeholderLabel.Visible = false;
            _taskList.Visible = true;
            _taskList.BringToFront();
            ResizeTaskItems();
            _taskList.AutoScrollPosition = scrollPosition;
            var focusedTask = _taskList.Controls
                .OfType<TaskItemControl>()
                .FirstOrDefault(control => control.Focused);
            if (focusedTask is not null)
            {
                _taskList.ScrollControlIntoView(focusedTask);
            }
        }

    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _headerTitleFont.Dispose();
            _closeButtonFont.Dispose();
            _taskTitleFont.Dispose();
            _taskMetadataFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Button CreateQuietButton(string text, int width)
    {
        return new Button
        {
            AutoSize = false,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            ForeColor = TextColor,
            Height = 30,
            Margin = new Padding(0),
            Text = text,
            UseVisualStyleBackColor = false,
            Width = width,
            FlatAppearance =
            {
                BorderColor = BorderColor,
                BorderSize = 1,
            },
        };
    }

    private static string BuildFingerprint(BackendTaskState state)
    {
        var value = new StringBuilder();
        value.Append(state.ActiveTaskCount).Append('|').Append(state.LatestTaskId);
        foreach (var task in state.Tasks)
        {
            value.Append('\u001e')
                .Append(task.Id).Append('\u001f')
                .Append(task.Kind).Append('\u001f')
                .Append(task.Status).Append('\u001f')
                .Append(task.VideoId).Append('\u001f')
                .Append(task.Message).Append('\u001f')
                .Append(task.CreatedAt).Append('\u001f')
                .Append(task.StartedAt).Append('\u001f')
                .Append(task.FinishedAt);
        }

        return value.ToString();
    }

    private void SetPanelStatus(string summary, string detail)
    {
        _summaryLabel.Text = summary;
        var description = $"{summary}。{detail.ReplaceLineEndings(" ")}";
        if (string.Equals(AccessibleDescription, description, StringComparison.Ordinal))
        {
            return;
        }

        AccessibleDescription = description;
        if (IsHandleCreated)
        {
            AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
        }
    }

    private void ReconcileTaskItems(IReadOnlyList<BackendTaskItem> tasks)
    {
        var existing = _taskList.Controls
            .OfType<TaskItemControl>()
            .ToDictionary(control => control.TaskId);
        var retained = new HashSet<TaskItemControl>();
        var focusedControl = existing.Values.FirstOrDefault(control => control.Focused);
        var focusedTaskId = focusedControl?.TaskId;
        var scaleFactor = Math.Max(DeviceDpi, 96) / 96F;

        for (var index = 0; index < tasks.Count; index++)
        {
            var task = tasks[index];
            if (!existing.TryGetValue(task.Id, out var control))
            {
                control = new TaskItemControl(
                    task,
                    _toolTip,
                    _taskTitleFont,
                    _taskMetadataFont,
                    scaleFactor);
                _taskList.Controls.Add(control);
            }
            else
            {
                control.UpdateTask(task);
            }

            retained.Add(control);
            _taskList.Controls.SetChildIndex(control, index);
        }

        foreach (var control in existing.Values.Where(control => !retained.Contains(control)))
        {
            ClearToolTips(control);
            _taskList.Controls.Remove(control);
            control.Dispose();
        }

        var focusTarget = focusedTaskId is null
            ? null
            : retained.FirstOrDefault(control => control.TaskId == focusedTaskId);
        focusTarget ??= _taskList.Controls.OfType<TaskItemControl>().FirstOrDefault();
        foreach (var control in retained)
        {
            control.TabStop = ReferenceEquals(control, focusTarget);
        }

        if (focusedControl is not null &&
            !retained.Contains(focusedControl) &&
            focusTarget is not null)
        {
            focusTarget.Focus();
            _taskList.ScrollControlIntoView(focusTarget);
        }
    }

    private void SetPlaceholder(string message)
    {
        ClearTaskItems();
        _taskList.Visible = false;
        _placeholderLabel.Text = message.ReplaceLineEndings(" ");
        _placeholderLabel.Visible = true;
        _placeholderLabel.BringToFront();
    }

    private void ClearTaskItems()
    {
        while (_taskList.Controls.Count > 0)
        {
            var control = _taskList.Controls[0];
            ClearToolTips(control);
            _taskList.Controls.RemoveAt(0);
            control.Dispose();
        }
    }

    private void ClearToolTips(Control control)
    {
        _toolTip.SetToolTip(control, null);
        foreach (Control child in control.Controls)
        {
            ClearToolTips(child);
        }
    }

    private void ResizeTaskItems()
    {
        if (!_taskList.Visible || _taskList.ClientSize.Width <= 0)
        {
            return;
        }

        var itemWidth = Math.Max(
            ScaleLogical(248),
            _taskList.ClientSize.Width -
            _taskList.Padding.Horizontal -
            2);
        foreach (Control control in _taskList.Controls)
        {
            control.Width = itemWidth;
        }
    }

    private int ScaleLogical(int value)
    {
        return (int)Math.Round(value * Math.Max(DeviceDpi, 96) / 96F);
    }

    private sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
    {
        public BufferedFlowLayoutPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private sealed class TaskItemControl : TableLayoutPanel
    {
        private static readonly Color ActiveColor = Color.FromArgb(9, 105, 218);
        private readonly ToolTip _toolTip;
        private readonly Label _titleLabel;
        private readonly Label _statusLabel;
        private readonly Label _messageLabel;
        private readonly Label _metadataLabel;
        private readonly float _initialScaleFactor;
        private BackendTaskItem _task;

        public TaskItemControl(
            BackendTaskItem task,
            ToolTip toolTip,
            Font titleFont,
            Font metadataFont,
            float scaleFactor)
        {
            _task = task;
            _toolTip = toolTip;
            _initialScaleFactor = scaleFactor;
            SetStyle(ControlStyles.Selectable, true);
            AccessibleRole = AccessibleRole.ListItem;
            ColumnCount = 2;
            Height = Scale(104);
            Margin = new Padding(0, 0, 0, Scale(8));
            RowCount = 1;
            TabStop = false;
            Width = Scale(280);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0F));
            RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var body = new TableLayoutPanel
            {
                BackColor = Color.White,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(Scale(11), Scale(8), Scale(11), Scale(8)),
                RowCount = 3,
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(24)));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(20)));

            var heading = new TableLayoutPanel
            {
                BackColor = Color.White,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                RowCount = 1,
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _titleLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = titleFont,
                ForeColor = TextColor,
                Margin = new Padding(0, Scale(2), Scale(8), 0),
                UseMnemonic = false,
            };
            _statusLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(Scale(5), Scale(2), Scale(5), Scale(2)),
            };
            heading.Controls.Add(_titleLabel, 0, 0);
            heading.Controls.Add(_statusLabel, 1, 0);

            _messageLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                ForeColor = MutedTextColor,
                Margin = new Padding(0, Scale(4), 0, Scale(2)),
                UseMnemonic = false,
            };
            _metadataLabel = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = metadataFont,
                ForeColor = Color.FromArgb(106, 115, 125),
                Margin = new Padding(0, Scale(2), 0, 0),
                UseMnemonic = false,
            };
            body.Controls.Add(heading, 0, 0);
            body.Controls.Add(_messageLabel, 0, 1);
            body.Controls.Add(_metadataLabel, 0, 2);
            Controls.Add(body, 0, 0);
            MouseDown += (_, _) => FocusItem();
            AttachFocusOnClick(body);
            UpdateTask(task);
        }

        public long TaskId => _task.Id;

        public void UpdateTask(BackendTaskItem task)
        {
            _task = task;
            var kindLabel = KindLabel(task.Kind);
            var statusLabel = StatusLabel(task.Status);
            var message = string.IsNullOrWhiteSpace(task.Message)
                ? DefaultMessage(task.Status)
                : task.Message;
            var metadata = Metadata(task);

            SetAccessibleText(
                $"任务 {task.Id}，{kindLabel}，{statusLabel}",
                $"{message}。{metadata}");
            _titleLabel.Text = $"#{task.Id}  {kindLabel}";
            _statusLabel.Text = statusLabel;
            _statusLabel.BackColor = StatusBackColor(task.Status);
            _statusLabel.ForeColor = StatusTextColor(task.Status);
            _messageLabel.Text = message;
            _metadataLabel.Text = metadata;
            _toolTip.SetToolTip(_titleLabel, _titleLabel.Text);
            _toolTip.SetToolTip(_messageLabel, message);
            _toolTip.SetToolTip(_metadataLabel, metadata);
            UpdateFrame();
        }

        protected override void OnGotFocus(EventArgs eventArgs)
        {
            base.OnGotFocus(eventArgs);
            if (Parent is not null)
            {
                foreach (var sibling in Parent.Controls.OfType<TaskItemControl>())
                {
                    sibling.TabStop = ReferenceEquals(sibling, this);
                }
            }

            UpdateFrame();
            AccessibilityNotifyClients(AccessibleEvents.Focus, -1);
        }

        protected override void OnLostFocus(EventArgs eventArgs)
        {
            base.OnLostFocus(eventArgs);
            UpdateFrame();
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (Focused || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            using var borderPen = new Pen(BorderColor, Scale(1));
            eventArgs.Graphics.DrawRectangle(
                borderPen,
                0,
                0,
                ClientSize.Width - 1,
                ClientSize.Height - 1);
            if (_task.IsActive)
            {
                using var activeBrush = new SolidBrush(ActiveColor);
                eventArgs.Graphics.FillRectangle(
                    activeBrush,
                    0,
                    0,
                    Scale(3),
                    ClientSize.Height);
            }
        }

        protected override void OnDpiChangedAfterParent(EventArgs eventArgs)
        {
            base.OnDpiChangedAfterParent(eventArgs);
            UpdateFrame();
        }

        protected override void OnKeyDown(KeyEventArgs eventArgs)
        {
            base.OnKeyDown(eventArgs);
            var direction = eventArgs.KeyCode switch
            {
                Keys.Up => -1,
                Keys.Down => 1,
                Keys.PageUp => -PageSize(),
                Keys.PageDown => PageSize(),
                Keys.Home => int.MinValue,
                Keys.End => int.MaxValue,
                _ => 0,
            };
            if (direction == 0 || !MoveFocus(direction))
            {
                return;
            }

            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return (keyData & Keys.KeyCode) is
                    Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End ||
                base.IsInputKey(keyData);
        }

        private void AttachFocusOnClick(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                child.MouseDown += (_, _) => FocusItem();
                AttachFocusOnClick(child);
            }
        }

        private void FocusItem()
        {
            TabStop = true;
            Focus();
            if (Parent is ScrollableControl scrollable)
            {
                scrollable.ScrollControlIntoView(this);
            }
        }

        private int PageSize()
        {
            return Parent is null
                ? 1
                : Math.Max(1, Parent.ClientSize.Height / Math.Max(Height, 1) - 1);
        }

        private bool MoveFocus(int direction)
        {
            if (Parent is not ScrollableControl scrollable)
            {
                return false;
            }

            var siblings = Parent.Controls.OfType<TaskItemControl>().ToList();
            var currentIndex = siblings.IndexOf(this);
            if (currentIndex < 0)
            {
                return false;
            }

            var targetIndex = direction switch
            {
                int.MinValue => 0,
                int.MaxValue => siblings.Count - 1,
                _ => Math.Clamp(currentIndex + direction, 0, siblings.Count - 1),
            };
            var target = siblings[targetIndex];
            if (ReferenceEquals(target, this))
            {
                return false;
            }

            TabStop = false;
            target.TabStop = true;
            target.Focus();
            scrollable.ScrollControlIntoView(target);
            return true;
        }

        private void UpdateFrame()
        {
            BackColor = Focused
                ? SystemColors.Highlight
                : Color.White;
            Padding = new Padding(Scale(3));
            Invalidate();
        }

        private void SetAccessibleText(string name, string description)
        {
            var nameChanged = !string.Equals(AccessibleName, name, StringComparison.Ordinal);
            var descriptionChanged = !string.Equals(
                AccessibleDescription,
                description,
                StringComparison.Ordinal);
            AccessibleName = name;
            AccessibleDescription = description;
            if (!IsHandleCreated)
            {
                return;
            }

            if (nameChanged)
            {
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
            }

            if (descriptionChanged)
            {
                AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            }
        }

        private int Scale(int value)
        {
            var scaleFactor = IsHandleCreated
                ? Math.Max(DeviceDpi, 96) / 96F
                : _initialScaleFactor;
            return (int)Math.Round(value * scaleFactor);
        }

        private static string KindLabel(string kind)
        {
            return kind switch
            {
                "authorize" => "账号授权",
                "export" => "数据导出",
                "sync_videos" => "主页同步",
                "comments" => "评论导出",
                _ => "任务",
            };
        }

        private static string StatusLabel(string status)
        {
            return status switch
            {
                "queued" => "等待中",
                "running" => "进行中",
                "succeeded" => "已完成",
                "partial" => "部分完成",
                "blocked" => "需要处理",
                "failed" => "失败",
                "interrupted" => "已中断",
                _ => "未知状态",
            };
        }

        private static string DefaultMessage(string status)
        {
            return status switch
            {
                "queued" => "等待执行",
                "running" => "正在执行",
                "succeeded" => "任务已完成",
                "partial" => "任务部分完成",
                "blocked" => "任务需要处理",
                "failed" => "任务执行失败",
                "interrupted" => "任务已中断",
                _ => "暂无结果信息",
            };
        }

        private static string Metadata(BackendTaskItem task)
        {
            var rawTime = task.FinishedAt ?? task.StartedAt ?? task.CreatedAt;
            var time = FormatTime(rawTime);
            return string.IsNullOrWhiteSpace(task.VideoId)
                ? time
                : $"视频 {task.VideoId} · {time}";
        }

        private static string FormatTime(string? rawTime)
        {
            if (DateTimeOffset.TryParse(
                rawTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
            {
                return parsed.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.CurrentCulture);
            }

            return string.IsNullOrWhiteSpace(rawTime) ? "时间未知" : rawTime;
        }

        private static Color StatusBackColor(string status)
        {
            return status switch
            {
                "running" or "queued" => Color.FromArgb(226, 239, 250),
                "succeeded" => Color.FromArgb(223, 244, 233),
                "partial" or "blocked" or "interrupted" => Color.FromArgb(255, 240, 213),
                "failed" => Color.FromArgb(251, 227, 224),
                _ => Color.FromArgb(232, 235, 237),
            };
        }

        private static Color StatusTextColor(string status)
        {
            return status switch
            {
                "running" or "queued" => Color.FromArgb(34, 91, 143),
                "succeeded" => Color.FromArgb(21, 94, 70),
                "partial" or "blocked" or "interrupted" => Color.FromArgb(128, 82, 16),
                "failed" => Color.FromArgb(141, 46, 39),
                _ => MutedTextColor,
            };
        }
    }
}
