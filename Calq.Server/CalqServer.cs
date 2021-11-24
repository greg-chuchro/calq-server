using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ghbvft6.Calq.Server {
    public class CalqServer {

        internal class MessagingException : Exception { }

        private static List<CalqServer> servers = new();
        private static readonly object serversLocker = new();

        public string[] Prefixes { get; set; }
        public HttpListener Listener { get; } = new();

        private readonly object root;

        private readonly JsonSerializerOptions serializerOptions = new() {
            IncludeFields = true
        };
        private readonly JsonReaderOptions readerOptions = new() {
            CommentHandling = JsonCommentHandling.Skip
        };

        static CalqServer() {
            Task.Run(() => {
                while (servers.Count == 0 || servers[0].Listener.IsListening == false) {
                    Thread.Sleep(1);
                }

                var process = Process.GetCurrentProcess();
                var server = new NamedPipeServerStream($"{Environment.CurrentDirectory}/{process.ProcessName}-{process.Id}-calq", PipeDirection.InOut);
                server.WaitForConnection();

                var reader = new StreamReader(server);
                var writer = new StreamWriter(server);
                while (true) {
                    var request = reader.ReadLine()!;
                    var serverIndex = int.Parse(request.Split('/')[0]);
                    var command = request.Split('/')[1];

                    var response = "";
                    switch (command) {
                        case "exit":
                            // FIXME
                            Environment.Exit(0);
                            break;
                        case "rootType":
                            response = servers[serverIndex].root.GetType().FullName;
                            break;
                        case "prefix":
                            response = servers[serverIndex].Prefixes[0];
                            break;
                    }

                    writer.WriteLine(response);
                    writer.Flush();
                }
            });
        }

        public CalqServer(object root) {
            this.root = root;
            Prefixes = Array.Empty<string>();
        }

        ~CalqServer() {
            lock (serversLocker) {
                servers.Remove(this);
            }
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

                var (parentObj, childObj, childObjName) = ResolveRequestResource(request.Url.AbsolutePath);
                var requestBody = new StreamReader(request.InputStream, request.ContentEncoding).ReadToEnd();
                var response = "";

                switch (request.HttpMethod) {
                    case "GET":
                        response = JsonSerializer.Serialize(childObj, serializerOptions);
                        break;
                    case "POST":
                        var newObj = Deserialize(parentObj, childObj, childObjName, requestBody);
                        AddOrAssignObjectToChild(parentObj, childObj, childObjName, newObj);
                        break;
                    case "PUT":
                        var newChildObj = Deserialize(parentObj, childObj, childObjName, requestBody);
                        SetChildObject(parentObj, childObjName, newChildObj);
                        break;
                    case "DELETE":
                        DeleteChildObject(parentObj, childObjName);
                        break;
                    case "PATCH":
                        if (childObj == null) {
                            throw new MessagingException(); // FIXME wrong status
                        }
                        var jsonBytes = Encoding.UTF8.GetBytes(requestBody);
                        var reader = new Utf8JsonReader(jsonBytes, readerOptions);
                        Populate(reader, childObj);
                        break;
                    default:
                        throw new Exception("unknown method");
                }

                return response;
            }

            object? Deserialize(object parentObj, object? childObj, string childObjName, string requestBody) {
                if (childObj is ICollection collection) {
                    if (Reflection.IsPrimitive(collection)) {
                        return Reflection.ParseValue(collection.GetType().GetGenericArguments()[0], requestBody);
                    }
                } else {
                    if (Reflection.IsPrimitive(parentObj, childObjName)) {
                        return Reflection.ParseValue(Reflection.GetFieldOrPropertyType(parentObj, childObjName), requestBody);
                    }
                }
                return JsonSerializer.Deserialize(requestBody, Reflection.GetFieldOrPropertyType(parentObj, childObjName), serializerOptions);
            }

            void AddOrAssignObjectToChild(object parentObj, object? childObj, string childObjName, object? newChildObj) {
                if (childObj is ICollection collection) {
                    Reflection.AddChildValue(collection, newChildObj);
                } else {
                    if (childObj != null) {
                        throw new MessagingException(); // FIXME 409
                    }
                    SetChildObject(parentObj, childObjName, newChildObj);
                }
            }

            void SetChildObject(object parentObj, string childObjName, object? newChildObj) {
                if (parentObj is ICollection collection) {
                    Reflection.SetChildValue(collection, childObjName, newChildObj);
                } else {
                    Reflection.SetFieldOrPropertyValue(parentObj, childObjName, newChildObj);
                }
            }

            void DeleteChildObject(object parentObj, string childObjName) {
                if (parentObj is ICollection collection) {
                    Reflection.DeleteChildValue(collection, childObjName);
                } else {
                    Reflection.SetFieldOrPropertyValue(parentObj, childObjName, null);
                }
            }

            (object parentObj, object? childObj, string childObjName) ResolveRequestResource(string path) {
                var subPaths = path.Split('/');


                object? parentObj = root;
                object? childObj = root;
                var childObjName = "";
                foreach (var subPath in subPaths) {
                    if (subPath == "") {
                        continue;
                    }
                    if (childObj == null) {
                        throw new MessagingException();
                    }

                    parentObj = childObj;
                    childObjName = subPath;
                    if (childObj is ICollection collection) {
                        childObj = Reflection.GetChildValue(collection, subPath);
                    } else {
                        childObj = Reflection.GetFieldOrPropertyValue(childObj, subPath);
                    }
                }

                return (parentObj, childObj, childObjName);
            }

            void Populate(Utf8JsonReader reader, object instance) {
                if (instance == null) {
                    throw new ArgumentException("instance can't be null");
                }

                object? currentInstance = instance;
                var currentType = instance.GetType();
                var instanceStack = new Stack<object>();

                void ReadObject(ref Utf8JsonReader reader) {
                    while (true) {
                        reader.Read();
                        string propertyName;
                        switch (reader.TokenType) {
                            case JsonTokenType.PropertyName:
                                propertyName = reader.GetString()!;
                                break;
                            case JsonTokenType.EndObject:
                                if (instanceStack.Count == 0) {
                                    if (reader.Read()) {
                                        throw new JsonException();
                                    }
                                    return;
                                }
                                currentInstance = instanceStack.Pop();
                                if (currentInstance is not ICollection) {
                                    currentType = currentInstance.GetType();
                                    continue;
                                } else {
                                    currentType = currentInstance.GetType().GetGenericArguments()[0];
                                    return;
                                }
                            default:
                                throw new JsonException();
                        }

                        reader.Read();
                        object? value;
                        switch (reader.TokenType) {
                            case JsonTokenType.False:
                            case JsonTokenType.True:
                                value = reader.GetBoolean();
                                break;
                            case JsonTokenType.String:
                                value = reader.GetString();
                                break;
                            case JsonTokenType.Number:
                                value = reader.GetInt32();
                                break;
                            case JsonTokenType.Null:
                                value = null;
                                break;
                            case JsonTokenType.StartObject:
                                instanceStack.Push(currentInstance);
                                currentInstance = Reflection.GetOrInitializeFieldOrPropertyValue(currentType, currentInstance, propertyName);
                                if (currentInstance == null) {
                                    throw new JsonException();
                                }
                                currentType = currentInstance.GetType();
                                continue;
                            case JsonTokenType.StartArray:
                                instanceStack.Push(currentInstance);
                                value = Reflection.GetOrInitializeFieldOrPropertyValue(currentType, currentInstance, propertyName);
                                if (currentInstance is not ICollection) {
                                    Reflection.SetFieldOrPropertyValue(currentType, currentInstance, propertyName, value);
                                } else {
                                    Reflection.SetChildValue((ICollection)currentInstance, propertyName, value);
                                }
                                currentInstance = value;
                                if (currentInstance == null) {
                                    throw new JsonException();
                                }
                                currentType = currentInstance.GetType().GetGenericArguments()[0];
                                ReadArray(ref reader);
                                continue;
                            default:
                                throw new JsonException();
                        }
                        if (currentInstance is not ICollection) {
                            Reflection.SetFieldOrPropertyValue(currentType, currentInstance, propertyName, value);
                        } else {
                            Reflection.SetChildValue((ICollection)currentInstance, propertyName, value);
                        }
                    }
                }

                void ReadArray(ref Utf8JsonReader reader) {
                    while (true) {
                        reader.Read();
                        object? value;
                        switch (reader.TokenType) {
                            case JsonTokenType.False:
                            case JsonTokenType.True:
                                value = reader.GetBoolean();
                                break;
                            case JsonTokenType.String:
                                value = reader.GetString();
                                break;
                            case JsonTokenType.Number:
                                value = reader.GetInt32();
                                break;
                            case JsonTokenType.Null:
                                value = null;
                                break;
                            case JsonTokenType.StartObject:
                                instanceStack.Push(currentInstance);
                                value = Activator.CreateInstance(currentType); // FIXME
                                Reflection.AddChildValue((ICollection)currentInstance, value);
                                currentInstance = value;
                                if (currentInstance == null) {
                                    throw new JsonException();
                                }
                                currentType = currentInstance.GetType();
                                ReadObject(ref reader);
                                continue;
                            case JsonTokenType.StartArray:
                                instanceStack.Push(currentInstance);
                                value = Activator.CreateInstance(currentType); // FIXME
                                Reflection.AddChildValue((ICollection)currentInstance, value);
                                currentInstance = value;
                                if (currentInstance == null) {
                                    throw new JsonException();
                                }
                                currentType = currentInstance.GetType().GetGenericArguments()[0];
                                continue;
                            case JsonTokenType.EndArray:
                                if (instanceStack.Count == 0) {
                                    if (reader.Read()) {
                                        throw new JsonException();
                                    }
                                    return;
                                }
                                currentInstance = instanceStack.Pop();
                                if (currentInstance is not ICollection) {
                                    currentType = currentInstance.GetType();
                                    return;
                                } else {
                                    currentType = currentInstance.GetType().GetGenericArguments()[0];
                                    continue;
                                }
                                break;
                            default:
                                throw new JsonException();
                        }
                        Reflection.AddChildValue((ICollection)currentInstance, value);
                    }
                }

                reader.Read();
                switch (reader.TokenType) {
                    case JsonTokenType.StartObject:
                        ReadObject(ref reader);
                        break;
                    case JsonTokenType.StartArray:
                        ReadArray(ref reader);
                        break;
                    default:
                        throw new JsonException();
                }
            }

            foreach (var prefix in Prefixes) {
                Listener.Prefixes.Add(prefix);
            }
            Listener.Start();
            lock (serversLocker) {
                servers.Add(this);
            }

            while (Listener.IsListening) {
                var result = Listener.BeginGetContext(new AsyncCallback(ListenerCallback), Listener);
                result.AsyncWaitHandle.WaitOne();
            }
        }
    }
}
