using Calq.Server;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Xunit;

namespace Calq.ServerTest {

    public abstract class CalqServerTestBase {

        protected HttpClient client = new();
        protected TestService root = new();

        protected static int serverCount = 0;
        protected static readonly object serverCountLocker = new();
        protected readonly static NamedPipeClientStream pipeClient;

        protected int serverIndex;

        static CalqServerTestBase() {
            var process = Process.GetCurrentProcess();
            pipeClient = new NamedPipeClientStream($"{Environment.CurrentDirectory}/{process.ProcessName}-{process.Id}-calq");
        }

        protected CalqServerTestBase() {
            lock (serverCountLocker) {
                serverIndex = serverCount;

                var url = "";

                var server = new CalqServer(root);
                new Thread(() => {
                    var port = 8080;
                    while (server.Listener.IsListening == false) {
                        try {
                            url = $"http://localhost:{port}/";
                            server.Prefixes = new[] { url };
                            server.Start();
                        } catch (HttpListenerException) {
                            ++port;
                            server = new CalqServer(root);
                        }
                    }
                }).Start();

                while (server.Listener.IsListening == false) {
                    Thread.Sleep(1);
                }
                client.BaseAddress = new Uri(url);

                ++serverCount;

                if (serverCount == 1) {
                    pipeClient.Connect();
                }
            }
        }

        protected (string, HttpStatusCode) Send(HttpMethod method, string uri, string body = "") {
            var request = new HttpRequestMessage(method, uri);
            request.Content = new StringContent(body);
            var response = client.Send(request);
            return (new StreamReader(response.Content.ReadAsStream()).ReadToEnd(), response.StatusCode);
        }

        protected (string body, HttpStatusCode status) Get(string uri) {
            return Send(HttpMethod.Get, uri);
        }

        protected (string body, HttpStatusCode status) Post(string uri, string body) {
            return Send(HttpMethod.Post, uri, body);
        }

        protected (string body, HttpStatusCode status) Put(string uri, string body) {
            return Send(HttpMethod.Put, uri, body);
        }

        protected (string body, HttpStatusCode status) Delete(string uri) {
            return Send(HttpMethod.Delete, uri);
        }

        protected (string body, HttpStatusCode status) Patch(string uri, string body) {
            return Send(HttpMethod.Patch, uri, body);
        }

        protected string Serialize(object instance) {
            var serializerOptions = new JsonSerializerOptions {
                IncludeFields = true
            };
            return JsonSerializer.Serialize(instance, serializerOptions);
        }
    }

    [Collection("Sequential")]
    public class CalqServerRootTest : CalqServerTestBase {

        [Fact]
        public void Test0() {
            Assert.NotNull(root.nested);
            Assert.Null(root.nullNested);
            Assert.Equal(0, root.integer);
            Assert.NotEqual(root.list[0], root.list[1]);
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
    }

    [Collection("Sequential")]
    public class CalqServerTest : CalqServerTestBase {

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
            var result = Get("nullText");
            Assert.Equal(Serialize(root.nullText), result.body);
        }

        [Fact]
        public void Test7() {
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

        [Fact]
        public void Test17() {
            var newNested = new TestService.Nested();
            var result = Post("nullNested", Serialize(newNested));
            Assert.Equal(Serialize(newNested), Serialize(root.nullNested));
        }

        [Fact]
        public void Test18() {
            var size = root.list.Count;

            var result = Post("list", Serialize(5));
            Assert.Equal(size + 1, root.list.Count);
            Assert.Equal(Serialize(5), Serialize(root.list[^1]));
        }

        [Fact]
        public void Test19() {
            var newNested = new TestService.Nested();
            var result = Post("nested", Serialize(newNested));
            Assert.Equal(HttpStatusCode.NotFound, result.status); // FIXME 409
        }

        [Fact]
        public void Test20() {
            var result = Post("integer", Serialize(5));
            Assert.Equal(HttpStatusCode.NotFound, result.status); // FIXME 409
        }

        [Fact]
        public void Test21() {
            var result = Put("integer", Serialize(5));
            Assert.Equal(5, root.integer);
        }

        [Fact]
        public void Test22() {
            var secondValue = root.list[1];
            var size = root.list.Count;

            var result = Delete("list/0");
            Assert.Equal(size - 1, root.list.Count);
            Assert.Equal(Serialize(secondValue), Serialize(root.list[0]));
        }

        [Fact]
        public void Test23() {
            var result = Delete("nested");
            Assert.Null(root.nested);
        }

        [Fact]
        public void Test24() {
            var newNested = new TestService.Nested();
            var result = Patch("nullNested", Serialize(newNested));
            Assert.Equal(HttpStatusCode.NotFound, result.status); // FIXME wrong status
        }

        [Fact]
        public void Test25() {
            var newNested = new TestService.Nested();
            newNested.b = 5;
            var result = Patch("nested", $"{{ \"b\": {newNested.b} }}");
            Assert.Equal(Serialize(newNested), Serialize(root.nested));
        }

        [Fact]
        public void Test26() {
            var reader = new StreamReader(pipeClient);
            var writer = new StreamWriter(pipeClient);
            writer.WriteLine($"{serverIndex}/rootType");
            writer.Flush();
            Assert.Equal(root.GetType().FullName, reader.ReadLine());
        }

        [Fact]
        public void Test27() {
            var reader = new StreamReader(pipeClient);
            var writer = new StreamWriter(pipeClient);
            writer.WriteLine($"{serverIndex}/prefix");
            writer.Flush();
            Assert.Equal(client.BaseAddress.AbsoluteUri, reader.ReadLine());
        }

        [Fact]
        public void Test28() {
            var newFirstValue = new TestService.Nested();
            newFirstValue.b = root.listOfObjects[0].b + 1;
            var result = Patch("listOfObjects/0", Serialize(newFirstValue));
            Assert.Equal(Serialize(newFirstValue), Serialize(root.listOfObjects[0]));
        }
    }
}
