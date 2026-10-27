using System.Collections.Generic;

namespace RRX.Xml.ProjectTree
{
    internal static class RevitBridgeProtocol
    {
        public const string PipeName = "RRX.Xml.RevitBridge";
    }

    internal class RevitBridgeRequest
    {
        public string Command { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public List<string> Components { get; set; } = new();
        public List<string> Uids { get; set; } = new();
    }

    internal class RevitBridgeResponse
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public int Deleted { get; set; }
        public int Affected { get; set; }
        public List<string> Components { get; set; } = new();
    }
}
