namespace ZeroAlloc.Serialisation.AotSmoke;

[global::MemoryPack.MemoryPackable]
public sealed partial record MpEnvelope<T>(string MessageId, T Body);
