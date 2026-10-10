namespace ZeroAlloc.Serialisation.MemoryPack;

/// <summary>
/// A <see cref="MemoryPackSerializer{T}"/> for a <c>[MemoryPackable]</c> type, safe under trimming and
/// Native AOT. This is the Native AOT path for MemoryPack.
/// </summary>
/// <remarks>
/// The constructor registers <typeparamref name="T"/>'s formatter through the type's generated static
/// <c>RegisterFormatter</c>, so MemoryPack never looks it up by reflection. Unlike
/// <see cref="MemoryPackSerializer{T}"/>, it carries no trim or AOT annotations.
/// </remarks>
/// <typeparam name="T">A <c>[MemoryPackable]</c> type.</typeparam>
public sealed class MemoryPackableSerializer<T> : MemoryPackSerializer<T>
    where T : global::MemoryPack.IMemoryPackable<T>
{
    /// <summary>Creates a serializer and registers the formatter for <typeparamref name="T"/>.</summary>
    public MemoryPackableSerializer()
        : base(formatterRegistered: true)
    {
        T.RegisterFormatter();
    }
}
