namespace ZeroAlloc.Serialisation.AotSmoke;

[global::MemoryPack.MemoryPackable]
public readonly partial record struct MpPair<TLeft, TRight>(TLeft Left, TRight Right);
