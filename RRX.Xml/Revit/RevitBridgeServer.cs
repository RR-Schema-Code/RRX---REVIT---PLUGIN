#if REVIT_API_AVAILABLE
using Autodesk.Revit.UI;
using RRX.Xml.ProjectTree;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;

namespace RRX.Xml.Revit
{
    internal static class RevitBridgeServer
    {
        private static readonly object Gate = new();
        private static RevitBridgeExternalEventHandler? handler;
        private static ExternalEvent? externalEvent;
        private static Thread? serverThread;

        public static void Start()
        {
            lock (Gate)
            {
                if (serverThread?.IsAlive == true)
                {
                    return;
                }

                handler = new RevitBridgeExternalEventHandler();
                externalEvent = ExternalEvent.Create(handler);
                serverThread = new Thread(RunServer)
                {
                    IsBackground = true,
                    Name = "RRX Revit Bridge"
                };
                serverThread.Start();
            }
        }

        private static void RunServer()
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(RevitBridgeProtocol.PipeName, PipeDirection.InOut, 1);
                    pipe.WaitForConnection();

                    using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

                    var requestJson = reader.ReadLine();
                    var request = string.IsNullOrWhiteSpace(requestJson)
                        ? null
                        : JsonSerializer.Deserialize<RevitBridgeRequest>(requestJson);

                    var response = request == null
                        ? Fail("Leere Anfrage an die Revit-Bridge.")
                        : InvokeInRevit(request);

                    writer.WriteLine(JsonSerializer.Serialize(response));
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }

        private static RevitBridgeResponse InvokeInRevit(RevitBridgeRequest request)
        {
            if (handler == null || externalEvent == null)
            {
                return Fail("Revit-Bridge ist nicht gestartet.");
            }

            var workItem = new RevitBridgeWorkItem(request);
            handler.Enqueue(workItem);

            var eventRequest = externalEvent.Raise();
            if (eventRequest != ExternalEventRequest.Accepted && eventRequest != ExternalEventRequest.Pending)
            {
                return Fail($"Revit-Bridge konnte den Auftrag nicht starten: {eventRequest}");
            }

            return workItem.Wait(TimeSpan.FromSeconds(60))
                ? workItem.Response
                : Fail("Zeitüberschreitung bei der Revit-Bridge.");
        }

        private static RevitBridgeResponse Fail(string message)
        {
            return new RevitBridgeResponse
            {
                Success = false,
                Error = message
            };
        }
    }

    internal class RevitBridgeExternalEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<RevitBridgeWorkItem> queue = new();

        public void Enqueue(RevitBridgeWorkItem workItem)
        {
            queue.Enqueue(workItem);
        }

        public void Execute(UIApplication application)
        {
            while (queue.TryDequeue(out var workItem))
            {
                try
                {
                    workItem.Complete(ExecuteRequest(application, workItem.Request));
                }
                catch (Exception exception)
                {
                    workItem.Complete(new RevitBridgeResponse
                    {
                        Success = false,
                        Error = exception.Message
                    });
                }
            }
        }

        public string GetName()
        {
            return "RRX Revit Bridge";
        }

        private static RevitBridgeResponse ExecuteRequest(UIApplication application, RevitBridgeRequest request)
        {
            var api = new AutodeskRevitProjectTreeApi(application);

            return request.Command switch
            {
                "Read" => Components(api.ReadComponents(request.FilePath)),
                "CreateOrUpdate" => Components(api.CreateOrUpdateComponents(ParseComponents(request.Components))),
                "Delete" => new RevitBridgeResponse
                {
                    Success = true,
                    Deleted = api.DeleteComponents(request.Uids)
                },
                "UngroupAutomationGroup" => new RevitBridgeResponse
                {
                    Success = true,
                    Affected = api.UngroupAutomationGroup()
                },
                "CreateAutomationGroup" => new RevitBridgeResponse
                {
                    Success = true,
                    Affected = api.CreateAutomationGroup()
                },
                _ => new RevitBridgeResponse
                {
                    Success = false,
                    Error = $"Unbekannter Revit-Bridge-Befehl: {request.Command}"
                }
            };
        }

        private static RevitBridgeResponse Components(IReadOnlyList<XElement> components)
        {
            return new RevitBridgeResponse
            {
                Success = true,
                Components = components.Select(component => component.ToString(SaveOptions.DisableFormatting)).ToList()
            };
        }

        private static IEnumerable<XElement> ParseComponents(IEnumerable<string> components)
        {
            return components.Select(XElement.Parse);
        }
    }

    internal class RevitBridgeWorkItem
    {
        private readonly ManualResetEventSlim completed = new();

        public RevitBridgeWorkItem(RevitBridgeRequest request)
        {
            Request = request;
        }

        public RevitBridgeRequest Request { get; }

        public RevitBridgeResponse Response { get; private set; } = new()
        {
            Success = false,
            Error = "Revit-Bridge-Auftrag wurde nicht ausgeführt."
        };

        public void Complete(RevitBridgeResponse response)
        {
            Response = response;
            completed.Set();
        }

        public bool Wait(TimeSpan timeout)
        {
            return completed.Wait(timeout);
        }
    }
}
#endif
