using Nexer.Ui;

namespace Nexer.Demo;

/// <summary>Nested split panes (Jexer <c>TSplitPane</c>): a list beside two stacked text areas.</summary>
public partial class DemoSplitPaneWindow : Window
{
    private static readonly string[] Facts =
    [
        "Mercury is the smallest planet and the closest to the Sun.",
        "Venus is the hottest planet, with a thick carbon dioxide atmosphere.",
        "Earth is the only planet known to support life.",
        "Mars has the largest volcano in the solar system, Olympus Mons.",
        "Jupiter is the largest planet; its Great Red Spot is a giant storm.",
        "Saturn has the most spectacular ring system.",
        "Uranus rotates on its side.",
        "Neptune has the strongest winds in the solar system.",
    ];

    /// <summary>Creates the window.</summary>
    public DemoSplitPaneWindow()
    {
        InitializeComponent();
    }

    private void OnPlanetChanged(object? sender, EventArgs e)
    {
        int i = Planets.SelectedIndex;
        if (i >= 0 && i < Facts.Length) Details.Content = Planets.SelectedItem + ": " + Facts[i];
    }
}
