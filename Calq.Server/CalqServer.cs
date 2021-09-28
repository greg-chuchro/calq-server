using System;
using System.Collections;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Calq.Server {
    public class CalqServer {

        internal class MessagingException : Exception { }

        public string[] Prefixes { get; set; }

        private readonly object root;

        private readonly JsonSerializerOptions serializerOptions = new() {
            IncludeFields = true
        };

        public CalqServer(object root) {
            this.root = root;
        }

        public void Start() {

            void ListenerCallback(IAsyncResult result) {
                var listener = (HttpListener)result.AsyncState!;
                var context = listener.EndGetContext(result);
                var response = context.Response;

                string responseBody;
                try {
                    responseBody = GetResponseString(context.Request);
                } catch (MessagingException ex) {
                    responseBody = ex.ToString();
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                } catch (Exception ex) {
                    responseBody = ex.ToString();
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }

                var buffer = Encoding.UTF8.GetBytes(responseBody);
                response.ContentLength64 = buffer.Length;
                using var output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
            }

            string GetResponseString(HttpListenerRequest request) {
                if (request.Url == null) {
                    throw new Exception("invalid url");
                }

                var obj = GetObjByPath(request.Url.AbsolutePath);

                switch (request.HttpMethod) {
                    case "GET":
                        return JsonSerializer.Serialize(obj, serializerOptions);
                    default:
                        throw new Exception("unknown method");
                }

                throw new Exception();
            }

            object GetObjByPath(string path) {
                var subPaths = path.Split('/');

                object? obj = root;
                foreach (var subPath in subPaths) {
                    if (subPath == "") {
                        continue;
                    }
                    if (obj == null) {
                        throw new MessagingException();
                    }

                    if (obj is ICollection collection) {
                        obj = Reflection.GetChildValue(collection, subPath);
                    } else {
                        obj = Reflection.GetFieldOrPropertyValue(obj, subPath);
                    }
                }

                return obj;
            }

            var listener = new HttpListener();
            foreach (var prefix in Prefixes) {
                listener.Prefixes.Add(prefix);
            }
            listener.Start();

            while (true) {
                var result = listener.BeginGetContext(new AsyncCallback(ListenerCallback), listener);
                result.AsyncWaitHandle.WaitOne();
            }
        }
    }
}
