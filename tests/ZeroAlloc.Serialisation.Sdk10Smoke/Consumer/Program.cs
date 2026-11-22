using System.Buffers;
using System.Text.Json.Serialization;
using ZeroAlloc.Serialisation;

// Uses the serializer the bundled generator emits. If the compiler skips the generator,
// OrderSerializer does not exist and this does not build.
var buffer = new ArrayBufferWriter<byte>();
var serializer = new OrderSerializer();
serializer.Serialize(buffer, new Order { Id = "order-1", Quantity = 3 });
var back = serializer.Deserialize(buffer.WrittenSpan);

if (back is null || back.Id != "order-1" || back.Quantity != 3)
{
    Console.Error.WriteLine("SDK 10 consumer: the generated serializer returned wrong data.");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded and the generated serializer round-trips.");
return 0;

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class Order
{
    public string Id { get; set; } = "";
    public int Quantity { get; set; }
}

[JsonSerializable(typeof(Order))]
internal partial class OrderContext : JsonSerializerContext;
