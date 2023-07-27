using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Schema;
using Xunit;

namespace XmlSchemaClassGenerator.Tests {
    public class DateTimeOffsetTypeTests
    {
        private static IEnumerable<string> ConvertXml(string name, string xsd, Generator generatorPrototype = null)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var writer = new MemoryOutputWriter();

            var gen = new Generator
            {
                OutputWriter = writer,
                Version = new VersionProvider("Tests", "1.0.0.1"),
                NamespaceProvider = generatorPrototype.NamespaceProvider,
                GenerateNullables = generatorPrototype.GenerateNullables,
                IntegerDataType = generatorPrototype.IntegerDataType,
                UseIntegerDataTypeAsFallback = generatorPrototype.UseIntegerDataTypeAsFallback,
                DataAnnotationMode = generatorPrototype.DataAnnotationMode,
                GenerateDesignerCategoryAttribute = generatorPrototype.GenerateDesignerCategoryAttribute,
                GenerateComplexTypesForCollections = generatorPrototype.GenerateComplexTypesForCollections,
                EntityFramework = generatorPrototype.EntityFramework,
                AssemblyVisible = generatorPrototype.AssemblyVisible,
                GenerateInterfaces = generatorPrototype.GenerateInterfaces,
                MemberVisitor = generatorPrototype.MemberVisitor,
                CodeTypeReferenceOptions = generatorPrototype.CodeTypeReferenceOptions,
                ConvertDateTimeToDateTimeOffset = generatorPrototype.ConvertDateTimeToDateTimeOffset
            };

            var set = new XmlSchemaSet();

            using (var stringReader = new StringReader(xsd))
            {
                var schema = XmlSchema.Read(stringReader, (s, e) =>
                {
                  throw new InvalidOperationException($"{e.Severity}: {e.Message}",e.Exception);
                });

                set.Add(schema);
            }

            gen.Generate(set);

            return writer.Content;
        }

        [Fact]        
        public void TestConversionOfDateTimeToDateTimeOffset()
        {
            var xsd = @$"<?xml version=""1.0"" encoding=""UTF-8""?>
                        <xs:schema elementFormDefault=""qualified"" xmlns:xs=""http://www.w3.org/2001/XMLSchema"">
	                        <xs:complexType name=""document"">
                            <xs:sequence>
		                          <xs:element name=""birthdate"" type=""sql_date"" minOccurs=""0""/>
                            </xs:sequence>
	                        </xs:complexType>
                            <xs:simpleType name=""sql_date"">
		                        <xs:restriction base=""xs:dateTime"">
			                        <xs:minInclusive value=""1753-01-01T00:00:00""/>
		                        </xs:restriction>
	                        </xs:simpleType>
                        </xs:schema>";

            var generatedType = ConvertXml(nameof(TestConversionOfDateTimeToDateTimeOffset), xsd, new Generator
            {
                NamespaceProvider = new NamespaceProvider
                {
                    GenerateNamespace = key => "Test",                    
                },
                ConvertDateTimeToDateTimeOffset = true
            });

            Assert.Contains("[System.Xml.Serialization.XmlElementAttribute(\"birthdate\")]", generatedType.First());
            Assert.Contains("public System.DateTimeOffset Birthdate { get; set; }", generatedType.First());            
        }        
    }
}
