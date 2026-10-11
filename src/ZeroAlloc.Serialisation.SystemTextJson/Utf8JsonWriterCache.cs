using System.Buffers;
using System.Text.Json;

namespace ZeroAlloc.Serialisation.SystemTextJson;

/// <summary>
/// One <see cref="Utf8JsonWriter"/> per thread, pointed at each caller's <see cref="IBufferWriter{T}"/>
/// in turn, so <see cref="SystemTextJsonSerializer{T}.Serialize"/> does not allocate a writer per call.
/// </summary>
/// <remarks>
/// <see cref="Rent"/> empties the slot, so a call made while the thread's writer is in use, such as a
/// converter that serializes through a serializer again, gets a new writer instead of corrupting the
/// outer one. <see cref="Return"/> resets the writer whether or not the write completed, which discards
/// the state an exception left behind and drops the reference to the caller's buffer writer.
/// </remarks>
internal static class Utf8JsonWriterCache
{
    [ThreadStatic]
    private static Utf8JsonWriter? t_writer;

    public static Utf8JsonWriter Rent(IBufferWriter<byte> output)
    {
        var writer = t_writer;
        if (writer is null)
            return new Utf8JsonWriter(output);

        t_writer = null;
        writer.Reset(output);
        return writer;
    }

    public static void Return(Utf8JsonWriter writer)
    {
        writer.Reset(DetachedOutput.Instance);
        t_writer = writer;
    }

    /// <summary>The output of a cached writer between calls. Never written to: every call resets first.</summary>
    private sealed class DetachedOutput : IBufferWriter<byte>
    {
        public static readonly DetachedOutput Instance = new();

        public void Advance(int count) => throw new InvalidOperationException("A cached Utf8JsonWriter was used without being reset.");

        public Memory<byte> GetMemory(int sizeHint = 0) => throw new InvalidOperationException("A cached Utf8JsonWriter was used without being reset.");

        public Span<byte> GetSpan(int sizeHint = 0) => throw new InvalidOperationException("A cached Utf8JsonWriter was used without being reset.");
    }
}
