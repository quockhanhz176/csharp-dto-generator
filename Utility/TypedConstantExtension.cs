using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DtoGenerator.Utility;

static class TypedConstantExtension
{
    public static string ToCodeString(this TypedConstant constant)
    {
        return constant.Kind switch
        {
            TypedConstantKind.Primitive => constant.Value is string str
                ? $"\"{str}\""
                : constant.Value?.ToString() ?? "null",
            TypedConstantKind.Enum => $"{constant.Type?.ToDisplayString()}.{constant.Value}",
            TypedConstantKind.Type => $"typeof({constant.Value})",
            TypedConstantKind.Array =>
                $"new {constant.Type?.ToDisplayString()} {{ {string.Join(", ", constant.Values.Select(ToCodeString))} }}",
            _ => "null",
        };
    }
}
