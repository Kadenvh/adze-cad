using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Adze.Contracts.Models;
using Adze.UI.Rendering;

namespace Adze.UI.V2;

/// <summary>
/// One chat bubble in the conversation thread. Rendered as a <see cref="UserControl"/>
/// containing a body <see cref="RichTextBox"/> with markdown-formatted content
/// and an optional footer (token count, model).
///
/// v1.1 visual treatment:
///   - Rounded corners drawn in <see cref="OnPaint"/> via <see cref="GraphicsPath"/>.
///   - User bubble: solid accent, white text, right-aligned in the thread.
///   - Assistant bubble: paper-white card, dark text, subtle 1px hairline border.
///   - System bubble: very light amber notice band.
///   - Footer is right-aligned, secondary text colour, small.
///   - Vertical breathing room: 8px above/below; horizontal margin: 16px.
/// </summary>
public sealed class ChatMessageView : UserControl
{
    public enum MessageRole
    {
        User,
        Assistant,
        System
    }

    // Bubble corner radius — matches modern chat UIs without going full pill.
    private const int BubbleRadius = 12;
    // Max bubble width as a fraction of the host width.
    private const float MaxBubbleWidthRatio = 0.86f;

    private readonly RichTextBox _body;
    private readonly Label _footer;
    private readonly RoundedPanel _bubble;
    private bool _streamingActive;

    public MessageRole Role { get; }

    public ChatMessageView(MessageRole role, string text, string? footerText = null)
    {
        Role = role;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        // Outer padding gives the thread breathing room; horizontal asymmetry
        // is set per-role by ApplyRoleStyling.
        Padding = new Padding(16, 8, 16, 8);
        BackColor = UiPalette.SurfaceBackground;
        Margin = Padding.Empty;
        DoubleBuffered = true;

        _bubble = new RoundedPanel(BubbleRadius)
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0)
        };

        _body = new RichTextBox
        {
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.None,
            Font = new Font(UiPalette.FontFamily, UiPalette.ChatBodyFontSize),
            DetectUrls = true,
            TabStop = false,
            Width = 320,
            Height = 60,
            Margin = Padding.Empty
        };
        _body.ContentsResized += OnBodyContentsResized;

        _footer = new Label
        {
            Text = footerText ?? string.Empty,
            AutoSize = true,
            Font = new Font(UiPalette.FontFamily, UiPalette.FooterFontSize, FontStyle.Italic),
            Margin = new Padding(0, 6, 0, 0),
            Visible = !string.IsNullOrWhiteSpace(footerText),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = UiPalette.TextSecondary
        };

        ApplyRoleStyling();
        SetText(text ?? string.Empty);

        _bubble.Controls.Add(_body);
        if (_footer.Visible) _bubble.Controls.Add(_footer);
        Controls.Add(_bubble);
        // Re-flow on resize so the bubble width tracks the host.
        SizeChanged += (_, _) => RelayoutChildren();
        RelayoutChildren();

        // Theme tracking — re-apply role styling whenever the user flips
        // Light / Dark / System. Subscribe at construction (rather than
        // OnHandleCreated) so cached views off-screen still pick up the change.
        UiPalette.ModeChanged += OnPaletteModeChanged;
    }

    private void OnPaletteModeChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(ReapplyTheme)); } catch (InvalidOperationException) { }
            return;
        }
        ReapplyTheme();
    }

    private void ReapplyTheme()
    {
        BackColor = UiPalette.SurfaceBackground;
        ApplyRoleStyling();
        Invalidate(true);
        _bubble.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiPalette.ModeChanged -= OnPaletteModeChanged;
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Convenience overload that maps a <see cref="ChatEntry"/> to two views
    /// (user + assistant) — call from the conversation thread renderer.
    /// </summary>
    public static (ChatMessageView userView, ChatMessageView assistantView) FromEntry(ChatEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        var user = new ChatMessageView(MessageRole.User, entry.UserMessage);
        var asst = new ChatMessageView(MessageRole.Assistant, entry.AssistantMessage, entry.Footer);
        return (user, asst);
    }

    /// <summary>Replace the bubble body with the given markdown.</summary>
    public void SetText(string markdown)
    {
        _body.Clear();
        if (Role == MessageRole.Assistant || Role == MessageRole.System)
        {
            MarkdownToRichText.AppendRendered(_body, markdown);
        }
        else
        {
            _body.AppendText(markdown);
        }
        AdjustBodyHeight();
    }

    /// <summary>Append a streamed text chunk to the end of the body.</summary>
    public void AppendStreamChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        _streamingActive = true;
        _body.AppendText(chunk);
        AdjustBodyHeight();
    }

    /// <summary>Mark streaming complete; replace the buffered text with a fully formatted render.</summary>
    public void FinalizeStream(string finalMarkdown)
    {
        _streamingActive = false;
        SetText(finalMarkdown);
    }

    /// <summary>Update the per-message footer text.</summary>
    public void SetFooter(string text)
    {
        _footer.Text = text ?? string.Empty;
        _footer.Visible = !string.IsNullOrWhiteSpace(_footer.Text);
        if (_footer.Visible && !_bubble.Controls.Contains(_footer))
        {
            _bubble.Controls.Add(_footer);
        }
        RelayoutChildren();
    }

    public bool IsStreaming => _streamingActive;

    private void ApplyRoleStyling()
    {
        switch (Role)
        {
            case MessageRole.User:
                _bubble.FillColor = UiPalette.UserBubbleBackground;
                _bubble.BorderColor = UiPalette.UserBubbleBorder;
                _bubble.BorderWidth = 0;
                _body.BackColor = UiPalette.UserBubbleBackground;
                _body.ForeColor = UiPalette.UserBubbleForeground;
                _footer.ForeColor = Color.FromArgb(220, 220, 240);
                _bubble.Dock = DockStyle.Right;
                Padding = new Padding(64, 8, 16, 8);
                break;
            case MessageRole.Assistant:
                _bubble.FillColor = UiPalette.AssistantBubbleBackground;
                _bubble.BorderColor = UiPalette.AssistantBubbleBorder;
                _bubble.BorderWidth = 1;
                _body.BackColor = UiPalette.AssistantBubbleBackground;
                _body.ForeColor = UiPalette.AssistantBubbleForeground;
                _footer.ForeColor = UiPalette.TextSecondary;
                _bubble.Dock = DockStyle.Left;
                Padding = new Padding(16, 8, 64, 8);
                break;
            case MessageRole.System:
                _bubble.FillColor = UiPalette.SystemBubbleBackground;
                _bubble.BorderColor = UiPalette.SystemBubbleBorder;
                _bubble.BorderWidth = 1;
                _body.BackColor = UiPalette.SystemBubbleBackground;
                _body.ForeColor = UiPalette.SystemBubbleForeground;
                _footer.ForeColor = UiPalette.SystemBubbleForeground;
                _bubble.Dock = DockStyle.Left;
                Padding = new Padding(16, 8, 64, 8);
                break;
        }
    }

    private void OnBodyContentsResized(object? sender, ContentsResizedEventArgs e)
    {
        _body.Height = Math.Max(20, e.NewRectangle.Height + 4);
        RelayoutChildren();
    }

    private void AdjustBodyHeight()
    {
        // RichTextBox doesn't auto-size; force a sensible content-height.
        Size pref = TextRenderer.MeasureText(
            _body.Text.Length == 0 ? " " : _body.Text,
            _body.Font,
            new Size(_body.ClientSize.Width, int.MaxValue),
            TextFormatFlags.WordBreak);
        _body.Height = Math.Max(20, pref.Height + 6);
    }

    private void RelayoutChildren()
    {
        if (Width <= 0) return;
        int available = Width - Padding.Left - Padding.Right;
        int maxBubble = Math.Max(80, (int)(Width * MaxBubbleWidthRatio));
        int bubbleWidth = Math.Min(available, maxBubble);
        _bubble.MaximumSize = new Size(bubbleWidth, 0);
        _body.Width = bubbleWidth - _bubble.Padding.Left - _bubble.Padding.Right;
        _footer.MaximumSize = new Size(_body.Width, 0);
    }

    /// <summary>
    /// A <see cref="Panel"/> that paints a rounded-rect background + optional
    /// 1px border. We use this instead of a custom <see cref="Form.Region"/>
    /// because Region clipping rasterises text along the curve; painting the
    /// fill keeps text crisp.
    /// </summary>
    private sealed class RoundedPanel : Panel
    {
        private int _radius;
        public Color FillColor { get; set; } = Color.White;
        public Color BorderColor { get; set; } = Color.Transparent;
        public int BorderWidth { get; set; } = 0;

        public RoundedPanel(int radius)
        {
            _radius = radius;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = BuildRoundedPath(r, _radius);
            using (SolidBrush fill = new(FillColor))
            {
                g.FillPath(fill, path);
            }
            if (BorderWidth > 0 && BorderColor != Color.Transparent)
            {
                using Pen pen = new(BorderColor, BorderWidth);
                g.DrawPath(pen, path);
            }
            // Don't call base — we own the entire surface.
        }

        private static GraphicsPath BuildRoundedPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new();
            if (radius <= 0)
            {
                p.AddRectangle(r);
                p.CloseFigure();
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
