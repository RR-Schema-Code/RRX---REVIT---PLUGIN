using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public interface IRevitProjectTreeApi
    {
        IReadOnlyList<XElement> ReadComponents(string? filePath = null);
        IReadOnlyList<XElement> CreateOrUpdateComponents(IEnumerable<XElement> rrxComponents);
        int DeleteComponents(IEnumerable<string> uids);
        int UngroupAutomationGroup();
        int CreateAutomationGroup();
    }

    public interface IAsyncRevitProjectTreeApi : IRevitProjectTreeApi
    {
        Task<IReadOnlyList<XElement>> ReadComponentsAsync(string? filePath = null);
        Task<IReadOnlyList<XElement>> CreateOrUpdateComponentsAsync(IEnumerable<XElement> rrxComponents);
        Task<int> DeleteComponentsAsync(IEnumerable<string> uids);
        Task<int> UngroupAutomationGroupAsync();
        Task<int> CreateAutomationGroupAsync();
    }

    public class RevitProjectTreeApi : IRevitProjectTreeApi
    {
        private readonly List<XElement> components = new();

        public IReadOnlyList<XElement> ReadComponents(string? filePath = null)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var extension = Path.GetExtension(filePath);
                if (extension.Equals(".rvt", System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new System.NotSupportedException("RVT-Dateien können nur im Revit-Add-in über die Autodesk Revit API gelesen werden.");
                }

                var document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
                return document.Descendants("cmp").Select(component => new XElement(component)).ToList();
            }

            return components.Select(component => new XElement(component)).ToList();
        }

        public IReadOnlyList<XElement> CreateOrUpdateComponents(IEnumerable<XElement> rrxComponents)
        {
            var writtenComponents = new List<XElement>();

            foreach (var component in rrxComponents)
            {
                var componentCopy = new XElement(component);
                var uid = componentCopy.Attribute("uid")?.Value ?? string.Empty;
                var existingIndex = components.FindIndex(item => string.Equals(item.Attribute("uid")?.Value, uid, System.StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    components[existingIndex] = componentCopy;
                }
                else
                {
                    components.Add(componentCopy);
                }

                writtenComponents.Add(new XElement(componentCopy));
            }

            return writtenComponents;
        }

        public int DeleteComponents(IEnumerable<string> uids)
        {
            var uidSet = uids
                .Where(uid => !string.IsNullOrWhiteSpace(uid))
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            return components.RemoveAll(component => uidSet.Contains(component.Attribute("uid")?.Value ?? string.Empty));
        }

        public int UngroupAutomationGroup()
        {
            return 0;
        }

        public int CreateAutomationGroup()
        {
            return components.Count;
        }

    }
}
