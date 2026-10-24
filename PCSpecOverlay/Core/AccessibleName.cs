using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PCSpecOverlay.Core;

/// <summary>
/// Screen readers name a control after its content only when that content is plain text. Switches here
/// carry a title and a description, so they'd be announced without a name. With
/// <c>AccessibleName.FromContent="True"</c>, a control without an explicit name is named after the first
/// text in its content (its title); the description is exposed as help text.
/// </summary>
public static class AccessibleName
{
    public static readonly DependencyProperty FromContentProperty = DependencyProperty.RegisterAttached(
        "FromContent", typeof(bool), typeof(AccessibleName), new PropertyMetadata(false, OnChanged));

    public static bool GetFromContent(DependencyObject d) => (bool)d.GetValue(FromContentProperty);
    public static void SetFromContent(DependencyObject d, bool value) => d.SetValue(FromContentProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContentControl c && (bool)e.NewValue) c.Loaded += (_, _) => Apply(c);
    }

    private static void Apply(ContentControl c)
    {
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(c)) || c.Content is string) return;
        var texts = new List<string>();
        Collect(c.Content as DependencyObject, texts);
        if (texts.Count == 0) return;
        AutomationProperties.SetName(c, texts[0]);
        if (texts.Count > 1 && string.IsNullOrEmpty(AutomationProperties.GetHelpText(c)))
            AutomationProperties.SetHelpText(c, string.Join(" ", texts.Skip(1)));
    }

    private static void Collect(DependencyObject? node, List<string> texts)
    {
        if (node is null) return;
        if (node is TextBlock { Text.Length: > 0 } tb) texts.Add(tb.Text);
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Collect(child, texts);
    }
}
