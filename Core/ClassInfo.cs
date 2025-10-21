using System;
using System.Collections.Generic;
using System.Text;
using DtoGenerator.Utility;

namespace DtoGenerator.Core;

readonly record struct ClassInfo
{
    public readonly string ClassType;
    public readonly string? NameSpace;
    public readonly string ClassName;
    public readonly EquatableList<string> Properties;
    public readonly EquatableList<string> Methods;
    public readonly EquatableList<string> AdditionalClasses;

    public ClassInfo(
        string classType,
        string? nameSpace,
        string className,
        EquatableList<string> properties,
        EquatableList<string> methods,
        EquatableList<string> additionalClasses
    )
    {
        ClassType = classType;
        NameSpace = nameSpace;
        ClassName = className;
        Properties = properties;
        Methods = methods;
        AdditionalClasses = additionalClasses;
    }
}
