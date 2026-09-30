using System.Buffers;
using System.Text.Json.Serialization;
using MemoryPack;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Serialisation;
using ZeroAlloc.Serialisation.Tests.ClosedGenerics;

// Closed generic types are declared serializable on the assembly, one declaration per closed
// construction and format (#183). The generator gives each the serializer, DI registration and
// dispatcher entry a non-generic [ZeroAllocSerializable] type gets.
[assembly: ZeroAllocSerializable(typeof(MpEnvelope<MpPurchase>), SerializationFormat.MemoryPack)]
[assembly: ZeroAllocSerializable(typeof(MpPair<int, MpPurchase>), SerializationFormat.MemoryPack)]
[assembly: ZeroAllocSerializable(typeof(MpEnvelope<MpPair<int, MpPurchase>>), SerializationFormat.MemoryPack)]
[assembly: ZeroAllocSerializable(typeof(MsgpEnvelope<MsgpPurchase>), SerializationFormat.MessagePack)]
[assembly: ZeroAllocSerializable(typeof(MsgpPair<int, MsgpPurchase>), SerializationFormat.MessagePack)]
[assembly: ZeroAllocSerializable(typeof(MsgpEnvelope<MsgpPair<int, MsgpPurchase>>), SerializationFormat.MessagePack)]
[assembly: ZeroAllocSerializable(typeof(StjEnvelope<StjPurchase>), SerializationFormat.SystemTextJson)]
[assembly: ZeroAllocSerializable(typeof(StjPair<int, StjPurchase>), SerializationFormat.SystemTextJson)]
[assembly: ZeroAllocSerializable(typeof(StjEnvelope<StjPair<int, StjPurchase>>), SerializationFormat.SystemTextJson)]

namespace ZeroAlloc.Serialisation.Tests.ClosedGenerics
{
    [MemoryPackable]
    public sealed partial class MpPurchase
    {
        public int Id { get; set; }
        public string Customer { get; set; } = "";
    }

    [MemoryPackable]
    public sealed partial class MpEnvelope<T>
    {
        public string MessageId { get; set; } = "";
        public T? Body { get; set; }
    }

    [MemoryPackable]
    public partial struct MpPair<TLeft, TRight>
    {
        public TLeft Left { get; set; }
        public TRight Right { get; set; }
    }

    [MessagePackObject]
    public sealed class MsgpPurchase
    {
        [Key(0)] public int Id { get; set; }
        [Key(1)] public string Customer { get; set; } = "";
    }

    [MessagePackObject]
    public sealed class MsgpEnvelope<T>
    {
        [Key(0)] public string MessageId { get; set; } = "";
        [Key(1)] public T? Body { get; set; }
    }

    [MessagePackObject]
    public struct MsgpPair<TLeft, TRight>
    {
        [Key(0)] public TLeft Left { get; set; }
        [Key(1)] public TRight Right { get; set; }
    }

    public sealed class StjPurchase
    {
        public int Id { get; set; }
        public string Customer { get; set; } = "";
    }

    public sealed class StjEnvelope<T>
    {
        public string MessageId { get; set; } = "";
        public T? Body { get; set; }
    }

    public struct StjPair<TLeft, TRight>
    {
        public TLeft Left { get; set; }
        public TRight Right { get; set; }
    }

    [JsonSerializable(typeof(StjEnvelope<StjPurchase>))]
    [JsonSerializable(typeof(StjPair<int, StjPurchase>))]
    [JsonSerializable(typeof(StjEnvelope<StjPair<int, StjPurchase>>))]
    internal sealed partial class ClosedGenericJsonContext : JsonSerializerContext { }
}

namespace ZeroAlloc.Serialisation.Tests
{
    public sealed class ClosedGenericRoundTripTests
    {
        // ── MemoryPack ───────────────────────────────────────────────────────────

        [Fact]
        public void MemoryPack_Envelope_RoundTrips()
        {
            var original = new MpEnvelope<MpPurchase> { MessageId = "m-1", Body = new MpPurchase { Id = 7, Customer = "ada" } };

            var back = RoundTrip(new MpEnvelopeOfMpPurchaseSerializer(), original);

            Assert.Equal("m-1", back!.MessageId);
            Assert.Equal(7, back.Body!.Id);
            Assert.Equal("ada", back.Body.Customer);
        }

        [Fact]
        public void MemoryPack_Pair_RoundTrips()
        {
            var original = new MpPair<int, MpPurchase> { Left = 3, Right = new MpPurchase { Id = 9, Customer = "bob" } };

            var back = RoundTrip(new MpPairOfInt32AndMpPurchaseSerializer(), original);

            Assert.Equal(3, back.Left);
            Assert.Equal(9, back.Right.Id);
        }

        [Fact]
        public void MemoryPack_NestedClosedGeneric_RoundTrips()
        {
            var original = new MpEnvelope<MpPair<int, MpPurchase>>
            {
                MessageId = "m-2",
                Body = new MpPair<int, MpPurchase> { Left = 4, Right = new MpPurchase { Id = 11, Customer = "cy" } },
            };

            var back = RoundTrip(new MpEnvelopeOfMpPairOfInt32AndMpPurchaseSerializer(), original);

            Assert.Equal("m-2", back!.MessageId);
            Assert.Equal(4, back.Body.Left);
            Assert.Equal("cy", back.Body.Right.Customer);
        }

        // ── MessagePack ──────────────────────────────────────────────────────────

        [Fact]
        public void MessagePack_Envelope_RoundTrips()
        {
            var original = new MsgpEnvelope<MsgpPurchase> { MessageId = "m-1", Body = new MsgpPurchase { Id = 7, Customer = "ada" } };

            var back = RoundTrip(new MsgpEnvelopeOfMsgpPurchaseSerializer(), original);

            Assert.Equal("m-1", back!.MessageId);
            Assert.Equal(7, back.Body!.Id);
            Assert.Equal("ada", back.Body.Customer);
        }

        [Fact]
        public void MessagePack_Pair_RoundTrips()
        {
            var original = new MsgpPair<int, MsgpPurchase> { Left = 3, Right = new MsgpPurchase { Id = 9, Customer = "bob" } };

            var back = RoundTrip(new MsgpPairOfInt32AndMsgpPurchaseSerializer(), original);

            Assert.Equal(3, back.Left);
            Assert.Equal(9, back.Right.Id);
        }

        [Fact]
        public void MessagePack_NestedClosedGeneric_RoundTrips()
        {
            var original = new MsgpEnvelope<MsgpPair<int, MsgpPurchase>>
            {
                MessageId = "m-2",
                Body = new MsgpPair<int, MsgpPurchase> { Left = 4, Right = new MsgpPurchase { Id = 11, Customer = "cy" } },
            };

            var back = RoundTrip(new MsgpEnvelopeOfMsgpPairOfInt32AndMsgpPurchaseSerializer(), original);

            Assert.Equal("m-2", back!.MessageId);
            Assert.Equal(4, back.Body.Left);
            Assert.Equal("cy", back.Body.Right.Customer);
        }

        // ── System.Text.Json ─────────────────────────────────────────────────────

        [Fact]
        public void SystemTextJson_Envelope_RoundTrips()
        {
            var original = new StjEnvelope<StjPurchase> { MessageId = "m-1", Body = new StjPurchase { Id = 7, Customer = "ada" } };

            var back = RoundTrip(new StjEnvelopeOfStjPurchaseSerializer(), original);

            Assert.Equal("m-1", back!.MessageId);
            Assert.Equal(7, back.Body!.Id);
            Assert.Equal("ada", back.Body.Customer);
        }

        [Fact]
        public void SystemTextJson_Pair_RoundTrips()
        {
            var original = new StjPair<int, StjPurchase> { Left = 3, Right = new StjPurchase { Id = 9, Customer = "bob" } };

            var back = RoundTrip(new StjPairOfInt32AndStjPurchaseSerializer(), original);

            Assert.Equal(3, back.Left);
            Assert.Equal(9, back.Right.Id);
        }

        [Fact]
        public void SystemTextJson_NestedClosedGeneric_RoundTrips()
        {
            var original = new StjEnvelope<StjPair<int, StjPurchase>>
            {
                MessageId = "m-2",
                Body = new StjPair<int, StjPurchase> { Left = 4, Right = new StjPurchase { Id = 11, Customer = "cy" } },
            };

            var back = RoundTrip(new StjEnvelopeOfStjPairOfInt32AndStjPurchaseSerializer(), original);

            Assert.Equal("m-2", back!.MessageId);
            Assert.Equal(4, back.Body.Left);
            Assert.Equal("cy", back.Body.Right.Customer);
        }

        // ── DI registration and dispatcher ───────────────────────────────────────

        [Fact]
        public void DiRegistrations_ResolveTheGeneratedSerializers()
        {
            var provider = new ServiceCollection()
                .AddMpEnvelopeOfMpPurchaseSerializer()
                .AddMsgpPairOfInt32AndMsgpPurchaseSerializer()
                .AddStjEnvelopeOfStjPairOfInt32AndStjPurchaseSerializer()
                .BuildServiceProvider();

            Assert.IsType<MpEnvelopeOfMpPurchaseSerializer>(provider.GetRequiredService<ISerializer<MpEnvelope<MpPurchase>>>());
            Assert.IsType<MsgpPairOfInt32AndMsgpPurchaseSerializer>(provider.GetRequiredService<ISerializer<MsgpPair<int, MsgpPurchase>>>());
            Assert.IsType<StjEnvelopeOfStjPairOfInt32AndStjPurchaseSerializer>(
                provider.GetRequiredService<ISerializer<StjEnvelope<StjPair<int, StjPurchase>>>>());
        }

        public static TheoryData<object> DispatchedValues() => new()
        {
            new MpEnvelope<MpPurchase> { MessageId = "mp", Body = new MpPurchase { Id = 1 } },
            new MpPair<int, MpPurchase> { Left = 2, Right = new MpPurchase { Id = 3 } },
            new MpEnvelope<MpPair<int, MpPurchase>> { MessageId = "mp-nested", Body = new MpPair<int, MpPurchase> { Left = 4, Right = new MpPurchase { Id = 5 } } },
            new MsgpEnvelope<MsgpPurchase> { MessageId = "msgp", Body = new MsgpPurchase { Id = 1 } },
            new MsgpPair<int, MsgpPurchase> { Left = 2, Right = new MsgpPurchase { Id = 3 } },
            new MsgpEnvelope<MsgpPair<int, MsgpPurchase>> { MessageId = "msgp-nested", Body = new MsgpPair<int, MsgpPurchase> { Left = 4, Right = new MsgpPurchase { Id = 5 } } },
            new StjEnvelope<StjPurchase> { MessageId = "stj", Body = new StjPurchase { Id = 1 } },
            new StjPair<int, StjPurchase> { Left = 2, Right = new StjPurchase { Id = 3 } },
            new StjEnvelope<StjPair<int, StjPurchase>> { MessageId = "stj-nested", Body = new StjPair<int, StjPurchase> { Left = 4, Right = new StjPurchase { Id = 5 } } },
        };

        [Theory]
        [MemberData(nameof(DispatchedValues))]
        public void Dispatcher_RoundTripsEveryClosedGeneric_ByItsRuntimeType(object value)
        {
            var dispatcher = new ServiceCollection()
                .AddSerializerDispatcher()
                .BuildServiceProvider()
                .GetRequiredService<ISerializerDispatcher>();
            var type = value.GetType();

            var bytes = dispatcher.Serialize(value, type);
            var back = dispatcher.Deserialize(bytes, type);

            Assert.NotNull(back);
            Assert.IsType(type, back);
            Assert.Equal(bytes.ToArray(), dispatcher.Serialize(back!, type).ToArray());
        }

        [Fact]
        public void Dispatcher_RejectsAnUndeclaredClosedConstruction()
        {
            var dispatcher = new SerializerDispatcher();
            var undeclared = new MsgpEnvelope<string> { MessageId = "x", Body = "y" };

            var ex = Assert.Throws<NotSupportedException>(() => dispatcher.Serialize(undeclared, undeclared.GetType()));
            Assert.Contains("[assembly: ZeroAllocSerializable(typeof(...), format)]", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Attribute_ExposesTheDeclaredClosedType_AndNoTypeForTheTypeLevelForm()
        {
            var assemblyLevel = new ZeroAllocSerializableAttribute(typeof(MpEnvelope<MpPurchase>), SerializationFormat.MemoryPack);
            var typeLevel = new ZeroAllocSerializableAttribute(SerializationFormat.SystemTextJson);

            Assert.Equal(typeof(MpEnvelope<MpPurchase>), assemblyLevel.Type);
            Assert.Equal(SerializationFormat.MemoryPack, assemblyLevel.Format);
            Assert.Null(typeLevel.Type);
            Assert.Equal(SerializationFormat.SystemTextJson, typeLevel.Format);
        }

        private static T? RoundTrip<T>(ISerializer<T> serializer, T value)
        {
            var buffer = new ArrayBufferWriter<byte>();
            serializer.Serialize(buffer, value);
            Assert.NotEqual(0, buffer.WrittenCount);
            return serializer.Deserialize(buffer.WrittenSpan);
        }
    }
}
