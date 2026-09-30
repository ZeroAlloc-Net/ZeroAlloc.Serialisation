using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.AotSmoke;

// Closed generic types, declared serializable on the assembly (#183). The generated serializers
// and dispatcher entries name each closed type directly, so NativeAOT compiles them ahead of time
// with no reflection. Envelope<Order>, Pair<int, Order> and one nested construction per format.
[assembly: ZeroAllocSerializable(typeof(StjEnvelope<StjOrder>), SerializationFormat.SystemTextJson)]
[assembly: ZeroAllocSerializable(typeof(StjPair<int, StjOrder>), SerializationFormat.SystemTextJson)]
[assembly: ZeroAllocSerializable(typeof(StjEnvelope<StjPair<int, StjOrder>>), SerializationFormat.SystemTextJson)]
[assembly: ZeroAllocSerializable(typeof(MpEnvelope<MpOrder>), SerializationFormat.MemoryPack)]
[assembly: ZeroAllocSerializable(typeof(MpPair<int, MpOrder>), SerializationFormat.MemoryPack)]
[assembly: ZeroAllocSerializable(typeof(MpEnvelope<MpPair<int, MpOrder>>), SerializationFormat.MemoryPack)]
