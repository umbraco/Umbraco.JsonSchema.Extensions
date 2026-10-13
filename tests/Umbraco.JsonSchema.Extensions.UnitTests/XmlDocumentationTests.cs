using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Umbraco.JsonSchema.Extensions.UnitTests;

/// <summary>
/// Tests for <see cref="XmlDocumentation" />, reading the XML documentation file of this test assembly.
/// </summary>
[TestFixture]
public class XmlDocumentationTests
{
    /// <summary>
    /// The summary of a type, property or field is rendered as text.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="memberName">The name of the property or field, or <see langword="null" /> for the type itself.</param>
    /// <param name="expected">The expected summary.</param>
    [TestCase(typeof(DocumentedType), null, "A documented type.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.Property), "A documented property.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.Field), "A documented field.")]
    [TestCase(typeof(GenericType<int>), null, "A generic type of T.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.References), "References DocumentedType, DocumentedType.Property, List,\nString.Join, null and https://umbraco.com.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.AdjacentElements), "Use DocumentedType DocumentedType.Property or a b.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.Paragraphs), "First paragraph\ncontinues here.\n\nSecond paragraph.\n\nBefore break\nafter break.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.ConsecutiveParagraphs), "First.\n\nSecond.")]
    [TestCase(typeof(DocumentedType), nameof(DocumentedType.Undocumented), null)]
    [TestCase(typeof(DerivedType), null, "A base type.")]
    [TestCase(typeof(DerivedType), nameof(DerivedType.FromBaseType), "Documented on the base type.")]
    [TestCase(typeof(DerivedType), nameof(DerivedType.FromInterface), "Documented on the interface.")]
    [TestCase(typeof(DerivedType), nameof(DerivedType.FromCref), "A documented property.")]
    public void GetSummary_ReturnsRenderedSummary(Type type, string? memberName, string? expected)
    {
        var sut = new XmlDocumentation(assembly => assembly.Location);
        MemberInfo member = memberName is null ? type : type.GetMember(memberName)[0];

        Assert.That(sut.GetSummary(member), Is.EqualTo(expected));
    }

    /// <summary>
    /// Without an XML documentation file, there is no summary.
    /// </summary>
    [Test]
    public void GetSummary_NoXmlDocumentationFile_ReturnsNull()
    {
        var sut = new XmlDocumentation(_ => null);

        Assert.That(sut.GetSummary(typeof(DocumentedType)), Is.Null);
    }

    /// <summary>
    /// A documented type.
    /// </summary>
    private sealed class DocumentedType
    {
        /// <summary>
        /// A documented field.
        /// </summary>
        public const string Field = "";

        /// <summary>
        /// A documented property.
        /// </summary>
        public string? Property { get; set; }

        /// <summary>
        /// References <see cref="DocumentedType" />, <see cref="Property" />, <see cref="List{T}" />,
        /// <see cref="string.Join(string, string[])" />, <see langword="null" /> and <see href="https://umbraco.com" />.
        /// </summary>
        public string? References { get; set; }

        /// <summary>
        /// Use <see cref="DocumentedType" /> <see cref="Property" /> or <c>a</c> <c>b</c>.
        /// </summary>
        public string? AdjacentElements { get; set; }

        /// <summary>
        /// First paragraph
        /// continues here.
        /// <para>Second paragraph.</para>
        /// Before break<br />after break.
        /// </summary>
        public string? Paragraphs { get; set; }

        /// <summary>
        /// <para>First.</para>
        /// <para>Second.</para>
        /// </summary>
        public string? ConsecutiveParagraphs { get; set; }

        public string? Undocumented { get; set; }
    }

    /// <summary>
    /// A generic type of <typeparamref name="T" />.
    /// </summary>
    /// <typeparam name="T">The type parameter.</typeparam>
    private sealed class GenericType<T>
    {
    }

    /// <summary>
    /// An interface.
    /// </summary>
    private interface IDocumentedInterface
    {
        /// <summary>
        /// Documented on the interface.
        /// </summary>
        string? FromInterface { get; set; }
    }

    /// <summary>
    /// A base type.
    /// </summary>
    private abstract class BaseType
    {
        /// <summary>
        /// Documented on the base type.
        /// </summary>
        public virtual string? FromBaseType { get; set; }
    }

    /// <inheritdoc />
    private sealed class DerivedType : BaseType, IDocumentedInterface
    {
        /// <inheritdoc />
        public override string? FromBaseType { get; set; }

        /// <inheritdoc />
        public string? FromInterface { get; set; }

        /// <inheritdoc cref="DocumentedType.Property" />
        public string? FromCref { get; set; }
    }
}
