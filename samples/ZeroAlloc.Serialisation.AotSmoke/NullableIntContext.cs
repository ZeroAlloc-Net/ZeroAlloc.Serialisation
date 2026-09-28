using System.Text.Json.Serialization;

namespace ZeroAlloc.Serialisation.AotSmoke;

// Supplies JsonTypeInfo<int?> for SystemTextJsonSerializer<int?>, so the generic
// ISerializer<T> pass-through runs with T = Nullable<int> under NativeAOT.
[JsonSerializable(typeof(int?))]
internal partial class NullableIntContext : JsonSerializerContext { }
