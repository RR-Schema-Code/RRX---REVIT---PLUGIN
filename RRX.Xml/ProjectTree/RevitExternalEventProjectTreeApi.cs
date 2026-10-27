#if REVIT_API_AVAILABLE
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public sealed class RevitExternalEventProjectTreeApi : IAsyncRevitProjectTreeApi, IDisposable
    {
        private readonly RevitProjectTreeExternalEventHandler handler;
        private readonly ExternalEvent externalEvent;

        public RevitExternalEventProjectTreeApi(UIApplication application)
        {
            handler = new RevitProjectTreeExternalEventHandler(application);
            externalEvent = ExternalEvent.Create(handler);
        }

        public IReadOnlyList<XElement> ReadComponents(string? filePath = null)
        {
            throw CreateAsyncOnlyException();
        }

        public IReadOnlyList<XElement> CreateOrUpdateComponents(IEnumerable<XElement> rrxComponents)
        {
            throw CreateAsyncOnlyException();
        }

        public int DeleteComponents(IEnumerable<string> uids)
        {
            throw CreateAsyncOnlyException();
        }

        public int UngroupAutomationGroup()
        {
            throw CreateAsyncOnlyException();
        }

        public int CreateAutomationGroup()
        {
            throw CreateAsyncOnlyException();
        }

        public Task<IReadOnlyList<XElement>> ReadComponentsAsync(string? filePath = null)
        {
            return ExecuteAsync(api => api.ReadComponents(filePath));
        }

        public Task<IReadOnlyList<XElement>> CreateOrUpdateComponentsAsync(IEnumerable<XElement> rrxComponents)
        {
            var componentCopies = rrxComponents.Select(component => new XElement(component)).ToList();
            return ExecuteAsync(api => api.CreateOrUpdateComponents(componentCopies));
        }

        public Task<int> DeleteComponentsAsync(IEnumerable<string> uids)
        {
            var uidCopies = uids.ToList();
            return ExecuteAsync(api => api.DeleteComponents(uidCopies));
        }

        public Task<int> UngroupAutomationGroupAsync()
        {
            return ExecuteAsync(api => api.UngroupAutomationGroup());
        }

        public Task<int> CreateAutomationGroupAsync()
        {
            return ExecuteAsync(api => api.CreateAutomationGroup());
        }

        public void Dispose()
        {
            externalEvent.Dispose();
        }

        private Task<TResult> ExecuteAsync<TResult>(Func<AutodeskRevitProjectTreeApi, TResult> action)
        {
            var task = handler.Enqueue(action);
            var request = externalEvent.Raise();
            if (request != ExternalEventRequest.Accepted)
            {
                handler.Cancel(task, $"ExternalEvent.Raise wurde nicht angenommen: {request}");
            }

            return WithTimeoutAsync(task);
        }

        private static async Task<TResult> WithTimeoutAsync<TResult>(Task<TResult> task)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(15));
            var completed = await Task.WhenAny(task, timeout);
            if (completed != task)
            {
                throw new TimeoutException("ExternalEvent wurde nicht innerhalb von 15 Sekunden von Revit ausgeführt. Prüfe, ob das RRX-Fenster modeless geöffnet ist und Revit nicht in einem modalen Dialog steht.");
            }

            return await task;
        }

        private static InvalidOperationException CreateAsyncOnlyException()
        {
            return new InvalidOperationException("Revit-Aufrufe müssen über ExternalEvent asynchron ausgeführt werden.");
        }

        private sealed class RevitProjectTreeExternalEventHandler : IExternalEventHandler
        {
            private readonly UIApplication application;
            private readonly Queue<IRevitProjectTreeRequest> requests = new();
            private readonly object syncRoot = new();

            public RevitProjectTreeExternalEventHandler(UIApplication application)
            {
                this.application = application;
            }

            public Task<TResult> Enqueue<TResult>(Func<AutodeskRevitProjectTreeApi, TResult> action)
            {
                var request = new RevitProjectTreeRequest<TResult>(action);
                lock (syncRoot)
                {
                    requests.Enqueue(request);
                }

                return request.Task;
            }

            public void Cancel<TResult>(Task<TResult> task, string message)
            {
                lock (syncRoot)
                {
                    foreach (var request in requests)
                    {
                        if (request.TaskObject == task)
                        {
                            request.Cancel(message);
                            return;
                        }
                    }
                }
            }

            public void Execute(UIApplication app)
            {
                while (true)
                {
                    IRevitProjectTreeRequest request;
                    lock (syncRoot)
                    {
                        if (requests.Count == 0)
                        {
                            return;
                        }

                        request = requests.Dequeue();
                    }

                    request.Execute(new AutodeskRevitProjectTreeApi(app ?? application));
                }
            }

            public string GetName()
            {
                return "RRX Revit Project Tree API";
            }
        }

        private interface IRevitProjectTreeRequest
        {
            Task TaskObject { get; }
            void Cancel(string message);
            void Execute(AutodeskRevitProjectTreeApi api);
        }

        private sealed class RevitProjectTreeRequest<TResult> : IRevitProjectTreeRequest
        {
            private readonly Func<AutodeskRevitProjectTreeApi, TResult> action;
            private readonly TaskCompletionSource<TResult> completion = new();

            public RevitProjectTreeRequest(Func<AutodeskRevitProjectTreeApi, TResult> action)
            {
                this.action = action;
            }

            public Task<TResult> Task => completion.Task;

            Task IRevitProjectTreeRequest.TaskObject => completion.Task;

            public void Cancel(string message)
            {
                completion.TrySetException(new InvalidOperationException(message));
            }

            public void Execute(AutodeskRevitProjectTreeApi api)
            {
                try
                {
                    completion.TrySetResult(action(api));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }
        }
    }
}
#endif
