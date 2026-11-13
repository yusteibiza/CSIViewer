using System.Reflection;
using CSIViewer.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CSIViewer.Presentation;

/// <summary>Localizes static XAML properties without recreating pages, sessions or input controls.</summary>
public static class Translate
{
    private static readonly List<WeakReference<Target>> Targets = [];
    private static readonly DependencyProperty TargetsProperty = DependencyProperty.RegisterAttached("Targets", typeof(object), typeof(Translate), new PropertyMetadata(null));
    static Translate() => Localization.Changed += (_, _) =>
    {
        Targets.RemoveAll(x => !x.TryGetTarget(out _));
        foreach (var weak in Targets.ToArray()) if (weak.TryGetTarget(out var target)) target.Update();
    };

    public static void Apply(FrameworkElement root, string code)
    {
        root.Language = Localization.Normalize(code);
        if (!Localization.Use(code)) return;
        RefreshBindings(root);
        if (root.XamlRoot is { } xamlRoot)
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot)) RefreshBindings(popup);
    }

    private static void RefreshBindings(DependencyObject node)
    {
        // Reevaluate localized computed model labels. Keep bindings themselves,
        // input contents, selection, remote video and navigation cache intact.
        if (node is TextBlock text && text.GetBindingExpression(TextBlock.TextProperty) is { } binding)
            text.SetBinding(TextBlock.TextProperty, binding.ParentBinding);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) RefreshBindings(VisualTreeHelper.GetChild(node, i));
    }

    private sealed class Target(DependencyObject owner, DependencyProperty property, string source)
    {
        private string _source = source;
        private bool _writing;
        private TextBlock? _comboLabel;
        public void Changed()
        {
            if (_writing || owner.GetValue(property) is not string current) return;
            _source = Localization.Source(current);
            Update();
        }
        public void Update()
        {
            _writing = true;
            try
            {
                // ComboBox caches string selection-box content. Keep an element
                // as content so the selected label refreshes without changing
                // selection or firing theme/settings handlers.
                if (owner is ComboBoxItem && property == ContentControl.ContentProperty)
                {
                    if (_comboLabel is null)
                    {
                        _comboLabel = new TextBlock();
                        owner.SetValue(property, _comboLabel);
                    }
                    _comboLabel.Text = Localization.Text(_source);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(owner, _comboLabel.Text);
                }
                else owner.SetValue(property, Localization.Text(_source));
            }
            finally { _writing = false; }
        }
    }

    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(name, typeof(string), typeof(Translate), new PropertyMetadata(null, OnSourceChanged));
    private static void OnSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string source) return;
        var name = Names[e.Property];
        var property = name switch
        {
            "ToolTip" => ToolTipService.ToolTipProperty,
            "AutomationName" => Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty,
            _ => owner.GetType().GetProperty(name + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null) as DependencyProperty
        };
        if (property is null) return;
        var owned = owner.GetValue(TargetsProperty) as List<Target>;
        if (owned is null) { owned = []; owner.SetValue(TargetsProperty, owned); }
        var target = new Target(owner, property, source);
        owned.Add(target);
        Targets.Add(new(target));
        owner.RegisterPropertyChangedCallback(property, (_, _) => target.Changed());
        target.Update();
    }

    public static readonly DependencyProperty TextProperty = Register("Text");
    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);
    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static readonly DependencyProperty ContentProperty = Register("Content");
    public static void SetContent(DependencyObject o, string v) => o.SetValue(ContentProperty, v);
    public static string GetContent(DependencyObject o) => (string)o.GetValue(ContentProperty);
    public static readonly DependencyProperty HeaderProperty = Register("Header");
    public static void SetHeader(DependencyObject o, string v) => o.SetValue(HeaderProperty, v);
    public static string GetHeader(DependencyObject o) => (string)o.GetValue(HeaderProperty);
    public static readonly DependencyProperty OnContentProperty = Register("OnContent");
    public static void SetOnContent(DependencyObject o, string v) => o.SetValue(OnContentProperty, v);
    public static string GetOnContent(DependencyObject o) => (string)o.GetValue(OnContentProperty);
    public static readonly DependencyProperty OffContentProperty = Register("OffContent");
    public static void SetOffContent(DependencyObject o, string v) => o.SetValue(OffContentProperty, v);
    public static string GetOffContent(DependencyObject o) => (string)o.GetValue(OffContentProperty);
    public static readonly DependencyProperty PlaceholderTextProperty = Register("PlaceholderText");
    public static void SetPlaceholderText(DependencyObject o, string v) => o.SetValue(PlaceholderTextProperty, v);
    public static string GetPlaceholderText(DependencyObject o) => (string)o.GetValue(PlaceholderTextProperty);
    public static readonly DependencyProperty TitleProperty = Register("Title");
    public static void SetTitle(DependencyObject o, string v) => o.SetValue(TitleProperty, v);
    public static string GetTitle(DependencyObject o) => (string)o.GetValue(TitleProperty);
    public static readonly DependencyProperty MessageProperty = Register("Message");
    public static void SetMessage(DependencyObject o, string v) => o.SetValue(MessageProperty, v);
    public static string GetMessage(DependencyObject o) => (string)o.GetValue(MessageProperty);
    public static readonly DependencyProperty PrimaryButtonTextProperty = Register("PrimaryButtonText");
    public static void SetPrimaryButtonText(DependencyObject o, string v) => o.SetValue(PrimaryButtonTextProperty, v);
    public static string GetPrimaryButtonText(DependencyObject o) => (string)o.GetValue(PrimaryButtonTextProperty);
    public static readonly DependencyProperty SecondaryButtonTextProperty = Register("SecondaryButtonText");
    public static void SetSecondaryButtonText(DependencyObject o, string v) => o.SetValue(SecondaryButtonTextProperty, v);
    public static string GetSecondaryButtonText(DependencyObject o) => (string)o.GetValue(SecondaryButtonTextProperty);
    public static readonly DependencyProperty CloseButtonTextProperty = Register("CloseButtonText");
    public static void SetCloseButtonText(DependencyObject o, string v) => o.SetValue(CloseButtonTextProperty, v);
    public static string GetCloseButtonText(DependencyObject o) => (string)o.GetValue(CloseButtonTextProperty);
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");
    public static void SetToolTip(DependencyObject o, string v) => o.SetValue(ToolTipProperty, v);
    public static string GetToolTip(DependencyObject o) => (string)o.GetValue(ToolTipProperty);
    public static readonly DependencyProperty AutomationNameProperty = Register("AutomationName");
    public static void SetAutomationName(DependencyObject o, string v) => o.SetValue(AutomationNameProperty, v);
    public static string GetAutomationName(DependencyObject o) => (string)o.GetValue(AutomationNameProperty);
    private static readonly Dictionary<DependencyProperty, string> Names = new()
    {
        [TextProperty] = "Text", [ContentProperty] = "Content", [HeaderProperty] = "Header", [OnContentProperty] = "OnContent", [OffContentProperty] = "OffContent", [PlaceholderTextProperty] = "PlaceholderText",
        [TitleProperty] = "Title", [MessageProperty] = "Message", [PrimaryButtonTextProperty] = "PrimaryButtonText",
        [SecondaryButtonTextProperty] = "SecondaryButtonText", [CloseButtonTextProperty] = "CloseButtonText",
        [ToolTipProperty] = "ToolTip", [AutomationNameProperty] = "AutomationName"
    };
}
