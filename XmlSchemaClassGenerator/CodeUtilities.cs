﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;

namespace XmlSchemaClassGenerator
{
    public static class CodeUtilities
    {
        // Match non-letter followed by letter
        static readonly Regex PascalCaseRegex = new(@"[^\p{L}]\p{L}", RegexOptions.Compiled);

        // Uppercases first letter and all letters following non-letters.
        // Examples: testcase -> Testcase, html5element -> Html5Element, test_case -> Test_Case
        public static string ToPascalCase(this string s)
        {
            if (string.IsNullOrEmpty(s)) { return s; }
            return char.ToUpperInvariant(s[0])
                + PascalCaseRegex.Replace(s.Substring(1), m => m.Value[0] + char.ToUpperInvariant(m.Value[1]).ToString());
        }

        public static string ToCamelCase(this string s)
        {
            if (string.IsNullOrEmpty(s)) { return s; }
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        public static string ToBackingField(this string propertyName, string privateFieldPrefix)
        {
            return string.Concat(privateFieldPrefix, propertyName.ToCamelCase());
        }

        public static bool? IsDataTypeAttributeAllowed(this XmlSchemaDatatype type, GeneratorConfiguration configuration)
        {
            bool? result = type.TypeCode switch
            {
                XmlTypeCode.AnyAtomicType => false,// union
                XmlTypeCode.Time or XmlTypeCode.Date or XmlTypeCode.Base64Binary or XmlTypeCode.HexBinary => true,
                _ => false,
            };

            if (!configuration.ConvertDateTimeToDateTimeOffset && type.TypeCode == XmlTypeCode.DateTime) result = true;

            return result;
        }

        private static Type GetIntegerDerivedType(XmlSchemaDatatype type, GeneratorConfiguration configuration, IEnumerable<RestrictionModel> restrictions)
        {
            if (configuration.IntegerDataType != null && !configuration.UseIntegerDataTypeAsFallback) return configuration.IntegerDataType;

            var xmlTypeCode = type.TypeCode;

            Type result = null;

            var maxInclusive = restrictions.OfType<MaxInclusiveRestrictionModel>().SingleOrDefault();
            var minInclusive = restrictions.OfType<MinInclusiveRestrictionModel>().SingleOrDefault();

            decimal? maxInclusiveValue = null;
            if (maxInclusive is null && xmlTypeCode == XmlTypeCode.NegativeInteger)
            {
                maxInclusiveValue = -1;
            }
            else if (maxInclusive is null && xmlTypeCode == XmlTypeCode.NonPositiveInteger)
            {
                maxInclusiveValue = 0;
            }
            else if (maxInclusive != null && decimal.TryParse(maxInclusive.Value, out decimal value))
            {
                maxInclusiveValue = value;
            }

            decimal? minInclusiveValue = null;
            if (minInclusive is null && xmlTypeCode == XmlTypeCode.PositiveInteger)
            {
                minInclusiveValue = 1;
            }
            else if (minInclusive is null && xmlTypeCode == XmlTypeCode.NonNegativeInteger)
            {
                minInclusiveValue = 0;
            }
            else if (minInclusive != null && decimal.TryParse(minInclusive.Value, out decimal value))
            {
                minInclusiveValue = value;
            }

            // If either value is null, then that value is either unbounded or too large to fit in any numeric type.
            if (minInclusiveValue != null && maxInclusiveValue != null) {
                if (minInclusiveValue >= byte.MinValue && maxInclusiveValue <= byte.MaxValue)
                    result = typeof(byte);
                else if (minInclusiveValue >= sbyte.MinValue && maxInclusiveValue <= sbyte.MaxValue)
                    result = typeof(sbyte);
                else if (minInclusiveValue >= ushort.MinValue && maxInclusiveValue <= ushort.MaxValue)
                    result = typeof(ushort);
                else if (minInclusiveValue >= short.MinValue && maxInclusiveValue <= short.MaxValue)
                    result = typeof(short);
                else if (minInclusiveValue >= uint.MinValue && maxInclusiveValue <= uint.MaxValue)
                    result = typeof(uint);
                else if (minInclusiveValue >= int.MinValue && maxInclusiveValue <= int.MaxValue)
                    result = typeof(int);
                else if (minInclusiveValue >= ulong.MinValue && maxInclusiveValue <= ulong.MaxValue)
                    result = typeof(ulong);
                else if (minInclusiveValue >= long.MinValue && maxInclusiveValue <= long.MaxValue)
                    result = typeof(long);
                else // If it didn't fit in a decimal, we could not have gotten here.
                    result = typeof(decimal);

                return result;
            }

            if (restrictions.SingleOrDefault(r => r is TotalDigitsRestrictionModel) is not TotalDigitsRestrictionModel totalDigits
                || ((xmlTypeCode == XmlTypeCode.PositiveInteger
                     || xmlTypeCode == XmlTypeCode.NonNegativeInteger) && totalDigits.Value >= 30)
                || ((xmlTypeCode == XmlTypeCode.Integer
                     || xmlTypeCode == XmlTypeCode.NegativeInteger
                     || xmlTypeCode == XmlTypeCode.NonPositiveInteger) && totalDigits.Value >= 29))
            {
                if (configuration.UseIntegerDataTypeAsFallback && configuration.IntegerDataType != null)
                    return configuration.IntegerDataType;
                return typeof(string);
            }

            switch (xmlTypeCode)
            {
                case XmlTypeCode.PositiveInteger:
                case XmlTypeCode.NonNegativeInteger:
                    switch (totalDigits.Value)
                    {
                        case int n when (n < 3):
                            result = typeof(byte);
                            break;
                        case int n when (n < 5):
                            result = typeof(ushort);
                            break;
                        case int n when (n < 10):
                            result = typeof(uint);
                            break;
                        case int n when (n < 20):
                            result = typeof(ulong);
                            break;
                        case int n when (n < 30):
                            result = typeof(decimal);
                            break;
                    }

                    break;

                case XmlTypeCode.Integer:
                case XmlTypeCode.NegativeInteger:
                case XmlTypeCode.NonPositiveInteger:
                    switch (totalDigits.Value)
                    {
                        case int n when (n < 3):
                            result = typeof(sbyte);
                            break;
                        case int n when (n < 5):
                            result = typeof(short);
                            break;
                        case int n when (n < 10):
                            result = typeof(int);
                            break;
                        case int n when (n < 19):
                            result = typeof(long);
                            break;
                        case int n when (n < 29):
                            result = typeof(decimal);
                            break;
                    }
                    break;
            }

            return result;
        }

        public static Type GetEffectiveType(this XmlSchemaDatatype type, GeneratorConfiguration configuration, IEnumerable<RestrictionModel> restrictions, bool attribute = false)
        {
            var resultType = type.TypeCode switch
            {
                XmlTypeCode.AnyAtomicType => typeof(string),// union
                XmlTypeCode.AnyUri or XmlTypeCode.GDay or XmlTypeCode.GMonth or XmlTypeCode.GMonthDay or XmlTypeCode.GYear or XmlTypeCode.GYearMonth => typeof(string),
                XmlTypeCode.Duration => configuration.NetCoreSpecificCode ? type.ValueType : typeof(string),
                XmlTypeCode.Time => typeof(DateTime),
                XmlTypeCode.DateTime => configuration.ConvertDateTimeToDateTimeOffset ? typeof(DateTimeOffset) : typeof(DateTime),
                XmlTypeCode.Idref => typeof(string),
                XmlTypeCode.Integer or XmlTypeCode.NegativeInteger or XmlTypeCode.NonNegativeInteger or XmlTypeCode.NonPositiveInteger or XmlTypeCode.PositiveInteger => GetIntegerDerivedType(type, configuration, restrictions),
                _ => type.ValueType,
            };

            if (type.Variety == XmlSchemaDatatypeVariety.List)
            {
                if (resultType.IsArray)
                    resultType = resultType.GetElementType();

                // XmlSerializer doesn't support xsd:list for elements, only for attributes:
                // https://docs.microsoft.com/en-us/previous-versions/dotnet/netframework-4.0/t84dzyst(v%3dvs.100)

                // Also, de/serialization fails when the XML schema type is ambiguous
                // DateTime -> date, datetime, or time
                // byte[] -> hexBinary or base64Binary

                if (!attribute || resultType == typeof(DateTime) || resultType == typeof(byte[]))
                    resultType = typeof(string);
            }

            return resultType;
        }

        public static XmlQualifiedName GetQualifiedName(this XmlSchemaType schemaType)
        {
            return schemaType.QualifiedName.IsEmpty
                ? schemaType.BaseXmlSchemaType.QualifiedName
                : schemaType.QualifiedName;
        }

        public static XmlQualifiedName GetQualifiedName(this TypeModel typeModel)
        {
            XmlQualifiedName qualifiedName;
            if (typeModel is not SimpleModel simpleTypeModel)
            {
                if (typeModel.IsAnonymous)
                {
                    qualifiedName = typeModel.XmlSchemaName;
                }
                else
                {
                    qualifiedName = typeModel.XmlSchemaType.GetQualifiedName();
                }
            }
            else
            {
                qualifiedName = simpleTypeModel.XmlSchemaType.GetQualifiedName();
                var xmlSchemaType = simpleTypeModel.XmlSchemaType;
                while (qualifiedName.Namespace != XmlSchema.Namespace &&
                       xmlSchemaType.BaseXmlSchemaType != null)
                {
                    xmlSchemaType = xmlSchemaType.BaseXmlSchemaType;
                    qualifiedName = xmlSchemaType.GetQualifiedName();
                }
            }
            return qualifiedName;
        }

        public static string GetUniqueTypeName(this NamespaceModel model, string name)
        {
            var n = name;
            var i = 2;

            while (model.Types.ContainsKey(n) && model.Types[n] is not SimpleModel)
            {
                n = name + i;
                i++;
            }

            return n;
        }

        public static string GetUniqueFieldName(this TypeModel typeModel, PropertyModel propertyModel)
        {
            var classModel = typeModel as ClassModel;
            var propBackingFieldName = propertyModel.Name.ToBackingField(classModel?.Configuration.PrivateMemberPrefix);

            if (CSharpKeywords.Contains(propBackingFieldName.ToLower()))
                propBackingFieldName = "@" + propBackingFieldName;

            if (classModel == null)
            {
                return propBackingFieldName;
            }

            var i = 0;
            foreach (var prop in classModel.Properties)
            {
                if (propertyModel == prop)
                {
                    i += 1;
                    break;
                }

                var backingFieldName = prop.Name.ToBackingField(classModel.Configuration.PrivateMemberPrefix);
                if (backingFieldName == propBackingFieldName)
                {
                    i += 1;
                }
            }

            if (i <= 1)
            {
                return propBackingFieldName;
            }

            return string.Format("{0}{1}", propBackingFieldName, i);
        }

        public static string GetUniquePropertyName(this TypeModel tm, string name)
        {
            if (tm is ClassModel cls)
            {
                var i = 0;
                var n = name;
                var baseClasses = cls.AllBaseClasses.ToList();
                var props = cls.Properties.ToList();

                while (baseClasses.SelectMany(b => b.Properties)
                    .Concat(props)
                    .Any(p => p.Name == n))
                {
                    n = name + (++i);
                }

                return n;
            }

            return name;
        }

        static readonly Regex NormalizeNewlinesRegex = new (@"(^|[^\r])\n", RegexOptions.Compiled);

        internal static string NormalizeNewlines(string text)
        {
            return NormalizeNewlinesRegex.Replace(text, "$1\r\n");
        }

        static readonly List<string> CSharpKeywords = new()
        {
            "abstract", "as", "base", "bool",
            "break", "byte", "case", "catch",
            "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate",
            "do", "double", "else", "enum",
            "event", " explicit", "extern", "false",
            "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit",
            "in", "int", "interface", "internal",
            "is", "lock", "long", "namespace",
            "new", "null", "object", "operator",
            "out", "override", "params", "private",
            "protected", "public", "readonly", "ref",
            "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string",
            "struct", "switch", "this", "throw",
            "true", "try", "typeof", "uint",
            "ulong", "unchecked", "unsafe", "ushort",
            "using", "using static", "virtual", "void",
            "volatile", "while"
        };

        internal static Uri CreateUri(string uri) => string.IsNullOrEmpty(uri) ? null : new Uri(uri);

        public static KeyValuePair<NamespaceKey, string> ParseNamespace(string nsArg, string namespacePrefix)
        {
            var parts = nsArg.Split(new[] { '=' }, 2);
            if (parts.Length != 2)
            {
                throw new ArgumentException("XML and C# namespaces should be separated by '='. You entered: " + nsArg);
            }

            var xmlNs = parts[0];
            var netNs = parts[1];
            var parts2 = xmlNs.Split(new[] { '|' }, 2);
            var source = parts2.Length == 2 ? new Uri(parts2[1], UriKind.RelativeOrAbsolute) : null;
            xmlNs = parts2[0];
            if (!string.IsNullOrEmpty(namespacePrefix))
            {
                netNs = namespacePrefix + "." + netNs;
            }
            return new KeyValuePair<NamespaceKey, string>(new NamespaceKey(source, xmlNs), netNs);
        }

        public static readonly ImmutableList<(string Namespace, Func<GeneratorConfiguration,bool> Condition)> UsingNamespaces = ImmutableList.Create<(string Namespace, Func<GeneratorConfiguration, bool> Condition)>(
            ("System", c => c.CompactTypeNames),
            ("System.CodeDom.Compiler", c => c.CompactTypeNames),
            ("System.Collections.Generic", c => c.CompactTypeNames),
            ("System.Collections.ObjectModel", c => c.CompactTypeNames),
            ("System.ComponentModel", c => c.CompactTypeNames),
            ("System.ComponentModel.DataAnnotations", c => c.CompactTypeNames && (c.DataAnnotationMode != DataAnnotationMode.None || c.EntityFramework)),
            ("System.Diagnostics", c => c.CompactTypeNames && c.GenerateDebuggerStepThroughAttribute),
            ("System.Linq", c => c.EnableDataBinding),
            ("System.Xml", c => c.CompactTypeNames),
            ("System.Xml.Schema", c => c.CompactTypeNames),
            ("System.Xml.Serialization", c => c.CompactTypeNames)
        );

        public static bool IsUsingNamespace(Type t, GeneratorConfiguration conf) => UsingNamespaces.Any(n => n.Namespace == t.Namespace && n.Condition(conf));

        public static bool IsUsingNamespace(string namespaceName, GeneratorConfiguration conf) => UsingNamespaces.Any(n => n.Namespace == namespaceName && n.Condition(conf));

        public static CodeTypeReference CreateTypeReference(Type t, GeneratorConfiguration conf)
        {
            if (IsUsingNamespace(t, conf))
            {
                var name = t.Name;
                var typeRef = new CodeTypeReference(name, conf.CodeTypeReferenceOptions);

                if (t.IsConstructedGenericType)
                {
                    var typeArgs = t.GenericTypeArguments.Select(a => CreateTypeReference(a, conf)).ToArray();
                    typeRef.TypeArguments.AddRange(typeArgs);
                }

                return typeRef;
            }
            else
            {
                var typeRef = new CodeTypeReference(t, conf.CodeTypeReferenceOptions);

                foreach (var typeArg in typeRef.TypeArguments)
                {
                    if (typeArg is CodeTypeReference typeArgRef)
                    {
                        typeArgRef.Options = conf.CodeTypeReferenceOptions;
                    }
                }

                return typeRef;
            }

        }

        public static CodeTypeReference CreateTypeReference(string namespaceName, string typeName, GeneratorConfiguration conf)
        {
            if (IsUsingNamespace(namespaceName, conf))
            {
                var typeRef = new CodeTypeReference(typeName, conf.CodeTypeReferenceOptions);

                return typeRef;
            }
            else
                return new CodeTypeReference($"{namespaceName}.{typeName}", conf.CodeTypeReferenceOptions);
        }

        /// <summary>
        /// See https://github.com/mganss/XmlSchemaClassGenerator/issues/245
        /// and https://docs.microsoft.com/en-us/dotnet/api/system.xml.serialization.xmlattributeattribute#remarks
        /// </summary>
        public static bool IsXmlLangOrSpace(XmlQualifiedName name)
        {
            return name != null && name.Namespace == "http://www.w3.org/XML/1998/namespace"
                && (name.Name == "lang" || name.Name == "space");
        }

        internal static XmlQualifiedName GetQualifiedName(this XmlSchemaObject obj)
        {
            var n = obj switch
            {
                XmlSchemaAttribute attr => attr.QualifiedName,
                XmlSchemaAttributeGroup attrGroup => attrGroup.QualifiedName,
                _ => null
            };

            return n;
        }
    }
}