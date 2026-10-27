#if REVIT_API_AVAILABLE
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public class AutodeskRevitProjectTreeApi : IRevitProjectTreeApi
    {
        private const string ApplicationId = "RRX.Xml.ProjectTree";
        private const string PlaceholderFamilyName = "Gebäudeautomation";
        private const string PlaceholderFamilyTypeName = "Kugel 50mm";
        private const string PlaceholderDirectShapeTypeName = "Gebäudeautomation";
        private const string AutomationGroupName = "Gebäudeautomation";
        private const string PlaceholderMaterialName = "RRX Grün";
        private const string RrxScheduleName = "RRX Gebäudeautomation";
        private const double PlaceholderDiameterMeters = 0.05;
        private const double PlaceholderSpacingMeters = 0.075;
        private static readonly Guid StorageSchemaId = new("7A55E9D8-3571-4F5B-B066-D95603970328");
        private static readonly string[] BaseVisibleProjectParameterNames =
        {
            "RRX-UID",
            "RRX-Key",
            "RRX-Label",
            "RRX-Typ",
            "RRX-Funktion"
        };
        private static readonly string[] DeprecatedVisibleProjectParameterNames =
        {
            "RRX-Datapoints",
            "RRX-Wires",
            "RRX-Articles",
            "RRX-Zubehör",
            "RRX-bid",
            "bid",
            "Key",
            "Label",
            "Typ",
            "Funktion",
            "ASP Key",
            "ASP Label",
            "System Key",
            "System Label"
        };
        private readonly UIApplication application;
        private Document Document => application.ActiveUIDocument.Document;

        public AutodeskRevitProjectTreeApi(UIApplication application)
        {
            this.application = application;
        }

        public IReadOnlyList<XElement> ReadComponents(string? filePath = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return ReadComponents(Document);
            }

            var extension = Path.GetExtension(filePath);
            if (!extension.Equals(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                var xmlDocument = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
                return xmlDocument.Descendants("cmp").Select(component => new XElement(component)).ToList();
            }

            if (!string.Equals(Document.PathName, filePath, StringComparison.OrdinalIgnoreCase))
            {
                application.OpenAndActivateDocument(filePath);
            }

            return ReadComponents(Document);
        }

        private IReadOnlyList<XElement> ReadComponents(Document sourceDocument)
        {
            return new FilteredElementCollector(sourceDocument)
                .WhereElementIsNotElementType()
                .Where(IsReadableModelElement)
                .Select(ToComponentXml)
                .ToList();
        }

        public IReadOnlyList<XElement> CreateOrUpdateComponents(IEnumerable<XElement> rrxComponents)
        {
            var componentCopies = rrxComponents.Select(component => new XElement(component)).ToList();
            var writtenComponents = new List<XElement>();
            var visibleParameterNames = CreateVisibleProjectParameterNames(componentCopies);
            var nextPlaceholderIndex = CountRrxPlaceholders();
            var placeholderSymbol = TryEnsurePlaceholderFamilySymbol();

            using var transaction = new Transaction(Document, "RRX nach Revit abgleichen");
            transaction.Start();
            SuppressTransactionWarnings(transaction);

            try
            {
                EnsureVisibleRrxProjectParameters(visibleParameterNames);
                Document.Regenerate();
                EnsureRrxSchedule(visibleParameterNames);

                foreach (var componentCopy in componentCopies)
                {
                    var uid = ReadComponentUid(componentCopy);
                    if (string.IsNullOrWhiteSpace(uid))
                    {
                        continue;
                    }

                    var element = FindElementByUid(uid);
                    if (element == null)
                    {
                        element = CreatePlaceholderElement(componentCopy, nextPlaceholderIndex++, placeholderSymbol);
                    }

                    WriteComponentProperties(element, componentCopy, visibleParameterNames);
                    writtenComponents.Add(ToComponentXml(element, componentCopy));
                }

                transaction.Commit();
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }

            return writtenComponents;
        }

        public int DeleteComponents(IEnumerable<string> uids)
        {
            var elementIds = uids
                .Where(uid => !string.IsNullOrWhiteSpace(uid))
                .Select(FindElementByUid)
                .Where(element => element != null)
                .Select(element => element!.Id)
                .Distinct()
                .ToList();

            if (elementIds.Count == 0)
            {
                return 0;
            }

            using var transaction = new Transaction(Document, "RRX Revit-Elemente entfernen");
            transaction.Start();
            var deleted = Document.Delete(elementIds).Count;
            transaction.Commit();

            return deleted;
        }

        public int UngroupAutomationGroup()
        {
            using var transaction = new Transaction(Document, "RRX Gebäudeautomation-Gruppe lösen");
            transaction.Start();
            SuppressTransactionWarnings(transaction);

            try
            {
                var ungrouped = UngroupAutomationGroups();
                transaction.Commit();
                return ungrouped;
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }
        }

        public int CreateAutomationGroup()
        {
            using var transaction = new Transaction(Document, "RRX Gebäudeautomation-Gruppe erzeugen");
            transaction.Start();
            SuppressTransactionWarnings(transaction);

            try
            {
                UngroupAutomationGroups();
                Document.Regenerate();

                var elementIds = new FilteredElementCollector(Document)
                    .WhereElementIsNotElementType()
                    .Where(IsRrxPlaceholder)
                    .Select(element => element.Id)
                    .Distinct()
                    .ToList();

                if (elementIds.Count == 0)
                {
                    transaction.Commit();
                    return 0;
                }

                var group = Document.Create.NewGroup(elementIds);
                TryNameAutomationGroup(group);

                transaction.Commit();
                return elementIds.Count;
            }
            catch
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                {
                    transaction.RollBack();
                }

                throw;
            }
        }

        private int UngroupAutomationGroups()
        {
            var groups = new FilteredElementCollector(Document)
                .OfClass(typeof(Group))
                .Cast<Group>()
                .Where(IsAutomationGroup)
                .ToList();

            foreach (var group in groups)
            {
                group.UngroupMembers();
            }

            return groups.Count;
        }

        private static bool IsAutomationGroup(Group group)
        {
            return string.Equals(group.Name, AutomationGroupName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(group.GroupType?.Name, AutomationGroupName, StringComparison.OrdinalIgnoreCase);
        }

        private void TryNameAutomationGroup(Group group)
        {
            try
            {
                group.GroupType.Name = AutomationGroupName;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                var unusedExistingType = new FilteredElementCollector(Document)
                    .OfClass(typeof(GroupType))
                    .Cast<GroupType>()
                    .FirstOrDefault(type => string.Equals(type.Name, AutomationGroupName, StringComparison.OrdinalIgnoreCase)
                        && new FilteredElementCollector(Document)
                            .OfClass(typeof(Group))
                            .Cast<Group>()
                            .All(existingGroup => existingGroup.GroupType?.Id != type.Id));

                if (unusedExistingType != null)
                {
                    Document.Delete(unusedExistingType.Id);
                    group.GroupType.Name = AutomationGroupName;
                }
            }
        }

        private Element? FindElementByUid(string uid)
        {
            return new FilteredElementCollector(Document)
                .WhereElementIsNotElementType()
                .Where(element => element is not Group)
                .FirstOrDefault(element => string.Equals(ReadUid(element), uid, StringComparison.OrdinalIgnoreCase));
        }

        private Element CreatePlaceholderElement(XElement component, int placeholderIndex, FamilySymbol? placeholderSymbol)
        {
            var location = CreatePlaceholderLocation(placeholderIndex);
            if (placeholderSymbol != null)
            {
                if (!placeholderSymbol.IsActive)
                {
                    placeholderSymbol.Activate();
                    Document.Regenerate();
                }

                return Document.Create.NewFamilyInstance(location, placeholderSymbol, StructuralType.NonStructural);
            }

            var uid = ReadComponentUid(component);
            var directShape = DirectShape.CreateElement(Document, new ElementId(BuiltInCategory.OST_GenericModel));
            directShape.ApplicationId = ApplicationId;
            directShape.ApplicationDataId = uid;
            directShape.Name = ReadAttribute(component, "g.lb", ReadAttribute(component, "fnc", uid));
            directShape.SetTypeId(GetOrCreatePlaceholderDirectShapeType().Id);
            directShape.SetShape(CreateSphereGeometry(Document, location));
            return directShape;
        }

        private DirectShapeType GetOrCreatePlaceholderDirectShapeType()
        {
            var categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
            var existingType = new FilteredElementCollector(Document)
                .OfClass(typeof(DirectShapeType))
                .Cast<DirectShapeType>()
                .FirstOrDefault(type =>
                    string.Equals(type.Name, PlaceholderDirectShapeTypeName, StringComparison.OrdinalIgnoreCase)
                    && type.Category?.Id == categoryId);

            if (existingType != null)
            {
                return existingType;
            }

            return DirectShapeType.Create(Document, PlaceholderDirectShapeTypeName, categoryId);
        }

        private FamilySymbol? TryEnsurePlaceholderFamilySymbol()
        {
            try
            {
                return EnsurePlaceholderFamilySymbol();
            }
            catch
            {
                return null;
            }
        }

        private FamilySymbol EnsurePlaceholderFamilySymbol()
        {
            var existingSymbol = FindPlaceholderFamilySymbol();
            if (existingSymbol != null)
            {
                return existingSymbol;
            }

            var templatePath = FindGenericModelTemplatePath();
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                throw new InvalidOperationException(
                    $"Die Familienvorlage fuer '{PlaceholderFamilyName}' wurde nicht gefunden. " +
                    $"Bitte pruefe den Revit FamilyTemplatePath: {application.Application.FamilyTemplatePath}");
            }

            Document? familyDocument = application.Application.NewFamilyDocument(templatePath);
            try
            {
                using (var familyTransaction = new Transaction(familyDocument, "RRX Kugel-Familie erzeugen"))
                {
                    familyTransaction.Start();

                    TrySetFamilyTypeName(familyDocument);
                    foreach (var solid in CreateSphereGeometry(familyDocument, XYZ.Zero).OfType<Solid>())
                    {
                        FreeFormElement.Create(familyDocument, solid);
                    }

                    familyTransaction.Commit();
                }

                var familyPath = Path.Combine(Path.GetTempPath(), "RRX.Revit", $"{PlaceholderFamilyName}.rfa");
                Directory.CreateDirectory(Path.GetDirectoryName(familyPath)!);
                familyDocument.SaveAs(familyPath, new SaveAsOptions { OverwriteExistingFile = true });

                Family? family = null;
                var loadOptions = new RrxFamilyLoadOptions();
                familyDocument.Close(false);
                familyDocument = null;

                if (!Document.LoadFamily(familyPath, loadOptions, out family))
                {
                    family = FindPlaceholderFamily();
                }

                if (family == null)
                {
                    throw new InvalidOperationException(
                        $"Die Familie '{PlaceholderFamilyName}' konnte nicht in das aktive Revit-Modell geladen werden. " +
                        $"Vorlage: {templatePath}; erzeugte RFA: {familyPath}");
                }

                var symbol = family.GetFamilySymbolIds()
                    .Select(id => Document.GetElement(id))
                    .OfType<FamilySymbol>()
                    .FirstOrDefault();

                return symbol ?? throw new InvalidOperationException($"Die Familie '{PlaceholderFamilyName}' enthaelt keinen ladbaren Typ.");
            }
            finally
            {
                familyDocument?.Close(false);
            }
        }

        private FamilySymbol? FindPlaceholderFamilySymbol()
        {
            return new FilteredElementCollector(Document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(symbol =>
                    IsGenericModelElementType(symbol)
                    && (string.Equals(symbol.FamilyName, PlaceholderFamilyName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(symbol.Name, PlaceholderFamilyTypeName, StringComparison.OrdinalIgnoreCase)
                        || symbol.FamilyName.Contains("Gebäudeautomation", StringComparison.OrdinalIgnoreCase)
                        || symbol.Name.Contains("Gebäudeautomation", StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsGenericModelElementType(ElementType elementType)
        {
            return elementType.Category?.Id == new ElementId(BuiltInCategory.OST_GenericModel);
        }

        private Family? FindPlaceholderFamily()
        {
            return new FilteredElementCollector(Document)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .FirstOrDefault(family => string.Equals(family.Name, PlaceholderFamilyName, StringComparison.OrdinalIgnoreCase));
        }

        private string? FindGenericModelTemplatePath()
        {
            var templateRoot = application.Application.FamilyTemplatePath;
            if (string.IsNullOrWhiteSpace(templateRoot) || !Directory.Exists(templateRoot))
            {
                return null;
            }

            var preferredNames = new[]
            {
                "Metric Generic Model.rft",
                "Generic Model.rft",
                "M_Generic Model.rft",
                "M_Allgemeines Modell.rft",
                "Allgemeines Modell.rft"
            };

            foreach (var preferredName in preferredNames)
            {
                var match = Directory.EnumerateFiles(templateRoot, preferredName, SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }

            return Directory.EnumerateFiles(templateRoot, "*.rft", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                    Path.GetFileNameWithoutExtension(path).Contains("Generic Model", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(path).Contains("Allgemeines Modell", StringComparison.OrdinalIgnoreCase));
        }

        private static void TrySetFamilyTypeName(Document familyDocument)
        {
            try
            {
                var manager = familyDocument.FamilyManager;
                if (manager.CurrentType == null)
                {
                    manager.NewType(PlaceholderFamilyTypeName);
                }
                else if (!string.Equals(manager.CurrentType.Name, PlaceholderFamilyTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    manager.RenameCurrentType(PlaceholderFamilyTypeName);
                }
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static IList<GeometryObject> CreateSphereGeometry(Document document, XYZ center)
        {
            try
            {
                return CreateDiamondGeometry(document, center);
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.InternalException or ArgumentException or InvalidOperationException)
            {
                return CreateBoxGeometry(document, center);
            }
        }

        private static IList<GeometryObject> CreateDiamondGeometry(Document document, XYZ center)
        {
            var radius = ToInternalLength(PlaceholderDiameterMeters / 2.0);
            var materialId = GetOrCreateGreenMaterial(document);
            var top = center + XYZ.BasisZ * radius;
            var bottom = center - XYZ.BasisZ * radius;
            var front = center + XYZ.BasisY * radius;
            var right = center + XYZ.BasisX * radius;
            var back = center - XYZ.BasisY * radius;
            var left = center - XYZ.BasisX * radius;

            var faces = new[]
            {
                new[] { top, right, front },
                new[] { top, back, right },
                new[] { top, left, back },
                new[] { top, front, left },
                new[] { bottom, front, right },
                new[] { bottom, right, back },
                new[] { bottom, back, left },
                new[] { bottom, left, front }
            };

            var builder = new TessellatedShapeBuilder
            {
                Target = TessellatedShapeBuilderTarget.Mesh,
                Fallback = TessellatedShapeBuilderFallback.Salvage
            };
            builder.OpenConnectedFaceSet(false);

            foreach (var face in faces)
            {
                builder.AddFace(new TessellatedFace(face.ToList(), materialId));
            }

            builder.CloseConnectedFaceSet();
            builder.Build();
            return builder.GetBuildResult().GetGeometricalObjects();
        }

        private static IList<GeometryObject> CreateBoxGeometry(Document document, XYZ center)
        {
            var radius = ToInternalLength(PlaceholderDiameterMeters / 2.0);
            var materialId = GetOrCreateGreenMaterial(document);
            var bottomZ = center.Z - radius;
            var topZ = center.Z + radius;

            var profile = new CurveLoop();
            profile.Append(Line.CreateBound(new XYZ(center.X - radius, center.Y - radius, bottomZ), new XYZ(center.X + radius, center.Y - radius, bottomZ)));
            profile.Append(Line.CreateBound(new XYZ(center.X + radius, center.Y - radius, bottomZ), new XYZ(center.X + radius, center.Y + radius, bottomZ)));
            profile.Append(Line.CreateBound(new XYZ(center.X + radius, center.Y + radius, bottomZ), new XYZ(center.X - radius, center.Y + radius, bottomZ)));
            profile.Append(Line.CreateBound(new XYZ(center.X - radius, center.Y + radius, bottomZ), new XYZ(center.X - radius, center.Y - radius, bottomZ)));

            var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { profile },
                XYZ.BasisZ,
                topZ - bottomZ,
                new SolidOptions(materialId, ElementId.InvalidElementId));

            return new List<GeometryObject> { solid };
        }

        private static ElementId GetOrCreateGreenMaterial(Document document)
        {
            var material = new FilteredElementCollector(document)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(item => string.Equals(item.Name, PlaceholderMaterialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                var materialId = Material.Create(document, PlaceholderMaterialName);
                material = (Material)document.GetElement(materialId);
            }

            material.Color = new Color(0, 180, 70);
            material.Transparency = 0;
            return material.Id;
        }

        private static IReadOnlyList<string> CreateVisibleProjectParameterNames(IReadOnlyList<XElement> components)
        {
            var parameterNames = new List<string>(BaseVisibleProjectParameterNames);
            AddIndexedParameterNames(parameterNames, "RRX-Datapoints", components.Count == 0 ? 0 : components.Max(component => component.Elements("dpt").Count()));
            AddIndexedParameterNames(parameterNames, "RRX-Articles", components.Count == 0 ? 0 : components.Max(component => component.Elements("art").Count()));
            AddIndexedParameterNames(parameterNames, "RRX-Zubehör", components.Count == 0 ? 0 : components.Max(component => component.Elements("add").Count()));
            AddIndexedParameterNames(parameterNames, "RRX-Wires", components.Count == 0 ? 0 : components.Max(component => component.Elements("wir").Count()));
            return parameterNames;
        }

        private static void AddIndexedParameterNames(ICollection<string> parameterNames, string prefix, int count)
        {
            for (var index = 0; index < count; index++)
            {
                parameterNames.Add($"{prefix}[{index}]");
            }
        }

        private void WriteComponentProperties(Element element, XElement component, IReadOnlyList<string> visibleParameterNames)
        {
            TrySetElementPhase(element);
            WriteStoredComponent(element, component);
            WriteParameter(element, "RRX-UID", ReadComponentUid(component));
            WriteParameter(element, "RRX-Key", ReadAttribute(component, "g.ky"));
            WriteParameter(element, "RRX-Label", ReadAttribute(component, "g.lb"));
            WriteParameter(element, "RRX-Typ", ReadAttribute(component, "typ"));
            WriteParameter(element, "RRX-Funktion", ReadAttribute(component, "fnc"));
            WriteIndexedChildParameters(element, visibleParameterNames, "RRX-Datapoints", component.Elements("dpt").ToList());
            WriteIndexedChildParameters(element, visibleParameterNames, "RRX-Articles", component.Elements("art").ToList());
            WriteIndexedChildParameters(element, visibleParameterNames, "RRX-Zubehör", component.Elements("add").ToList());
            WriteIndexedChildParameters(element, visibleParameterNames, "RRX-Wires", component.Elements("wir").ToList());
            WriteParameter(element, "g.sys.ky", ReadAttribute(component, "g.sys.ky"));
            WriteParameter(element, "g.sys.lb", ReadAttribute(component, "g.sys.lb"));
            WriteParameter(element, "g.ky", ReadAttribute(component, "g.ky"));
            WriteParameter(element, "g.lb", ReadAttribute(component, "g.lb"));
            WriteParameter(element, "typ", ReadAttribute(component, "typ"));
            WriteParameter(element, "fnc", ReadAttribute(component, "fnc"));
            WriteParameter(element, "rrx.xml", component.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        }

        private static void WriteIndexedChildParameters(Element element, IReadOnlyList<string> visibleParameterNames, string prefix, IReadOnlyList<XElement> children)
        {
            var indexedParameterNames = visibleParameterNames
                .Where(parameterName => parameterName.StartsWith($"{prefix}[", StringComparison.OrdinalIgnoreCase))
                .ToList();

            for (var index = 0; index < indexedParameterNames.Count; index++)
            {
                var value = index < children.Count ? FormatChildElement(children[index]) : string.Empty;
                WriteParameter(element, indexedParameterNames[index], value);
            }
        }

        private static string FormatChildElements(XElement component, string childName)
        {
            return string.Join(Environment.NewLine, component.Elements(childName).Select(FormatChildElement));
        }

        private static string FormatChildElement(XElement child)
        {
            if (child.Name.LocalName.Equals("art", StringComparison.OrdinalIgnoreCase)
                || child.Name.LocalName.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                return FormatNamedAttributes(child, new[]
                {
                    ("Name", "nam"),
                    ("Beschreibung", "dsc"),
                    ("Hersteller", "mfl"),
                    ("Kuerzel", "mfs"),
                    ("Gruppe", "grp")
                });
            }

            if (child.Name.LocalName.Equals("wir", StringComparison.OrdinalIgnoreCase))
            {
                return FormatWireElement(child);
            }

            var key = ReadAttribute(child, "g.ky", ReadAttribute(child, "key", ReadAttribute(child, "nam")));
            var label = ReadAttribute(child, "g.lb", ReadAttribute(child, "fnl", ReadAttribute(child, "dsc", ReadAttribute(child, "fnc"))));
            var type = ReadAttribute(child, "typ", ReadAttribute(child, "tpg"));
            var function = ReadAttribute(child, "fnc");
            var parts = new[] { key, label, type, function }.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            return string.Join(" | ", parts);
        }

        private static string FormatWireElement(XElement wire)
        {
            var typeParts = new[]
            {
                ReadAttribute(wire, "typ"),
                ReadAttribute(wire, "gge")
            }.Where(value => !string.IsNullOrWhiteSpace(value));

            var values = new List<(string Label, string Value)>
            {
                ("Nummer", ReadAttribute(wire, "g.ky")),
                ("Funktion", ReadAttribute(wire, "fnc")),
                ("Typ", string.Join(" ", typeParts)),
                ("Spannung", ReadAttribute(wire, "vlt")),
                ("Strom", ReadAttribute(wire, "cur")),
                ("Leistung", ReadAttribute(wire, "pwr"))
            };

            return string.Join(" | ", values
                .Where(value => !string.IsNullOrWhiteSpace(value.Value))
                .Select(value => $"{value.Label}: {value.Value}"));
        }

        private static string FormatNamedAttributes(XElement element, IEnumerable<(string Label, string AttributeName)> attributes)
        {
            var parts = attributes
                .Select(attribute => (attribute.Label, Value: ReadAttribute(element, attribute.AttributeName)))
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Value))
                .Select(attribute => $"{attribute.Label}: {attribute.Value}")
                .ToList();

            return string.Join(" | ", parts);
        }

        private void EnsureVisibleRrxProjectParameters(IReadOnlyList<string> visibleParameterNames)
        {
            var genericModelCategory = Document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
            var categorySet = application.Application.Create.NewCategorySet();
            categorySet.Insert(genericModelCategory);
            var binding = application.Application.Create.NewInstanceBinding(categorySet);
            var definitionGroup = GetSharedParameterDefinitionGroup();

            RemoveDeprecatedVisibleProjectParameters(visibleParameterNames);

            foreach (var parameterName in visibleParameterNames)
            {
                var definition = FindProjectParameterDefinition(parameterName)
                    ?? GetOrCreateSharedTextDefinition(definitionGroup, parameterName);
                if (!Document.ParameterBindings.ReInsert(definition, binding, GroupTypeId.IdentityData))
                {
                    Document.ParameterBindings.Insert(definition, binding, GroupTypeId.IdentityData);
                }
            }
        }

        private void EnsureRrxSchedule(IReadOnlyList<string> visibleParameterNames)
        {
            RenameExistingRrxSchedules();
            var schedule = ViewSchedule.CreateSchedule(Document, new ElementId(BuiltInCategory.OST_GenericModel));
            TrySetViewName(schedule, RrxScheduleName);

            TrySetViewTextParameter(schedule, "Unterdisziplin", "RRX");
            TrySetViewTextParameter(schedule, "Sub-Discipline", "RRX");
            TrySetViewTextParameter(schedule, "Kommentare", "RRX");
            TrySetViewTextParameter(schedule, "Comments", "RRX");
            TrySetViewDiscipline(schedule, ViewDiscipline.Coordination);
            TrySetSchedulePhase(schedule);
            schedule.Definition.IsItemized = true;
            ClearScheduleFilters(schedule.Definition);
            ClearScheduleSortGroupFields(schedule.Definition);
            RemoveObsoleteScheduleFields(schedule.Definition, visibleParameterNames);
            foreach (var parameterName in visibleParameterNames)
            {
                AddScheduleField(schedule.Definition, parameterName);
            }

            ApplyScheduleFieldOrder(schedule.Definition, visibleParameterNames);
        }

        private static void ClearScheduleFilters(ScheduleDefinition definition)
        {
            for (var index = definition.GetFilterCount() - 1; index >= 0; index--)
            {
                definition.RemoveFilter(index);
            }
        }

        private static void ClearScheduleSortGroupFields(ScheduleDefinition definition)
        {
            for (var index = definition.GetSortGroupFieldCount() - 1; index >= 0; index--)
            {
                definition.RemoveSortGroupField(index);
            }
        }

        private void RenameExistingRrxSchedules()
        {
            var existingSchedules = new FilteredElementCollector(Document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(view => !view.IsTemplate
                    && string.Equals(view.Name, RrxScheduleName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var schedule in existingSchedules)
            {
                TrySetViewName(schedule, $"{RrxScheduleName} (alt {schedule.Id.Value})");
            }
        }

        private static bool IsGenericModelSchedule(ViewSchedule schedule)
        {
            return schedule.Definition.CategoryId == new ElementId(BuiltInCategory.OST_GenericModel);
        }

        private static void TrySetViewTextParameter(View view, string parameterName, string value)
        {
            var parameter = view.LookupParameter(parameterName);
            if (parameter?.StorageType == StorageType.String && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private static void TrySetViewDiscipline(View view, ViewDiscipline discipline)
        {
            var parameter = view.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
            if (parameter?.StorageType == StorageType.Integer && !parameter.IsReadOnly)
            {
                parameter.Set((int)discipline);
            }
        }

        private void TrySetSchedulePhase(ViewSchedule schedule)
        {
            var phase = FindLastProjectPhase();
            if (phase != null)
            {
                TrySetViewElementIdParameter(schedule, BuiltInParameter.VIEW_PHASE, phase.Id);
            }

            var phaseFilter = FindOpenPhaseFilter();
            if (phaseFilter != null)
            {
                TrySetViewElementIdParameter(schedule, BuiltInParameter.VIEW_PHASE_FILTER, phaseFilter.Id);
            }
        }

        private Phase? FindLastProjectPhase()
        {
            return new FilteredElementCollector(Document)
                .OfClass(typeof(Phase))
                .Cast<Phase>()
                .OrderBy(phase => phase.Id.Value)
                .LastOrDefault();
        }

        private PhaseFilter? FindOpenPhaseFilter()
        {
            var phaseFilters = new FilteredElementCollector(Document)
                .OfClass(typeof(PhaseFilter))
                .Cast<PhaseFilter>()
                .ToList();
            var preferredNames = new[]
            {
                "Show All",
                "Alle anzeigen",
                "Alle einblenden",
                "Alle"
            };

            return preferredNames
                .Select(name => phaseFilters.FirstOrDefault(filter => string.Equals(filter.Name, name, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(filter => filter != null)
                ?? phaseFilters.FirstOrDefault();
        }

        private static void TrySetViewElementIdParameter(View view, BuiltInParameter parameterId, ElementId value)
        {
            var parameter = view.get_Parameter(parameterId);
            if (parameter?.StorageType == StorageType.ElementId && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private void TrySetElementPhase(Element element)
        {
            var phase = FindLastProjectPhase();
            if (phase == null)
            {
                return;
            }

            TrySetElementIdParameter(element, BuiltInParameter.PHASE_CREATED, phase.Id);
            TrySetElementIdParameter(element, BuiltInParameter.PHASE_DEMOLISHED, ElementId.InvalidElementId);
        }

        private static void TrySetElementIdParameter(Element element, BuiltInParameter parameterId, ElementId value)
        {
            var parameter = element.get_Parameter(parameterId);
            if (parameter?.StorageType == StorageType.ElementId && !parameter.IsReadOnly)
            {
                parameter.Set(value);
            }
        }

        private static void ApplyScheduleFieldOrder(ScheduleDefinition definition, IReadOnlyList<string> visibleParameterNames)
        {
            var existingFields = definition.GetFieldOrder()
                .Select(fieldId => new
                {
                    FieldId = fieldId,
                    Name = definition.GetField(fieldId).GetName()
                })
                .ToList();

            var orderedRrxFieldIds = visibleParameterNames
                .Select(parameterName => existingFields.FirstOrDefault(field => string.Equals(field.Name, parameterName, StringComparison.OrdinalIgnoreCase))?.FieldId)
                .Where(fieldId => fieldId != null)
                .Select(fieldId => fieldId!)
                .ToList();

            var otherFieldIds = existingFields
                .Where(field => !visibleParameterNames.Any(parameterName => string.Equals(parameterName, field.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(field => field.FieldId)
                .ToList();

            definition.SetFieldOrder(orderedRrxFieldIds.Concat(otherFieldIds).ToList());
        }

        private static void RemoveObsoleteScheduleFields(ScheduleDefinition definition, IReadOnlyCollection<string> visibleParameterNames)
        {
            var fieldIds = definition.GetFieldOrder().ToList();
            foreach (var fieldId in fieldIds)
            {
                var field = definition.GetField(fieldId);
                var fieldName = field.GetName();
                var shouldRemove = DeprecatedVisibleProjectParameterNames.Any(parameterName =>
                    string.Equals(parameterName, fieldName, StringComparison.OrdinalIgnoreCase))
                    || IsObsoleteIndexedRrxParameter(fieldName, visibleParameterNames);

                if (shouldRemove)
                {
                    definition.RemoveField(fieldId);
                }
            }
        }

        private void AddScheduleField(ScheduleDefinition definition, string parameterName)
        {
            var existingFieldNames = definition.GetFieldOrder()
                .Select(fieldId => definition.GetField(fieldId).GetName())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (existingFieldNames.Contains(parameterName))
            {
                return;
            }

            var schedulableField = definition.GetSchedulableFields()
                .FirstOrDefault(field => string.Equals(field.GetName(Document), parameterName, StringComparison.OrdinalIgnoreCase));
            if (schedulableField != null)
            {
                definition.AddField(schedulableField);
            }
        }

        private static void TrySetViewName(View view, string name)
        {
            try
            {
                view.Name = name;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
            }
        }

        private void RemoveDeprecatedVisibleProjectParameters(IReadOnlyCollection<string> visibleParameterNames)
        {
            var iterator = Document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            var definitionsToRemove = new List<Definition>();
            while (iterator.MoveNext())
            {
                if (DeprecatedVisibleProjectParameterNames.Any(parameterName =>
                    string.Equals(iterator.Key.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                    || IsObsoleteIndexedRrxParameter(iterator.Key.Name, visibleParameterNames))
                {
                    definitionsToRemove.Add(iterator.Key);
                }
            }

            foreach (var definition in definitionsToRemove)
            {
                Document.ParameterBindings.Remove(definition);
            }
        }

        private static bool IsObsoleteIndexedRrxParameter(string parameterName, IReadOnlyCollection<string> visibleParameterNames)
        {
            var isIndexedRrxParameter = parameterName.StartsWith("RRX-Wires[", StringComparison.OrdinalIgnoreCase)
                || parameterName.StartsWith("RRX-Datapoints[", StringComparison.OrdinalIgnoreCase)
                || parameterName.StartsWith("RRX-Articles[", StringComparison.OrdinalIgnoreCase)
                || parameterName.StartsWith("RRX-Zubehör[", StringComparison.OrdinalIgnoreCase);

            return isIndexedRrxParameter
                && !visibleParameterNames.Any(visibleName => string.Equals(visibleName, parameterName, StringComparison.OrdinalIgnoreCase));
        }

        private DefinitionGroup GetSharedParameterDefinitionGroup()
        {
            var oldSharedParameterFile = application.Application.SharedParametersFilename;
            var sharedParameterFile = Path.Combine(Path.GetTempPath(), "RRX.Revit", "RRX.SharedParameters.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(sharedParameterFile)!);
            EnsureSharedParameterFile(sharedParameterFile);

            try
            {
                application.Application.SharedParametersFilename = sharedParameterFile;
                var definitionFile = application.Application.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException($"Die RRX-Shared-Parameter-Datei konnte nicht geoeffnet werden: {sharedParameterFile}");

                return definitionFile.Groups.get_Item("RRX") ?? definitionFile.Groups.Create("RRX");
            }
            finally
            {
                application.Application.SharedParametersFilename = oldSharedParameterFile;
            }
        }

        private static Definition GetOrCreateSharedTextDefinition(DefinitionGroup group, string parameterName)
        {
            var existingDefinition = group.Definitions.get_Item(parameterName);
            if (existingDefinition != null)
            {
                return existingDefinition;
            }

            var options = new ExternalDefinitionCreationOptions(parameterName, SpecTypeId.String.Text)
            {
                Visible = true,
                UserModifiable = true
            };
            return group.Definitions.Create(options);
        }

        private Definition? FindProjectParameterDefinition(string parameterName)
        {
            var iterator = Document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                if (string.Equals(iterator.Key.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return iterator.Key;
                }
            }

            return null;
        }

        private static void EnsureSharedParameterFile(string sharedParameterFile)
        {
            if (File.Exists(sharedParameterFile))
            {
                return;
            }

            File.WriteAllText(
                sharedParameterFile,
                "# This is a Revit shared parameter file.\r\n" +
                "# Do not edit manually.\r\n" +
                "*META\tVERSION\tMINVERSION\r\n" +
                "META\t2\t1\r\n" +
                "*GROUP\tID\tNAME\r\n" +
                "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
        }

        private static void WriteParameter(Element element, string parameterName, string value)
        {
            var parameter = element.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly)
            {
                return;
            }

            if (parameter.StorageType == StorageType.String)
            {
                parameter.Set(value);
            }
        }

        private XElement ToComponentXml(Element element)
        {
            return ToComponentXml(element, null);
        }

        private XElement ToComponentXml(Element element, XElement? sourceComponent)
        {
            var component = sourceComponent == null ? ReadStoredComponent(element) ?? new XElement("cmp") : new XElement(sourceComponent);
            component.SetAttributeValue("uid", ReadUid(element));
            component.SetAttributeValue("bid", null);
            component.SetAttributeValue("revit.id", element.Id.Value.ToString());
            component.SetAttributeValue("revit.uniqueId", element.UniqueId);
            WriteRevitGroupAttributes(component, element);
            component.SetAttributeValue("g.sys.ky", ReadParameter(element, "g.sys.ky", element.Category?.Name ?? "Nicht sortiert"));
            component.SetAttributeValue("g.sys.lb", ReadParameter(element, "g.sys.lb", element.Category?.Name ?? "Nicht sortiert"));
            component.SetAttributeValue("g.ky", ReadParameter(element, "g.ky", element.Name));
            component.SetAttributeValue("g.lb", ReadParameter(element, "g.lb", element.Name));
            component.SetAttributeValue("typ", ReadParameter(element, "typ", element.Category?.Name ?? string.Empty));
            component.SetAttributeValue("fnc", ReadParameter(element, "fnc", element.Name));
            return component;
        }

        private void WriteRevitGroupAttributes(XElement component, Element element)
        {
            var groupId = element.GroupId;
            if (groupId == ElementId.InvalidElementId)
            {
                component.SetAttributeValue("revit.group.id", null);
                component.SetAttributeValue("revit.group.name", null);
                return;
            }

            if (Document.GetElement(groupId) is not Group group)
            {
                return;
            }

            component.SetAttributeValue("revit.group.id", group.Id.Value.ToString());
            component.SetAttributeValue("revit.group.name", group.GroupType?.Name ?? group.Name);
        }

        private static bool IsReadableModelElement(Element element)
        {
            if (element is Group)
            {
                return false;
            }

            if (element is DirectShape directShape && string.Equals(directShape.ApplicationId, ApplicationId, StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(ReadUid(directShape));
            }

            if (element.Category == null || element.Category.CategoryType != CategoryType.Model)
            {
                return false;
            }

            if (element.ViewSpecific)
            {
                return false;
            }

            return element is FamilyInstance
                or DirectShape
                or Group
                or Wall
                or Floor
                or Ceiling
                or RoofBase
                or MEPCurve
                or FabricationPart;
        }

        private static string ReadUid(Element element)
        {
            if (element is DirectShape directShape && !string.IsNullOrWhiteSpace(directShape.ApplicationDataId))
            {
                return directShape.ApplicationDataId;
            }

            var rrxUid = ReadParameter(element, "RRX-UID", string.Empty);
            if (!string.IsNullOrWhiteSpace(rrxUid))
            {
                return rrxUid;
            }

            var parameterUid = ReadParameter(element, "uid", string.Empty);
            if (!string.IsNullOrWhiteSpace(parameterUid))
            {
                return parameterUid;
            }

            var oldRrxBid = ReadParameter(element, "RRX-bid", string.Empty);
            if (!string.IsNullOrWhiteSpace(oldRrxBid))
            {
                return oldRrxBid;
            }

            var oldBid = ReadParameter(element, "bid", string.Empty);
            if (!string.IsNullOrWhiteSpace(oldBid))
            {
                return oldBid;
            }

            return ReadStoredUid(element);
        }

        private static string ReadParameter(Element element, string parameterName, string fallback)
        {
            var parameter = element.LookupParameter(parameterName);
            if (parameter?.StorageType == StorageType.String)
            {
                var value = parameter.AsString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return fallback;
        }

        private static string ReadAttribute(XElement element, string name, string fallback = "")
        {
            return element.Attribute(name)?.Value ?? fallback;
        }

        private static string ReadComponentUid(XElement component)
        {
            return ReadAttribute(component, "uid", ReadAttribute(component, "bid"));
        }

        private int CountRrxPlaceholders()
        {
            return new FilteredElementCollector(Document)
                .WhereElementIsNotElementType()
                .Count(element => IsRrxPlaceholder(element));
        }

        private static XYZ CreatePlaceholderLocation(int placeholderIndex)
        {
            var radius = ToInternalLength(PlaceholderDiameterMeters / 2.0);
            var spacing = ToInternalLength(PlaceholderSpacingMeters);
            return new XYZ(placeholderIndex * spacing, 0, radius);
        }

        private static bool IsRrxPlaceholder(Element element)
        {
            if (element is Group)
            {
                return false;
            }

            if (element is DirectShape directShape && string.Equals(directShape.ApplicationId, ApplicationId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (element is FamilyInstance familyInstance)
            {
                return !string.IsNullOrWhiteSpace(ReadUid(element));
            }

            return !string.IsNullOrWhiteSpace(ReadUid(element));
        }

        private static double ToInternalLength(double meters)
        {
#if REVIT2020_OR_OLDER
            return UnitUtils.ConvertToInternalUnits(meters, DisplayUnitType.DUT_METERS);
#else
            return UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
#endif
        }

        private static Schema GetStorageSchema()
        {
            var schema = Schema.Lookup(StorageSchemaId);
            if (schema != null)
            {
                return schema;
            }

            var builder = new SchemaBuilder(StorageSchemaId);
            builder.SetSchemaName("RrxProjectTreeComponent");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Bid", typeof(string));
            builder.AddSimpleField("ComponentXml", typeof(string));
            return builder.Finish();
        }

        private static void WriteStoredComponent(Element element, XElement component)
        {
            var schema = GetStorageSchema();
            var entity = new Entity(schema);
            entity.Set("Bid", ReadComponentUid(component));
            entity.Set("ComponentXml", component.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
            element.SetEntity(entity);
        }

        private static XElement? ReadStoredComponent(Element element)
        {
            var schema = GetStorageSchema();
            var entity = element.GetEntity(schema);
            if (!entity.IsValid())
            {
                return null;
            }

            var xml = entity.Get<string>("ComponentXml");
            return string.IsNullOrWhiteSpace(xml) ? null : XElement.Parse(xml);
        }

        private static string ReadStoredUid(Element element)
        {
            var schema = GetStorageSchema();
            var entity = element.GetEntity(schema);
            return entity.IsValid() ? entity.Get<string>("Bid") : string.Empty;
        }

        private static void SuppressTransactionWarnings(Transaction transaction)
        {
            var options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new RevitWarningSuppressor());
            transaction.SetFailureHandlingOptions(options);
        }

        private sealed class RevitWarningSuppressor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                var messages = failuresAccessor.GetFailureMessages();
                foreach (var message in messages)
                {
                    if (message.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(message);
                    }
                }

                return FailureProcessingResult.Continue;
            }
        }

        private sealed class RrxFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
#endif
