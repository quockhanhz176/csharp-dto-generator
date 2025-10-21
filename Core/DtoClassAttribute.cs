using System;
using System.Collections.Generic;
using System.Text;

// copy this to the other DtoClassGegnerator file after modification
namespace DtoGenerator.Core;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
public sealed class DtoClassAttribute : Attribute
{
    public DtoClassAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }

    public string? ClassNamespace { get; set; }

    public string? ClassName { get; set; }

    public bool IncludeNonPrimitives { get; set; } = false;

    public string[]? ExcludedProperties { get; set; }

    public bool MakePropertiesOptional { get; set; } = false;

    public string[]? NonOptionalProperties { get; set; }

    public bool CopyAttributes { get; set; } = true;
}
