using System;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using NSubstitute;
using Sentry.Internal.Http;
using Xunit;

namespace Sentry.Tests.Internals
{
    public class DefaultSentryHttpClientFactoryTests
    {
        private class Fixture
        {
            public SentryOptions HttpOptions { get; set; } = new SentryOptions
            {
                Dsn = DsnSamples.ValidDsnWithSecret
            };

            public Action<HttpClientHandler> ConfigureHandler { get; set; }
            public Action<HttpClient> ConfigureClient { get; set; }

            public DefaultSentryHttpClientFactory GetSut()
                => new DefaultSentryHttpClientFactory(ConfigureHandler, ConfigureClient);
        }

        private readonly Fixture _fixture = new Fixture();

        [Fact]
        public void Create_Returns_HttpClient()
        {
            var sut = _fixture.GetSut();

            Assert.NotNull(sut.Create(_fixture.HttpOptions));
        }

        [Fact]
        public void Create_CompressionLevelNoCompression_NoGzipRequestBodyHandler()
        {
            _fixture.HttpOptions.RequestBodyCompressionLevel = CompressionLevel.NoCompression;

            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            foreach (var handler in client.GetMessageHandlers())
            {
                Assert.IsNotType<GzipRequestBodyHandler>(handler);
            }
        }

        [Theory]
        [InlineData(CompressionLevel.Fastest)]
        [InlineData(CompressionLevel.Optimal)]
        public void Create_CompressionLevelEnabled_ByDefault_IncludesGzipRequestBodyHandler(CompressionLevel level)
        {
            _fixture.HttpOptions.RequestBodyCompressionLevel = level;

            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            Assert.Contains(client.GetMessageHandlers(), h => h.GetType() == typeof(GzipBufferedRequestBodyHandler));
        }

        [Theory]
        [InlineData(CompressionLevel.Fastest)]
        [InlineData(CompressionLevel.Optimal)]
        public void Create_CompressionLevelEnabled_NonBuffered_IncludesGzipRequestBodyHandler(CompressionLevel level)
        {
            _fixture.HttpOptions.RequestBodyCompressionLevel = level;
            _fixture.HttpOptions.RequestBodyCompressionBuffered = false;
            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            Assert.Contains(client.GetMessageHandlers(), h => h.GetType() == typeof(GzipRequestBodyHandler));
        }

        [Fact]
        public void Create_RetryAfterHandler_FirstHandler()
        {
            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            Assert.Equal(typeof(RetryAfterHandler), client.GetMessageHandlers().First().GetType());
        }

        [Fact]
        public void Create_DecompressionMethodNone_SetToClientHandler()
        {
            _fixture.HttpOptions.DecompressionMethods = DecompressionMethods.None;

            var configureHandlerInvoked = false;
            _fixture.ConfigureHandler = (handler) =>
            {
                Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
                configureHandlerInvoked = true;
            };
            var sut = _fixture.GetSut();

            _ = sut.Create(_fixture.HttpOptions);

            Assert.True(configureHandlerInvoked);
        }

        [Fact]
        public void Create_DecompressionMethodDefault_AllBitsSet()
        {
            var configureHandlerInvoked = false;
            _fixture.ConfigureHandler = (handler) =>
            {
                Assert.Equal(~DecompressionMethods.None, handler.AutomaticDecompression);
                configureHandlerInvoked = true;
            };
            var sut = _fixture.GetSut();

            _ = sut.Create(_fixture.HttpOptions);

            Assert.True(configureHandlerInvoked);
        }

        [Fact]
        public void Create_DefaultHeaders_AcceptJson()
        {
            var configureHandlerInvoked = false;
            _fixture.ConfigureClient = (client) =>
            {
                Assert.Equal("application/json", client.DefaultRequestHeaders.Accept.ToString());
                configureHandlerInvoked = true;
            };
            var sut = _fixture.GetSut();

            _ = sut.Create(_fixture.HttpOptions);

            Assert.True(configureHandlerInvoked);
        }

        [Fact]
        public void Create_HttpProxyOnOptions_HandlerUsesProxy()
        {
            _fixture.HttpOptions.HttpProxy = new WebProxy("https://proxy.sentry.io:31337");
            var configureHandlerInvoked = false;
            _fixture.ConfigureHandler = (handler) =>
            {
                Assert.Same(_fixture.HttpOptions.HttpProxy, handler.Proxy);
                configureHandlerInvoked = true;
            };
            var sut = _fixture.GetSut();

            _ = sut.Create(_fixture.HttpOptions);

            Assert.True(configureHandlerInvoked);
        }

        [Fact]
        public void Create_ProvidedCreateHttpClientHandler_ReturnedHandlerUsed()
        {
            var handler = Substitute.For<HttpClientHandler>();
            _fixture.HttpOptions.CreateHttpClientHandler = () => handler;
            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            Assert.Contains(client.GetMessageHandlers(), h => ReferenceEquals(handler, h));
        }

        [Fact]
        public void Create_NullCreateHttpClientHandler_HttpClientHandlerUsed()
        {
            _fixture.HttpOptions.CreateHttpClientHandler = null;
            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            Assert.Contains(client.GetMessageHandlers(), h => h.GetType() == typeof(HttpClientHandler));
        }

        [Fact]
        public void Create_NullReturnedCreateHttpClientHandler_HttpClientHandlerUsed()
        {
            _fixture.HttpOptions.CreateHttpClientHandler = () => null;
            var sut = _fixture.GetSut();

            var client = sut.Create(_fixture.HttpOptions);

            Assert.Contains(client.GetMessageHandlers(), h => h.GetType() == typeof(HttpClientHandler));
        }
    }
}
