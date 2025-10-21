using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using DtoGenerator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DtoGenerator.Core;

[Generator]
class DtoClassGenerator : IIncrementalGenerator
{
    private static readonly string NullablAttributeName =
        "System.Runtime.CompilerServices.NullableAttribute";

    private static readonly string AttributeName = typeof(DtoClassAttribute).FullName;

    //private static readonly string Extension =
    //    @"
    //    namespace DtoGenerator.Core;

    //    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    //    public sealed class DtoClassAttribute(Type type) : Attribute
    //    {
    //        public Type Type { get; } = type;

    //        public string? ClassNamespace { get; set; }

    //        public string? ClassName { get; set; }

    //        public bool IncludeNonPrimitives { get; set; } = false;

    //        public string[]? ExcludedProperties { get; set; }

    //        public bool MakePropertiesOptional { get; set; } = false;

    //        public string[]? NonOptionalProperties { get; set; }

    //        public bool CopyAttributes { get; set; } = true;
    //    } ";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        //context.RegisterPostInitializationOutput(static ctx =>
        //    ctx.AddSource("DtoClassAttribute.g.cs", SourceText.From(Extension, Encoding.UTF8))
        //);
        var classSource = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                AttributeName,
                (syntaxNode, _) => true,
                GetClassInfoList
            )
            .SelectMany(static (classSource, _) => classSource);

        context.RegisterSourceOutput(
            classSource,
            static (context, classInfo) => AddFile(context, classInfo)
        );

        context.RegisterPostInitializationOutput(static ctx =>
        {
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(
                "DtoGenerator.Core.DtoClassAttribute.cs"
            );
            if (stream is null)
                return;

            using var reader = new StreamReader(stream);
            var sourceText = reader.ReadToEnd();

            ctx.AddSource("DtoClassAttribute.g.cs", SourceText.From(sourceText, Encoding.UTF8));
        });
    }

    private static ClassInfo[] GetClassInfoList(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        return context
            .Attributes.SelectMany<AttributeData, ClassInfo>(a =>
            {
                var classInfo = GetClassInfo(a, context, cancellationToken);
                return classInfo is null ? [] : [(ClassInfo)classInfo];
            })
            .ToArray();
    }

    private static ClassInfo? GetClassInfo(
        AttributeData attribute,
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        var currentSymbol = context.TargetSymbol;
        INamedTypeSymbol? currentClass = null;

        if (currentSymbol is INamedTypeSymbol notNullClass)
        {
            currentClass = notNullClass;
        }

        if (
            attribute.ConstructorArguments.Length == 0
            || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol originalTypeSymbol
        )
        {
            // no constructor arguments?
            return null;
        }

        if (
            originalTypeSymbol.TypeKind != TypeKind.Class
            && originalTypeSymbol.TypeKind != TypeKind.Interface
            && originalTypeSymbol.TypeKind != TypeKind.Struct
        )
        {
            return null;
        }

        // parameterName of the class or parameterName of the interface without the first letter if it is 'I'
        string targetClassName =
            originalTypeSymbol.TypeKind == TypeKind.Interface
            && originalTypeSymbol.Name.StartsWith("I")
                ? originalTypeSymbol.Name.Substring(1)
                : originalTypeSymbol.Name;

        string? classNamespace =
            currentClass?.ContainingNamespace.ToDisplayString()
            ?? GetArg<string?>(attribute, nameof(DtoClassAttribute.ClassNamespace), null);
        string className = GetArg(
            attribute,
            nameof(DtoClassAttribute.ClassName),
            currentClass?.Name ?? $"{targetClassName}Dto"
        );
        string classType =
            className == currentClass?.Name
                ? $"partial {(currentClass.IsRecord ? "record " : "")}{(currentClass.TypeKind is TypeKind.Struct ? "struct" : "class")}"
                : "partial class";
        bool includeNonPrimitives = GetArg(
            attribute,
            nameof(DtoClassAttribute.IncludeNonPrimitives),
            false
        );
        bool makePropertiesOptional = GetArg(
            attribute,
            nameof(DtoClassAttribute.MakePropertiesOptional),
            false
        );
        bool copyAttributes = GetArg(attribute, nameof(DtoClassAttribute.CopyAttributes), true);

        var excludedPropertyNames = GetArrayArg<string>(
            attribute,
            nameof(DtoClassAttribute.ExcludedProperties)
        );
        var nonOptionalProperties = GetArrayArg<string>(
            attribute,
            nameof(DtoClassAttribute.NonOptionalProperties)
        );
        List<IPropertySymbol> includedProperties = [];
        List<IPropertySymbol> excludedProperties = [];
        List<string> properties = new EquatableList<string>([]);

        foreach (var propertySymbol in originalTypeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (propertySymbol.Name == "EqualityContract")
            {
                continue;
            }
            if (
                !includeNonPrimitives
                    && propertySymbol.Type.GetTypedConstantKind() != TypedConstantKind.Primitive
                || excludedPropertyNames.Contains(propertySymbol.Name)
            )
            {
                excludedProperties.Add(propertySymbol);
                continue;
            }
            else
            {
                includedProperties.Add(propertySymbol);
                var accessibility = SyntaxFacts.GetText(propertySymbol.DeclaredAccessibility);
                var type = propertySymbol.Type.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                var optional =
                    propertySymbol.NullableAnnotation == NullableAnnotation.Annotated ? ""
                    : makePropertiesOptional && !nonOptionalProperties.Contains(propertySymbol.Name)
                        ? "?"
                    : "";
                var name = propertySymbol.Name;

                var setAccessor = propertySymbol.SetMethod is not null
                    ? $"{GetAccessorString(propertySymbol, propertySymbol.SetMethod)}set; "
                    : "";
                var getAccessor = propertySymbol.GetMethod is not null
                    ? $"{GetAccessorString(propertySymbol, propertySymbol.GetMethod)}get; "
                    : "get; ";
                var propertyStringBuilder = new StringBuilder();
                if (copyAttributes)
                {
                    foreach (var propertyAttribute in propertySymbol.GetAttributes())
                    {
                        var attributeName = propertyAttribute.AttributeClass?.ToDisplayString();
                        if (attributeName == NullablAttributeName)
                        {
                            optional = "?";
                            continue;
                        }
                        var parameters = propertyAttribute
                            .ConstructorArguments.Select(a => a.ToCodeString())
                            .Union(
                                propertyAttribute.NamedArguments.Select(a =>
                                    a.Key + " = " + a.Value.ToCodeString()
                                )
                            );
                        propertyStringBuilder.AppendLine(
                            $"\t[{propertyAttribute.AttributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({string.Join(", ", parameters)})]\n"
                        );
                    }
                }

                propertyStringBuilder.AppendLine(
                    $"\t{accessibility} {type}{optional} {name} {{ {getAccessor}{setAccessor}}}"
                );
                properties.Add(propertyStringBuilder.ToString());
            }
        }

        //Convert methods
        var methods = new EquatableList<string>([]);
        var additionalClass = new EquatableList<string>([]);

        string originalType = originalTypeSymbol.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat
        );

        // toOld method (convert from the generated type to the original type)
        // Don't support interface for now
        // Assignment code is also faulty if the property or the property set accessor on the old object is inacessible
        if (
            (
                originalTypeSymbol.TypeKind is TypeKind.Class
                || originalTypeSymbol.TypeKind is TypeKind.Struct
            ) && !makePropertiesOptional
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var toOldMethodBuilder = new StringBuilder(
                $$"""
                    public {{originalType}} To{{originalTypeSymbol.Name}}(
                """
            );

            toOldMethodBuilder.Append(
                $$"""
                )
                    {
                         var returnValue = new {{originalType}}(){

                """
            );

            var last = includedProperties.Last();
            foreach (var propertySymbol in includedProperties)
            {
                var name = propertySymbol.Name;
                toOldMethodBuilder.AppendLine(
                    $"""
                                {name} = this.{name},
                    """
                );
            }

            //if (assignmentFromParametersBuilder.Length > 0)
            //{
            //    toOldMethodBuilder.Append(assignmentFromParametersBuilder.ToString());
            //}

            var hasCustom =
                currentClass != null
                && currentClass
                    .GetMembers()
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.Name == "CustomToOriginal")
                    is not null;

            //CustomToOriginal(returnValue);
            toOldMethodBuilder.AppendLine(
                $$"""
                        };
                        {{(
                            hasCustom 
                                ? "CustomToOriginal(returnValue);" 
                                : "return returnValue;"
                        )}}                        
                    }
                """
            );

            methods.Add(toOldMethodBuilder.ToString());
            //methods.Add($$"""
            //    partial void CustomToOriginal({{originalType}} returnValue);
            //""");
        }

        // fromOld method (convert from the original type to the generated type)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceParamName = "source";

            var toNewExtensionClassBuilder = new StringBuilder(
                $$"""
                public static partial class {{className}}Extensions
                {
                    public static {{className}} To{{className}}(this {{originalType}} {{sourceParamName}})
                    {
                         var returnValue = new {{className}}(){

                """
            );

            foreach (var propertySymbol in includedProperties)
            {
                var name = propertySymbol.Name;
                toNewExtensionClassBuilder.AppendLine(
                    $$"""
                                {{name}} = {{sourceParamName}}.{{name}},
                    """
                );
            }

            toNewExtensionClassBuilder.AppendLine(
                $$"""
                        };
                """
            );
            var hasCustom =
                currentClass != null
                && currentClass
                    .GetMembers()
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.Name == "CustomFromOriginal")
                    is not null;

            //{{className}}.CustomFromOriginal(returnValue, {{sourceParamName}});
            toNewExtensionClassBuilder.AppendLine(
                $$"""
                        {{( 
                            hasCustom ? 
                                "return Value.CustomFromOriginal({{sourceParamName}});" : 
                                "return returnValue;" 
                        )}}
                    }
                }
                """
            );
            additionalClass.Add(toNewExtensionClassBuilder.ToString());

            //methods.Add($$"""
            //    static partial void CustomFromOriginal({{className}} returnValue, {{originalType}} source);
            //""");
        }

        return new ClassInfo(
            classType,
            classNamespace,
            className,
            new EquatableList<string>(properties),
            methods,
            additionalClass
        );
    }

    private static void AddFile(SourceProductionContext context, ClassInfo classInfo)
    {
        var fileName = $"{classInfo.ClassName}.g.cs";
        var fileContentBuilder = new StringBuilder(
            $$"""
            #nullable enable

            // <auto-generated/>
            {{(classInfo.NameSpace != null ? $"namespace {classInfo.NameSpace};\n" : "")}}
            public {{classInfo.ClassType}} {{classInfo.ClassName}}
            {

            """
        );

        foreach (string property in classInfo.Properties)
        {
            fileContentBuilder.AppendLine(property);
        }
        foreach (string method in classInfo.Methods)
        {
            fileContentBuilder.AppendLine();
            fileContentBuilder.AppendLine(method);
        }

        fileContentBuilder.AppendLine(
            """
            }
            """
        );

        foreach (string c in classInfo.AdditionalClasses)
        {
            fileContentBuilder.AppendLine();
            fileContentBuilder.AppendLine(c);
        }

        var fileContent = fileContentBuilder.ToString();
        context.AddSource(fileName, SourceText.From(fileContent, Encoding.UTF8));
    }

    private static T GetArg<T>(
        AttributeData attributeData,
        string namedArgumentName,
        T defaultValue
    )
    {
        var argumentPair = attributeData.NamedArguments.FirstOrDefault(p =>
            p.Key == namedArgumentName
        );
        return argumentPair.Key is null ? defaultValue : (T)argumentPair.Value.Value;
    }

    private static T[] GetArrayArg<T>(AttributeData attributeData, string namedArgumentName)
    {
        var argumentPair = attributeData.NamedArguments.FirstOrDefault(p =>
            p.Key == namedArgumentName
        );
        var array = argumentPair.Key is null
            ? []
            : argumentPair.Value.Values.Select(v => (T)v.Value).ToArray();
        return array;
    }

    private static string GetAccessorString(
        IPropertySymbol propertySymbol,
        IMethodSymbol accessorSymbol
    )
    {
        return propertySymbol.DeclaredAccessibility == accessorSymbol.DeclaredAccessibility
            ? ""
            : $"{SyntaxFacts.GetText(propertySymbol.DeclaredAccessibility)} ";
    }
}
