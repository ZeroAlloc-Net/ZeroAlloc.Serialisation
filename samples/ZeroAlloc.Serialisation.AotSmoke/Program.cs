using System;
using System.Buffers;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.AotSmoke;

// Round-trip the two formats that are known-safe under PublishAot=true.
//
// SystemTextJson is intentionally NOT exercised here: the generator
// currently emits `JsonSerializer.Serialize(_jw, value)` with no
// JsonTypeInfo<T>, which requires reflection at runtime. Under PublishAot
// the STJ runtime enforces `JsonSerializerIsReflectionEnabledByDefault=false`
// and throws InvalidOperationException. Tracked as a follow-up: generator
// must switch STJ emission to require JsonTypeInfo<T> / JsonSerializerContext
// for true AOT compatibility. MpMessage and MsgpMessage do round-trip safely
// because both backends carry their own source generators.

var mp = new MpMessage { Id = "mp-1", Value = 99 };
var msgp = new MsgpMessage { Id = "msgp-1", Value = 7 };

var mpBuf = new ArrayBufferWriter<byte>();
new MpMessageSerializer().Serialize(mpBuf, mp);
var mpBack = new MpMessageSerializer().Deserialize(mpBuf.WrittenSpan);

var msgpBuf = new ArrayBufferWriter<byte>();
new MsgpMessageSerializer().Serialize(msgpBuf, msgp);
var msgpBack = new MsgpMessageSerializer().Deserialize(msgpBuf.WrittenSpan);

var ok = string.Equals(mpBack?.Id, mp.Id, StringComparison.Ordinal)
    && string.Equals(msgpBack?.Id, msgp.Id, StringComparison.Ordinal);
Console.WriteLine(ok ? "AOT smoke: PASS" : "AOT smoke: FAIL");
return ok ? 0 : 1;
