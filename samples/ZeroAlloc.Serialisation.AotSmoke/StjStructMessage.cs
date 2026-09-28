namespace ZeroAlloc.Serialisation.AotSmoke;

// Value-type [ZeroAllocSerializable] target. The emitted StjStructMessageSerializer
// must implement ISerializer<StjStructMessage> with a plain struct return, and the
// generated SerializerDispatcher must box and unbox it correctly under NativeAOT.
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public readonly record struct StjStructMessage(int A, string B);
