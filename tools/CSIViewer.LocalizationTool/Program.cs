using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(args[0]);
var apply = args.Contains("--apply");
var sources = new HashSet<string>(StringComparer.Ordinal);
var uiRoots = new[] { "CSIViewer", "CSIViewer.Host", "CSIViewer.Server" }.Select(x => Path.Combine(root, "src", x)).ToArray();
var attributes = new HashSet<string>(["Text", "Content", "Header", "PlaceholderText", "Title", "Message", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "ToolTipService.ToolTip", "AutomationProperties.Name"]);
XNamespace loc = "using:CSIViewer.Presentation";
foreach (var folder in uiRoots)
foreach (var file in Directory.EnumerateFiles(folder, "*.xaml", SearchOption.AllDirectories).Where(Include))
{
    var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
    var changed = false;
    foreach (var element in document.Descendants())
    foreach (var attribute in element.Attributes().ToArray())
    {
        var value = attribute.Value;
        if (attribute.Name.NamespaceName.Length > 0 || !attributes.Contains(attribute.Name.LocalName) ||
            value.StartsWith('{') || !value.Any(char.IsLetter)) continue;
        sources.Add(value);
        if (apply)
        {
            var name = attribute.Name.LocalName switch
            {
                "ToolTipService.ToolTip" => "ToolTip", "AutomationProperties.Name" => "AutomationName", var other => other
            };
            element.SetAttributeValue(loc + "Translate." + name, value);
            changed = true;
        }
    }
    if (apply && changed)
    {
        document.Root!.SetAttributeValue(XNamespace.Xmlns + "loc", loc.NamespaceName);
        document.Save(file, SaveOptions.DisableFormatting);
    }
}
foreach (var value in new[] { "Color de Windows", "Azul", "Verde", "Turquesa", "Violeta", "Rosa", "Naranja", "Rojo" }) sources.Add(value);
var folders = uiRoots.Concat(new[] { "CSIViewer.Core", "CSIViewer.Application", "CSIViewer.Services", "CSIViewer.Infrastructure", "CSIViewer.Protocol" }.Select(x => Path.Combine(root, "src", x)));
foreach (var folder in folders)
foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).Where(Include))
{
    if (Path.GetFileName(file) is "Localization.cs" or "AccentPalette.cs") continue;
    var text = File.ReadAllText(file);
    var tree = CSharpSyntaxTree.ParseText(text);
    var rewritten = new Rewrite(sources, apply).Visit(tree.GetRoot())!;
    if (apply && rewritten.ToFullString() != text) File.WriteAllText(file, rewritten.ToFullString());
}
var output = Path.Combine(root, "artifacts", "diagnostics", "localization-sources.json");
File.WriteAllText(output, JsonSerializer.Serialize(sources.Order(StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{sources.Count} UI resources; source rewrites applied={apply}");

static bool Include(string path) => !path.Split(Path.DirectorySeparatorChar).Any(x => x is "bin" or "obj");

sealed class Rewrite(HashSet<string> sources, bool apply) : CSharpSyntaxRewriter
{
    private static readonly HashSet<string> ShortWords = new(StringComparer.Ordinal)
    { "Conectar", "Conectando", "Reconectando", "Conectado", "Desconectado", "Disponible", "Ocupado", "Pendiente", "Aceptado", "Rechazado", "Enviar", "Recibir", "Guardar", "Cancelar", "Cerrar", "Pausar", "Reanudar", "Quitar", "Eliminar", "Editar", "Descripción", "Contraseña", "Nombre", "Alias", "Fecha", "Duración", "Resultado", "Motivo", "Permisos", "Controlador", "Anfitrión", "Carpeta", "Archivo", "Envío", "Recepción", "Preparando", "Transfiriendo", "Verificando", "Completada", "Cancelada", "Fallida", "Interrumpida", "Pausada", "Error", "Aviso", "Información", "Sí", "No", "Iniciar", "Detener", "Español", "Ayuda", "Configuración", "Contactos", "Historial", "Dispositivos", "Favoritos", "Todos", "Ninguno", "Sistema", "Claro", "Oscuro", "Vacío", "Correcta", "Fallida", "Correctas", "Fallidas", "Finalizada", "Finalizar", "Recibido", "Entregado", "Leyendo", "Calculando" };

    private bool Human(string value) => value.Any(char.IsLetter) &&
        !value.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("SELECT ", StringComparison.Ordinal) &&
        !value.StartsWith("ALTER ", StringComparison.Ordinal) && !value.StartsWith("CREATE ", StringComparison.Ordinal) &&
        (value.Contains(' ') || value.Any(c => "áéíóúüñ¿¡".Contains(c, StringComparison.Ordinal)) || ShortWords.Contains(value.TrimEnd('.', '…')) || sources.Contains(value));

    private static bool Excluded(SyntaxNode node)
    {
        if (node.Ancestors().Any(x => x is AttributeSyntax or PatternSyntax)) return true;
        foreach (var call in node.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            var name = call.Expression.ToString();
            if (name.Contains("Log", StringComparison.Ordinal) || name.Contains("Localization.", StringComparison.Ordinal) ||
                name.EndsWith(".Contains", StringComparison.Ordinal) || name.EndsWith(".StartsWith", StringComparison.Ordinal) ||
                name.EndsWith(".EndsWith", StringComparison.Ordinal) || name.EndsWith(".Equals", StringComparison.Ordinal) ||
                name.EndsWith(".Replace", StringComparison.Ordinal) || name.Contains("ExecuteSql", StringComparison.Ordinal) ||
                name.EndsWith(".GetMethod", StringComparison.Ordinal) || name.EndsWith(".GetProperty", StringComparison.Ordinal)) return true;
        }
        if (node.Parent is BinaryExpressionSyntax binary && (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))) return true;
        return false;
    }

    private static bool NeedsConstant(SyntaxNode node) => node.Ancestors().Any(x =>
        x is ParameterSyntax or ConstantPatternSyntax or AttributeSyntax ||
        x is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword) ||
        x is LocalDeclarationStatementSyntax local && local.Modifiers.Any(SyntaxKind.ConstKeyword));

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.StringLiteralExpression) || Excluded(node)) return base.VisitLiteralExpression(node);
        var value = node.Token.ValueText;
        if (!Human(value)) return node;
        sources.Add(value);
        if (!apply || NeedsConstant(node)) return node;
        return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::CSIViewer.Core.Localization.Text"),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(node.WithoutTrivia())))).WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        if (Excluded(node)) return node;
        var template = "";
        var expressions = new List<ExpressionSyntax>();
        foreach (var part in node.Contents)
        {
            if (part is InterpolatedStringTextSyntax text) template += text.TextToken.ValueText;
            else if (part is InterpolationSyntax interpolation)
            {
                template += "{" + expressions.Count + (interpolation.AlignmentClause?.ToString() ?? "") + (interpolation.FormatClause?.ToString() ?? "") + "}";
                expressions.Add((ExpressionSyntax)Visit(interpolation.Expression)!);
            }
        }
        if (!Human(template)) return base.VisitInterpolatedStringExpression(node);
        sources.Add(template);
        if (!apply || NeedsConstant(node)) return node;
        var arguments = new List<ArgumentSyntax> { SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(template))) };
        arguments.AddRange(expressions.Select(SyntaxFactory.Argument));
        return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::CSIViewer.Core.Localization.Format"),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))).WithTriviaFrom(node);
    }
}
