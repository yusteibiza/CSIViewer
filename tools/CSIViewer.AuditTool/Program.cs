using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(args[0]);
var output = Path.Combine(root, "artifacts", "audit");
Directory.CreateDirectory(output);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
string Relative(string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
static bool Source(string file) => !file.Split(Path.DirectorySeparatorChar).Any(x => x is "bin" or "obj" or ".git");
static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
static int XmlLine(XObject node) => (node as IXmlLineInfo)?.LineNumber ?? 0;
void Save(string name, object data) => File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(data, jsonOptions));

var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
    .Where(Source).Where(x => !Relative(x).StartsWith("artifacts/", StringComparison.Ordinal)).Order().Select(file =>
{
    var xml = XDocument.Load(file);
    string[] Values(string name) => xml.Descendants(name).Select(x => x.Value).ToArray();
    return new
    {
        File = Relative(file), Frameworks = Values("TargetFramework"), Runtime = Values("RuntimeIdentifier"),
        Platform = Values("PlatformTarget"), Output = Values("OutputType"),
        References = xml.Descendants("ProjectReference").Select(x => (string?)x.Attribute("Include")).ToArray(),
        Packages = xml.Descendants("PackageReference").Select(x => (string?)x.Attribute("Include")).ToArray(),
        LinkedOrExcludedCode = xml.Descendants("Compile").Select(x => x.ToString()).ToArray(),
        EmbeddedResources = xml.Descendants("EmbeddedResource").Select(x => (string?)x.Attribute("Include")).ToArray()
    };
}).ToArray();
Save("projects.json", projects);

var methodNames = new HashSet<string>(StringComparer.Ordinal);
var sourceFiles = new List<object>();
var subscriptions = new List<object>();
var reviewSites = new List<object>();
var typesCount = 0;
var methodsCount = 0;
foreach (var folder in new[] { "src", "tests", "tools" })
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories).Where(Source).Order())
{
    var text = File.ReadAllText(file);
    var syntax = CSharpSyntaxTree.ParseText(text).GetRoot();
    var types = syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(x => new
    {
        Name = x.Identifier.ValueText, Kind = x.Kind().ToString(), Line = Line(x),
        Namespace = x.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString(),
        Bases = x.BaseList?.ToString()
    }).ToArray();
    var methods = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(x =>
    {
        methodNames.Add(x.Identifier.ValueText);
        var empty = x.Body is { Statements.Count: 0 };
        if (empty || (x.Modifiers.Any(SyntaxKind.AsyncKeyword) && x.ReturnType.ToString() == "void"))
            reviewSites.Add(new { File = Relative(file), Line = Line(x), Method = x.Identifier.ValueText, Empty = empty, AsyncVoid = x.ReturnType.ToString() == "void" && x.Modifiers.Any(SyntaxKind.AsyncKeyword) });
        return new { Name = x.Identifier.ValueText, Line = Line(x), Type = x.Parent is TypeDeclarationSyntax type ? type.Identifier.ValueText : "", Return = x.ReturnType.ToString(), Parameters = x.ParameterList.ToString(), Modifiers = x.Modifiers.ToString(), Empty = empty };
    }).ToArray();
    foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                 .Where(x => x.IsKind(SyntaxKind.AddAssignmentExpression) || x.IsKind(SyntaxKind.SubtractAssignmentExpression)))
        subscriptions.Add(new { File = Relative(file), Line = Line(assignment), Kind = assignment.Kind().ToString(), Target = assignment.Left.ToString(), Handler = assignment.Right.ToString()[..Math.Min(assignment.Right.ToString().Length, 500)] });
    var risks = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
        .Where(x => Regex.IsMatch(x.Expression.ToString(), @"(\.Wait$|\.GetResult$|Task\.Run$|\.WaitAsync$|\.Dispose|File\.WriteAll|File\.ReadAll|Process\.Start|Registry\.|\.StartNew|\.ContinueWith)"))
        .Select(x => new { Line = Line(x), Call = x.Expression.ToString() }).ToArray();
    var catches = syntax.DescendantNodes().OfType<CatchClauseSyntax>()
        .Where(x => x.Block.Statements.Count == 0).Select(x => Line(x)).ToArray();
    typesCount += types.Length;
    methodsCount += methods.Length;
    sourceFiles.Add(new { File = Relative(file), Lines = text.Count(x => x == '\n') + 1, Types = types, Methods = methods, ReviewCalls = risks, EmptyCatches = catches });
}
Save("code-inventory.json", sourceFiles);
Save("event-subscriptions.json", subscriptions);
Save("method-review-sites.json", reviewSites);

var xamlFiles = new List<object>();
var eventPattern = new Regex(@"^(Click|Loaded|Unloaded|Closing|Closed|Activated|SelectionChanged|TextChanged|Toggled|ValueChanged|SizeChanged|ActualThemeChanged|ItemClick|DoubleTapped|Tapped|RightTapped|Pointer\w+|KeyDown|KeyUp|GotFocus|LostFocus|Drag\w+|Drop|Navigation\w+|BackRequested|Opened|Opening|PasswordChanged|BeforeTextChanging|TextChanging|OnNavigatedTo|OnNavigatedFrom)$");
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories).Where(Source).Order())
{
    var xml = XDocument.Load(file, LoadOptions.SetLineInfo);
    XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
    var controls = xml.Descendants().Select(element => new
    {
        Type = element.Name.LocalName, Name = (string?)element.Attribute(x + "Name"), Line = XmlLine(element),
        Content = (string?)element.Attribute("Content"), Text = (string?)element.Attribute("Text"),
        Command = (string?)element.Attribute("Command"),
        Events = element.Attributes().Where(a => a.Name.NamespaceName.Length == 0 && eventPattern.IsMatch(a.Name.LocalName))
            .Select(a => new { Event = a.Name.LocalName, Handler = a.Value, HandlerNameExistsInSource = methodNames.Contains(a.Value), Line = XmlLine(a) }).ToArray(),
        FixedColors = element.Attributes().Where(a => a.Value.StartsWith('#')).Select(a => new { Property = a.Name.ToString(), Value = a.Value }).ToArray(),
        ThemeKey = (string?)element.Attribute(x + "Key")
    }).ToArray();
    xamlFiles.Add(new { File = Relative(file), Class = (string?)xml.Root?.Attribute(x + "Class"), Root = xml.Root?.Name.LocalName, Controls = controls });
}
Save("xaml-controls-events.json", xamlFiles);
var packageXml = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
Save("package-versions.json", packageXml.Descendants("PackageVersion").Select(x => new { Id = (string?)x.Attribute("Include"), Version = (string?)x.Attribute("Version") }));
var summary = new { GeneratedUtc = DateTimeOffset.UtcNow, Projects = projects.Length, CSharpFiles = sourceFiles.Count, Types = typesCount, Methods = methodsCount, XamlFiles = xamlFiles.Count, EventAssignments = subscriptions.Count, MethodReviewSites = reviewSites.Count, Note = "Static inventory, not runtime verification. Conditional/shared code and += arithmetic need manual interpretation." };
Save("inventory-summary.json", summary);
Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));
