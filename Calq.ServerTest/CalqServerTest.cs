using Calq.Server;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace Calq.ServerTest {
    public class CalqServerTest {

        private HttpClient client = new();
        private TestService root = new();

        public CalqServerTest() {
            var url = "http://localhost:8080/";

            var server = new CalqServer(root);
            server.Prefixes = new [] { url };
            new Thread(() => server.Start()).Start();

            client.BaseAddress = new Uri(url);
        }

        private (string, HttpStatusCode) Send(HttpMethod method, string uri) {
            var request = new HttpRequestMessage(method, uri);
            var response = client.Send(request);
            return (new StreamReader(response.Content.ReadAsStream()).ReadToEnd(), response.StatusCode);
        }

        private (string body, HttpStatusCode status) Get(string uri) {
            return Send(HttpMethod.Get, uri);
        }

        private string Serialize(object instance) {
            var serializerOptions = new JsonSerializerOptions {
                IncludeFields = true
            };
            return JsonSerializer.Serialize(instance, serializerOptions);
        }

        [Fact]
        public void Test1() {
            var result = Get("");
            Assert.Equal(Serialize(new TestService()), result.body);
        }

        [Fact]
        public void Test2() {
            var result = Get("/");
            Assert.Equal(Serialize(new TestService()), result.body);
        }

        [Fact]
        public void Test3() {
            var result = Get(client.BaseAddress + "/");
            Assert.Equal(Serialize(new TestService()), result.body);
        }

        [Fact]
        public void Test4() {
            var result = Get("integer");
            Assert.Equal(Serialize(root.integer), result.body);
        }

        [Fact]
        public void Test5() {
            var result = Get("text");
            Assert.Equal(Serialize(root.text), result.body);
        }

        [Fact]
        public void Test6() {
            Assert.Null(root.nullText);
            var result = Get("nullText");
            Assert.Equal(Serialize(root.nullText), result.body);
        }

        [Fact]
        public void Test7() {
            Assert.NotNull(root.nested);
            var result = Get("nested");
            Assert.Equal(Serialize(root.nested), result.body);
        }

        [Fact]
        public void Test8() {
            var result = Get("nested/a");
            Assert.Equal(Serialize(root.nested.a), result.body);
        }

        [Fact]
        public void Test9() {
            Assert.Null(root.nullNested);
            var result = Get("nullNested/a");
            Assert.Equal(HttpStatusCode.NotFound, result.status);
        }

        [Fact]
        public void Test10() {
            var result = Get("@");
            Assert.Equal(HttpStatusCode.InternalServerError, result.status);
        }

        [Fact]
        public void Test11() {
            var result = Get("array");
            Assert.Equal(Serialize(root.array), result.body);
        }

        [Fact]
        public void Test12() {
            var result = Get("list");
            Assert.Equal(Serialize(root.list), result.body);
        }

        [Fact]
        public void Test13() {
            var result = Get("dictionary");
            Assert.Equal(Serialize(root.dictionary), result.body);
        }

        [Fact]
        public void Test14() {
            var result = Get("array/1");
            Assert.Equal(Serialize(root.array[1]), result.body);
        }

        [Fact]
        public void Test15() {
            var result = Get("list/1");
            Assert.Equal(Serialize(root.list[1]), result.body);
        }

        [Fact]
        public void Test16() {
            var result = Get("dictionary/1");
            Assert.Equal(Serialize(root.dictionary[1]), result.body);
        }
    }
}
