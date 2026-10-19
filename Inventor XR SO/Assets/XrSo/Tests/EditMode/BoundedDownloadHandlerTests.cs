using System;
using InventorXrSo.Core.Net;
using InventorXrSo.Unity.Net;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public class BoundedDownloadHandlerTests
    {
        private sealed class Receiver : BoundedDownloadHandler
        {
            public Receiver(long limit) : base(limit) { }
            public bool Receive(byte[] bytes, int length) => ReceiveData(bytes, length);
            public void Length(ulong length) => ReceiveContentLengthHeader(length);
            public byte[] Result() => GetData();
        }

        [Test]
        public void ExactLimitAcrossChunksPreservesOnlyReceivedBytes()
        {
            using (var receiver = new Receiver(5))
            {
                receiver.Length(5);
                Assert.IsTrue(receiver.Receive(new byte[] { 1, 2, 99 }, 2));
                Assert.IsTrue(receiver.Receive(new byte[] { 3, 4, 5 }, 3));
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5 }, receiver.Result());
                Assert.IsFalse(receiver.LimitExceeded);
            }
        }

        [Test]
        public void ChunkedOverflowRetainsNoPartOfOverflowingChunkAndCannotReturnPartialSuccess()
        {
            using (var receiver = new Receiver(4))
            {
                Assert.IsTrue(receiver.Receive(new byte[] { 1, 2, 3 }, 3));
                Assert.IsFalse(receiver.Receive(new byte[] { 4, 5 }, 2));
                Assert.AreEqual(3, receiver.ReceivedBytes);
                Assert.IsTrue(receiver.LimitExceeded);
                Assert.IsFalse(receiver.Receive(new byte[] { 4 }, 1));
                Assert.Throws<InvalidOperationException>(() => receiver.Result());
            }
        }

        [Test]
        public void OversizedLengthRejectsBeforeRetainingBody()
        {
            using (var receiver = new Receiver(4))
            {
                receiver.Length(ulong.MaxValue);
                Assert.IsFalse(receiver.Receive(new byte[] { 1 }, 1));
                Assert.AreEqual(0, receiver.ReceivedBytes);
                Assert.Throws<InvalidOperationException>(() => receiver.Result());
            }
        }

        [Test]
        public void UnderstatedLengthDoesNotBypassActualByteCap()
        {
            using (var receiver = new Receiver(1))
            {
                receiver.Length(0);
                Assert.IsFalse(receiver.Receive(new byte[] { 1, 2 }, 2));
                Assert.AreEqual(0, receiver.ReceivedBytes);
            }
        }

        [Test]
        public void ResultIsDetachedFromDownloadStorage()
        {
            using (var receiver = new Receiver(2))
            {
                var source = new byte[] { 7, 8 };
                receiver.Receive(source, 2);
                source[0] = 0;
                var first = receiver.Result();
                first[1] = 0;
                CollectionAssert.AreEqual(new byte[] { 7, 8 }, receiver.Result());
            }
        }

        [Test]
        public void RequestLimitMustFitManagedResponseArray()
        {
            var request = new TransportRequest("GET", "https://localhost/");
            Assert.AreEqual(0, request.MaxResponseBytes);
            Assert.Throws<ArgumentOutOfRangeException>(() => request.MaxResponseBytes = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => request.MaxResponseBytes = (long)int.MaxValue + 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Receiver(0));
        }
    }
}
