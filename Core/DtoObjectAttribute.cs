using System;
using System.Collections.Generic;
using System.Text;

namespace DtoGenerator.Core;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public sealed class DtoObjectAttribute(Type type) : Attribute
{
    public Type Type { get; } = type;

    public string? ClassNamespace { get; set; }

    public string? ClassName { get; set; }

    public bool IncludeNonPrimitives { get; set; } = false;

    public string[]? ExcludedProperties { get; set; }

    public bool MakePropertiesOptional { get; set; } = false;

    public string[]? NonOptionalProperties { get; set; }

    public bool CopyAttributes { get; set; } = true;
}
