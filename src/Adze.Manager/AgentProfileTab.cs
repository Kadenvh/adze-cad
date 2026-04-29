using System.Drawing;
using System.Windows.Forms;

namespace Adze.Manager;

/// <summary>
/// Placeholder for Phase 2 of the v1.1 UI rebuild. Agent persona / behavior
/// knobs land here once we've validated which knobs actually change output
/// vs which are placebo (open question #3 in plans/ui-rebuild-v1.1.md).
/// </summary>
public sealed class AgentProfileTab : UserControl
{
    public AgentProfileTab()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 248, 250);
        Padding = new Padding(24, 24, 24, 24);

        var heading = new Label
        {
            Text = "Agent Profile",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 50, 70),
            Location = new Point(24, 24)
        };
        Controls.Add(heading);

        var body = new Label
        {
            Text =
                "Coming in v1.1+: agent persona sliders (verbosity, caution, creativity), " +
                "custom system instructions, and prompt format templates. " +
                "Phase 2 of the UI rebuild.\n\n" +
                "Deferred from this round to keep scope focused on Logs + Settings + Status. " +
                "See plans/ui-rebuild-v1.1.md (open question #3) for the current thinking on " +
                "which knobs actually change model behavior vs. which are placebo.",
            AutoSize = true,
            MaximumSize = new Size(620, 0),
            Location = new Point(24, 64),
            ForeColor = Color.FromArgb(70, 80, 100),
            Font = new Font("Segoe UI", 9.5F)
        };
        Controls.Add(body);
    }
}
