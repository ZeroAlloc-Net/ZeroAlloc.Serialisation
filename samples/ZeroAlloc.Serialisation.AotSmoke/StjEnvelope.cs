namespace ZeroAlloc.Serialisation.AotSmoke;

public sealed record StjEnvelope<T>(string MessageId, T Body);
