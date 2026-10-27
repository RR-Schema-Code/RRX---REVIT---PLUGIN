using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public class RevitBridgeProjectTreeApi : IRevitProjectTreeApi
    {
        private readonly RevitProjectTreeApi fileApi = new();

        public IReadOnlyList<XElement> ReadComponents(string? filePath = null)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && !Path.GetExtension(filePath).Equals(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                return fileApi.ReadComponents(filePath);
            }

            var response = Send(new RevitBridgeRequest
            {
                Command = "Read",
                FilePath = filePath
            });

            return response.Components.Select(XElement.Parse).ToList();
        }

        public IReadOnlyList<XElement> CreateOrUpdateComponents(IEnumerable<XElement> rrxComponents)
        {
            var response = Send(new RevitBridgeRequest
            {
                Command = "CreateOrUpdate",
                Components = rrxComponents.Select(component => component.ToString(SaveOptions.DisableFormatting)).ToList()
            });

            return response.Components.Select(XElement.Parse).ToList();
        }

        public int DeleteComponents(IEnumerable<string> uids)
        {
            var response = Send(new RevitBridgeRequest
            {
                Command = "Delete",
                Uids = uids.Where(uid => !string.IsNullOrWhiteSpace(uid)).ToList()
            });

            return response.Deleted;
        }

        public int UngroupAutomationGroup()
        {
            var response = Send(new RevitBridgeRequest
            {
                Command = "UngroupAutomationGroup"
            });

            return response.Affected;
        }

        public int CreateAutomationGroup()
        {
            var response = Send(new RevitBridgeRequest
            {
                Command = "CreateAutomationGroup"
            });

            return response.Affected;
        }

        private static RevitBridgeResponse Send(RevitBridgeRequest request)
        {
            using var pipe = new NamedPipeClientStream(".", RevitBridgeProtocol.PipeName, PipeDirection.InOut);
            try
            {
                pipe.Connect(750);
            }
            catch (TimeoutException exception)
            {
                throw new InvalidOperationException("Keine Verbindung zur Revit-Bridge. Bitte Revit öffnen und das RRX-Add-in einmal starten.", exception);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException("Keine Verbindung zur Revit-Bridge. Bitte Revit öffnen und das RRX-Add-in einmal starten.", exception);
            }

            using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

            writer.WriteLine(JsonSerializer.Serialize(request));
            var responseJson = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new IOException("Keine Antwort von der Revit-Bridge erhalten. Bitte Revit 2027 neu starten und prüfen, ob das RRX-Add-in ohne Ladefehler startet.");
            }

            var response = JsonSerializer.Deserialize<RevitBridgeResponse>(responseJson)
                ?? throw new IOException("Antwort der Revit-Bridge konnte nicht gelesen werden.");

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error);
            }

            return response;
        }
    }
}
