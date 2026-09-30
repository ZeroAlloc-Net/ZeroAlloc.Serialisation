namespace ZeroAlloc.Serialisation;

/// <summary>
/// Declares a type serializable, so the source generator emits its <see cref="ISerializer{T}"/>,
/// its dependency-injection registration and its entry in the generated <c>SerializerDispatcher</c>.
/// </summary>
/// <remarks>
/// <para>
/// Apply <see cref="ZeroAllocSerializableAttribute(SerializationFormat)"/> to a non-generic class
/// or struct declaration.
/// </para>
/// <para>
/// A generic type is declared once per closed construction, on the assembly, with
/// <see cref="ZeroAllocSerializableAttribute(System.Type, SerializationFormat)"/>:
/// <c>[assembly: ZeroAllocSerializable(typeof(Envelope&lt;Order&gt;), SerializationFormat.MessagePack)]</c>.
/// The generated code names the closed type directly, so no reflection or runtime code generation
/// is involved and it stays NativeAOT-safe.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ZeroAllocSerializableAttribute : Attribute
{
    /// <summary>Declares the class or struct this attribute is applied to serializable.</summary>
    /// <param name="format">The serialization format of the type.</param>
    public ZeroAllocSerializableAttribute(SerializationFormat format)
    {
        Format = format;
    }

    /// <summary>
    /// Declares the closed generic type <paramref name="type"/> serializable. Valid only on the
    /// assembly: <c>[assembly: ZeroAllocSerializable(typeof(Envelope&lt;Order&gt;), format)]</c>.
    /// </summary>
    /// <param name="type">A closed construction of a generic type, such as <c>typeof(Envelope&lt;Order&gt;)</c>.</param>
    /// <param name="format">The serialization format of the type.</param>
    public ZeroAllocSerializableAttribute(Type type, SerializationFormat format)
    {
        Type = type;
        Format = format;
    }

    /// <summary>The serialization format of the type.</summary>
    public SerializationFormat Format { get; }

    /// <summary>
    /// The closed generic type an assembly-level declaration names, or <see langword="null"/> when
    /// the attribute is applied to a type declaration.
    /// </summary>
    public Type? Type { get; }
}
