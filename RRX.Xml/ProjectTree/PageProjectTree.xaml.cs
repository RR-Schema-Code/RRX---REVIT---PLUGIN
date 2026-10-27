using Microsoft.Win32;
using RRX.DragDrop;
using RRX.DragDrop.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public partial class PageProjectTree : Page, IDropTarget
    {
        public const string BuildVersion = "RRX-Revit 2026-09-10.0060";
        private readonly ItemProjectTreeDefault rrxRoot = new();
        private readonly ItemProjectTreeDefault revitRoot = new();
        private readonly IRevitProjectTreeApi revitApi;
        private XDocument? rrxDocument;
        private string? rrxFilePath;
        private string? revitFilePath;

        public PageProjectTree()
            : this(new RevitBridgeProjectTreeApi())
        {
        }

        public PageProjectTree(IRevitProjectTreeApi revitApi)
        {
            this.revitApi = revitApi;
            InitializeComponent();
            DataContext = this;
            VersionText.Text = BuildVersion;

            LoadInitialRrxTree();
            LoadInitialRevitFile();
            BuildInitialRevitTree();
        }

        private void LoadInitialRevitFile()
        {
            revitFilePath = FindFileInParentDirectories("rstbasicsampleproject.rvt");
        }

        private void LoadInitialRrxTree()
        {
            var workspaceTemplate = FindFileInParentDirectories("46d3b839-c96a-42ea-820e-e069499cb947.tmp");
            if (!string.IsNullOrWhiteSpace(workspaceTemplate))
            {
                LoadRrxXml(workspaceTemplate);
                return;
            }

            RrxStatus.Text = "Keine RRX-XML geladen";
            RefreshTreeSources();
        }

        private static string? FindFileInParentDirectories(string fileName)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private void LoadRrxXml(string filePath)
        {
            rrxFilePath = filePath;
            rrxDocument = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);

            rrxRoot.Children.Clear();

            var rootItem = new RrxProjectTreeItem
            {
                Kind = RrxProjectTreeItemKind.Root,
                SourceSide = "RRX",
                Name = Path.GetFileName(filePath),
                Label = "RRX XML"
            };
            rrxRoot.Children.Add(rootItem);

            foreach (var aspGroup in rrxDocument.Descendants("cmp").GroupBy(ReadAspKey).OrderBy(g => g.Key))
            {
                var firstAspComponent = aspGroup.First();
                var aspItem = new RrxProjectTreeItem
                {
                    Kind = RrxProjectTreeItemKind.Asp,
                    SourceSide = "RRX",
                    Key = aspGroup.Key,
                    Label = AttributeValue(firstAspComponent, "g.asp.lb"),
                    Name = aspGroup.Key
                };
                rootItem.Children.Add(aspItem);

                foreach (var systemGroup in aspGroup.GroupBy(ReadSystemKey).OrderBy(g => g.Key))
                {
                    var firstSystemComponent = systemGroup.First();
                    var systemItem = new RrxProjectTreeItem
                    {
                        Kind = RrxProjectTreeItemKind.SystemFolder,
                        SourceSide = "RRX",
                        Key = systemGroup.Key,
                        Label = AttributeValue(firstSystemComponent, "g.sys.lb").Trim(),
                        Name = systemGroup.Key
                    };
                    aspItem.Children.Add(systemItem);

                    foreach (var component in systemGroup.OrderBy(ReadSequence).ThenBy(ReadComponentKey))
                    {
                        var componentItem = CreateComponentItem(component, "RRX");
                        systemItem.Children.Add(componentItem);
                    }
                }
            }

            RrxStatus.Text = $"{rrxDocument.Descendants("cmp").Count()} RRX-Komponenten geladen";
            MatchTrees();
            RefreshTreeSources();
        }

        private RrxProjectTreeItem CreateComponentItem(XElement component, string sourceSide)
        {
            var item = new RrxProjectTreeItem
            {
                Kind = sourceSide == "Revit" ? RrxProjectTreeItemKind.RevitElement : RrxProjectTreeItemKind.Component,
                SourceElement = component,
                SourceSide = sourceSide,
                Key = ReadComponentKey(component),
                Label = AttributeValue(component, "g.lb"),
                Name = AttributeValue(component, "fnc"),
                Uid = ReadComponentUid(component)
            };

            AddChildren(item, component, "dpt", RrxProjectTreeItemKind.DataPoint);
            AddChildren(item, component, "wir", RrxProjectTreeItemKind.Wire);
            AddChildren(item, component, "art", RrxProjectTreeItemKind.Article);
            AddChildren(item, component, "add", RrxProjectTreeItemKind.Additional);

            return item;
        }

        private static void AddChildren(RrxProjectTreeItem parent, XElement component, string elementName, RrxProjectTreeItemKind kind)
        {
            foreach (var child in component.Elements(elementName))
            {
                parent.Children.Add(new RrxProjectTreeItem
                {
                    Kind = kind,
                    SourceElement = child,
                    SourceSide = parent.SourceSide,
                    Key = AttributeValue(child, "g.ky", AttributeValue(child, "key", AttributeValue(child, "nam"))),
                    Label = AttributeValue(child, "g.lb", AttributeValue(child, "dsc", AttributeValue(child, "fnc"))),
                    Name = child.Name.LocalName
                });
            }
        }

        private void BuildInitialRevitTree()
        {
            revitRoot.Children.Clear();

            var rootItem = new RrxProjectTreeItem
            {
                Kind = RrxProjectTreeItemKind.Root,
                SourceSide = "Revit",
                Name = "Revit Modell",
                Label = "Elemente"
            };
            revitRoot.Children.Add(rootItem);

            AddRevitGroup(rootItem, "Nicht sortiert");
            RevitStatus.Text = string.IsNullOrWhiteSpace(revitFilePath)
                ? "Revit-API bereit"
                : $"Revit-Datei bereit: {Path.GetFileName(revitFilePath)}";
            MatchTrees();
            RefreshTreeSources();
        }

        private static RrxProjectTreeItem AddRevitGroup(RrxProjectTreeItem rootItem, string name)
        {
            var group = new RrxProjectTreeItem
            {
                Kind = RrxProjectTreeItemKind.RevitGroup,
                SourceSide = "Revit",
                Name = name,
                Label = "Anlage"
            };
            rootItem.Children.Add(group);
            return group;
        }

        private async Task ReadRevitModelFromApiAsync(string? filePath = null)
        {
            BuildInitialRevitTree();
            if (revitRoot.Children.FirstOrDefault() is not RrxProjectTreeItem rootItem)
            {
                return;
            }

            rootItem.Children.Clear();
            var revitComponents = await ReadRevitComponentsAsync(filePath);
            foreach (var systemGroup in revitComponents.GroupBy(ReadRevitGroupKey).OrderBy(g => g.Key))
            {
                var group = AddRevitGroup(rootItem, systemGroup.Key);
                foreach (var component in systemGroup.OrderBy(ReadSequence))
                {
                    var revitElement = CreateComponentItem(new XElement(component), "Revit");
                    group.Children.Add(revitElement);
                }
            }

            if (rootItem.Children.Count == 0)
            {
                AddRevitGroup(rootItem, "Nicht sortiert");
            }

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                revitFilePath = filePath;
            }

            var sourceName = string.IsNullOrWhiteSpace(filePath) ? "aktives Modell" : Path.GetFileName(filePath);
            RevitStatus.Text = $"{revitComponents.Count} Revit-Eintrag/Einträge gelesen: {sourceName}";
            SyncRrxUidsFromXml();
            MatchTrees();
            RefreshTreeSources();
        }

        private void SyncRrxUidsFromXml()
        {
            foreach (var item in Flatten(rrxRoot.Children).OfType<RrxProjectTreeItem>())
            {
                if (item.Kind == RrxProjectTreeItemKind.Component && item.SourceElement != null)
                {
                    item.Uid = ReadComponentUid(item.SourceElement);
                }
            }
        }

        private void MatchTrees()
        {
            var rrxItems = Flatten(rrxRoot.Children).OfType<RrxProjectTreeItem>().ToList();
            var revitItems = Flatten(revitRoot.Children).OfType<RrxProjectTreeItem>().ToList();
            var rrxUids = rrxItems.Where(IsMatchCandidate).Select(i => i.Uid).Where(NotEmpty).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var revitUids = revitItems.Where(IsMatchCandidate).Select(i => i.Uid).Where(NotEmpty).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var item in rrxItems)
            {
                item.IsMatched = IsMatchCandidate(item) && NotEmpty(item.Uid) && revitUids.Contains(item.Uid);
            }

            foreach (var item in revitItems)
            {
                item.IsMatched = IsMatchCandidate(item) && NotEmpty(item.Uid) && rrxUids.Contains(item.Uid);
            }

            UpdateAggregateStatus(rrxRoot.Children);
            UpdateAggregateStatus(revitRoot.Children);
        }

        private static void UpdateAggregateStatus(IEnumerable<IItemProjectTree> items)
        {
            foreach (var item in items.OfType<RrxProjectTreeItem>())
            {
                UpdateAggregateStatus(item.Children);

                if (IsAggregateMatchCandidate(item))
                {
                    var childComponents = Flatten(item.Children)
                        .OfType<RrxProjectTreeItem>()
                        .Where(IsMatchCandidate)
                        .ToList();

                    item.IsMatched = childComponents.Count > 0 && childComponents.All(child => child.IsMatched);
                }
            }
        }

        private void RefreshTreeSources()
        {
            if (Templates.ItemsSource == null)
            {
                Templates.ItemsSource = rrxRoot.Children;
            }

            if (Xml.ItemsSource == null)
            {
                Xml.ItemsSource = revitRoot.Children;
            }
        }

        private Task<IReadOnlyList<XElement>> ReadRevitComponentsAsync(string? filePath = null)
        {
            return revitApi is IAsyncRevitProjectTreeApi asyncApi
                ? asyncApi.ReadComponentsAsync(filePath)
                : Task.FromResult(revitApi.ReadComponents(filePath));
        }

        private Task<IReadOnlyList<XElement>> CreateOrUpdateRevitComponentsAsync(IEnumerable<XElement> components)
        {
            return revitApi is IAsyncRevitProjectTreeApi asyncApi
                ? asyncApi.CreateOrUpdateComponentsAsync(components)
                : Task.FromResult(revitApi.CreateOrUpdateComponents(components));
        }

        private Task<int> DeleteRevitComponentsAsync(IEnumerable<string> uids)
        {
            return revitApi is IAsyncRevitProjectTreeApi asyncApi
                ? asyncApi.DeleteComponentsAsync(uids)
                : Task.FromResult(revitApi.DeleteComponents(uids));
        }

        private Task<int> UngroupAutomationGroupAsync()
        {
            return revitApi is IAsyncRevitProjectTreeApi asyncApi
                ? asyncApi.UngroupAutomationGroupAsync()
                : Task.FromResult(revitApi.UngroupAutomationGroup());
        }

        private Task<int> CreateAutomationGroupAsync()
        {
            return revitApi is IAsyncRevitProjectTreeApi asyncApi
                ? asyncApi.CreateAutomationGroupAsync()
                : Task.FromResult(revitApi.CreateAutomationGroup());
        }

        private static IEnumerable<IItemProjectTree> Flatten(IEnumerable<IItemProjectTree> items)
        {
            foreach (var item in items)
            {
                yield return item;

                foreach (var child in Flatten(item.Children))
                {
                    yield return child;
                }
            }
        }

        private static bool IsMatchCandidate(RrxProjectTreeItem item)
        {
            return item.Kind is RrxProjectTreeItemKind.Component or RrxProjectTreeItemKind.RevitElement;
        }

        private static bool IsAggregateMatchCandidate(RrxProjectTreeItem item)
        {
            return item.Kind is RrxProjectTreeItemKind.Root
                or RrxProjectTreeItemKind.Asp
                or RrxProjectTreeItemKind.SystemFolder
                or RrxProjectTreeItemKind.RevitGroup;
        }

        private static bool NotEmpty(string value)
        {
            return !string.IsNullOrWhiteSpace(value);
        }

        private static string ReadAspKey(XElement component)
        {
            return AttributeValue(component, "g.asp.ky", "Ohne ASP");
        }

        private static string ReadSystemKey(XElement component)
        {
            return AttributeValue(component, "g.sys.ky", "Ohne System");
        }

        private static string ReadRevitGroupKey(XElement component)
        {
            return AttributeValue(component, "revit.group.name", ReadSystemKey(component));
        }

        private static string ReadComponentKey(XElement component)
        {
            return AttributeValue(component, "g.ky", AttributeValue(component, "key", component.Name.LocalName));
        }

        private static int ReadSequence(XElement component)
        {
            return int.TryParse(AttributeValue(component, "seq"), out var sequence) ? sequence : int.MaxValue;
        }

        private static string AttributeValue(XElement element, string name, string fallback = "")
        {
            return element.Attribute(name)?.Value ?? fallback;
        }

        private static string ReadComponentUid(XElement component)
        {
            return AttributeValue(component, "uid", AttributeValue(component, "bid"));
        }

        private void Tree_Loaded(object sender, RoutedEventArgs e)
        {
            var presenter = (sender as TreeView)?.GetVisualDescendent<ScrollContentPresenter>();
            if (presenter != null)
            {
                presenter.RequestBringIntoView -= Tree_RequestBringIntoView;
                presenter.RequestBringIntoView += Tree_RequestBringIntoView;
            }
        }

        private void Tree_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            e.Handled = true;
        }

        private void TreeItem_Collapsed(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, e.OriginalSource) && sender is TreeViewItem treeViewItem)
            {
                CollapseChildItems(treeViewItem);
            }
        }

        private static void CollapseChildItems(ItemsControl parent)
        {
            parent.ApplyTemplate();
            var itemsPresenter = parent.GetVisualDescendent<ItemsPresenter>();
            itemsPresenter?.ApplyTemplate();

            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem child)
                {
                    child.IsExpanded = false;
                    CollapseChildItems(child);
                }
            }
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is not RrxProjectTreeItem sourceItem || dropInfo.TargetItem is not RrxProjectTreeItem targetItem)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            if (IsRevitDeleteDrop(sourceItem, targetItem))
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                return;
            }

            if (!CanCreateRevitElements(sourceItem))
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }

            if (targetItem.Kind is RrxProjectTreeItemKind.Root or RrxProjectTreeItemKind.RevitGroup or RrxProjectTreeItemKind.RevitElement)
            {
                dropInfo.Effects = DragDropEffects.Copy;
                dropInfo.DropTargetAdorner = targetItem.Kind == RrxProjectTreeItemKind.RevitElement
                    ? DropTargetAdorners.Insert
                    : DropTargetAdorners.Highlight;
                return;
            }

            dropInfo.Effects = DragDropEffects.None;
        }

        async void IDropTarget.Drop(IDropInfo dropInfo)
        {
            try
            {
                if (dropInfo.Data is not RrxProjectTreeItem sourceItem || dropInfo.TargetItem is not RrxProjectTreeItem targetItem)
                {
                    return;
                }

                if (IsRevitDeleteDrop(sourceItem, targetItem))
                {
                    await DeleteRevitItemsAsync(sourceItem);
                    return;
                }

                var components = CollectComponents(sourceItem).ToList();
                if (components.Count == 0)
                {
                    return;
                }

                var added = 0;
                if (targetItem.Kind == RrxProjectTreeItemKind.Root && components.Select(ReadSystemKeyFromItem).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                {
                    foreach (var systemGroup in components.GroupBy(ReadSystemKeyFromItem).OrderBy(g => g.Key))
                    {
                        var revitGroup = EnsureRevitGroup(targetItem, systemGroup.Key);
                        foreach (var component in systemGroup)
                        {
                            if (await AddRevitElementAsync(component, revitGroup.Children))
                            {
                                added++;
                            }
                        }
                    }
                }
                else
                {
                    var targetCollection = GetRevitTargetCollection(targetItem);
                    foreach (var component in components)
                    {
                        if (await AddRevitElementAsync(component, targetCollection))
                        {
                            added++;
                        }
                    }
                }

                RevitStatus.Text = $"{added} Element(e) ins Revit-Modell übernommen";
                MatchTrees();
            }
            catch (Exception exception)
            {
                RevitStatus.Text = $"Revit-Übernahme fehlgeschlagen: {exception.Message}";
            }
        }

        private static bool CanCreateRevitElements(RrxProjectTreeItem item)
        {
            return item.SourceSide == "RRX" && (item.Kind is RrxProjectTreeItemKind.Root
                or RrxProjectTreeItemKind.Asp
                or RrxProjectTreeItemKind.SystemFolder
                or RrxProjectTreeItemKind.Component);
        }

        private static bool IsRevitDeleteDrop(RrxProjectTreeItem sourceItem, RrxProjectTreeItem targetItem)
        {
            return targetItem.SourceSide == "RRX" && CanDeleteRevitElements(sourceItem);
        }

        private static bool CanDeleteRevitElements(RrxProjectTreeItem item)
        {
            return item.SourceSide == "Revit"
                && (item.Kind is RrxProjectTreeItemKind.RevitElement or RrxProjectTreeItemKind.RevitGroup);
        }

        private static IEnumerable<RrxProjectTreeItem> CollectComponents(RrxProjectTreeItem item)
        {
            if (item.Kind == RrxProjectTreeItemKind.Component)
            {
                yield return item;
            }

            foreach (var child in item.Children.OfType<RrxProjectTreeItem>())
            {
                foreach (var component in CollectComponents(child))
                {
                    yield return component;
                }
            }
        }

        private static IEnumerable<RrxProjectTreeItem> CollectRevitElements(RrxProjectTreeItem item)
        {
            if (item.Kind == RrxProjectTreeItemKind.RevitElement)
            {
                yield return item;
            }

            foreach (var child in item.Children.OfType<RrxProjectTreeItem>())
            {
                foreach (var revitElement in CollectRevitElements(child))
                {
                    yield return revitElement;
                }
            }
        }

        private async Task DeleteRevitItemsAsync(RrxProjectTreeItem sourceItem)
        {
            var uids = CollectRevitElements(sourceItem)
                .Select(item => item.Uid)
                .Where(NotEmpty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uids.Count == 0)
            {
                RevitStatus.Text = "Kein Revit-Element mit uid zum Entfernen ausgewählt";
                return;
            }

            var expandedItems = CaptureExpandedTreeItems(revitRoot.Children);
            var deleted = await DeleteRevitComponentsAsync(uids);
            RebuildRevitTree(await ReadRevitComponentsAsync(), expandedItems);
            MatchTrees();
            RefreshTreeSources();

            RevitStatus.Text = $"{deleted} Revit-Element(e) entfernt";
        }

        private static HashSet<string> CaptureExpandedTreeItems(IEnumerable<IItemProjectTree> items)
        {
            var expandedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CaptureExpandedTreeItems(items, string.Empty, expandedItems);
            return expandedItems;
        }

        private static void CaptureExpandedTreeItems(IEnumerable<IItemProjectTree> items, string parentPath, ISet<string> expandedItems)
        {
            foreach (var item in items.OfType<RrxProjectTreeItem>())
            {
                var path = CreateTreePath(parentPath, item);
                if (item.IsExpanded)
                {
                    expandedItems.Add(path);
                }

                CaptureExpandedTreeItems(item.Children, path, expandedItems);
            }
        }

        private static string ReadSystemKeyFromItem(RrxProjectTreeItem item)
        {
            return item.SourceElement == null ? "Nicht sortiert" : ReadSystemKey(item.SourceElement);
        }

        private static RrxProjectTreeItem EnsureRevitGroup(RrxProjectTreeItem rootItem, string name)
        {
            var existing = rootItem.Children.OfType<RrxProjectTreeItem>()
                .FirstOrDefault(i => i.Kind == RrxProjectTreeItemKind.RevitGroup && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

            return existing ?? AddRevitGroup(rootItem, name);
        }

        private async Task<bool> AddRevitElementAsync(RrxProjectTreeItem sourceItem, ObservableCollection<IItemProjectTree> targetCollection)
        {
            if (string.IsNullOrWhiteSpace(sourceItem.Uid))
            {
                RevitStatus.Text = "RRX-Komponente ohne uid wird nicht übernommen";
                return false;
            }

            if (Flatten(revitRoot.Children).OfType<RrxProjectTreeItem>().Any(i => i.Kind == RrxProjectTreeItemKind.RevitElement && string.Equals(i.Uid, sourceItem.Uid, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var transferComponent = CreateTransferComponent(sourceItem.Xml());
            var writtenComponents = await CreateOrUpdateRevitComponentsAsync(new[] { transferComponent });
            var writtenComponent = writtenComponents.FirstOrDefault() ?? new XElement(sourceItem.Xml());

            var revitElement = CreateComponentItem(writtenComponent, "Revit");
            revitElement.Kind = RrxProjectTreeItemKind.RevitElement;
            revitElement.SourceSide = "Revit";
            revitElement.Uid = sourceItem.Uid;
            targetCollection.Add(revitElement);
            return true;
        }

        private async Task SyncRrxToRevitAsync()
        {
            var existingRevitComponents = await ReadRevitComponentsAsync();
            var existingRevitUids = existingRevitComponents
                .Select(ReadComponentUid)
                .Where(NotEmpty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rrxComponents = Flatten(rrxRoot.Children)
                .OfType<RrxProjectTreeItem>()
                .Where(item => item.Kind == RrxProjectTreeItemKind.Component)
                .Where(component => !string.IsNullOrWhiteSpace(component.Uid))
                .Where(component => existingRevitUids.Contains(component.Uid))
                .ToList();

            var writtenComponents = await CreateOrUpdateRevitComponentsAsync(rrxComponents.Select(component => CreateTransferComponent(component.Xml())));
            RebuildRevitTree(await ReadRevitComponentsAsync());
            SyncRrxUidsFromXml();
            MatchTrees();

            RevitStatus.Text = $"{writtenComponents.Count} vorhandene Revit-Element(e) aktualisiert";
        }

        private XElement CreateTransferComponent(XElement component)
        {
            var transferComponent = new XElement(component);
            RemoveTransferChildrenWhenUnchecked(transferComponent, "dpt", TransferDatapoints.IsChecked == true);
            RemoveTransferChildrenWhenUnchecked(transferComponent, "wir", TransferWires.IsChecked == true);
            RemoveTransferChildrenWhenUnchecked(transferComponent, "art", TransferArticles.IsChecked == true);
            RemoveTransferChildrenWhenUnchecked(transferComponent, "add", TransferAdditionals.IsChecked == true);
            return transferComponent;
        }

        private static void RemoveTransferChildrenWhenUnchecked(XElement component, string elementName, bool shouldTransfer)
        {
            if (shouldTransfer)
            {
                return;
            }

            component.Elements(elementName).Remove();
        }

        private void RebuildRevitTree(IReadOnlyList<XElement> revitComponents, IReadOnlySet<string>? expandedItems = null)
        {
            BuildInitialRevitTree();
            if (revitRoot.Children.FirstOrDefault() is not RrxProjectTreeItem rootItem)
            {
                return;
            }

            rootItem.Children.Clear();
            foreach (var systemGroup in revitComponents.GroupBy(ReadRevitGroupKey).OrderBy(g => g.Key))
            {
                var group = AddRevitGroup(rootItem, systemGroup.Key);
                foreach (var component in systemGroup.OrderBy(ReadSequence).ThenBy(ReadComponentKey))
                {
                    group.Children.Add(CreateComponentItem(new XElement(component), "Revit"));
                }
            }

            if (rootItem.Children.Count == 0)
            {
                AddRevitGroup(rootItem, "Nicht sortiert");
            }

            RestoreExpandedTreeItems(revitRoot.Children, expandedItems);
        }

        private static void RestoreExpandedTreeItems(IEnumerable<IItemProjectTree> items, IReadOnlySet<string>? expandedItems)
        {
            if (expandedItems == null || expandedItems.Count == 0)
            {
                return;
            }

            RestoreExpandedTreeItems(items, string.Empty, expandedItems);
        }

        private static void RestoreExpandedTreeItems(IEnumerable<IItemProjectTree> items, string parentPath, IReadOnlySet<string> expandedItems)
        {
            foreach (var item in items.OfType<RrxProjectTreeItem>())
            {
                var path = CreateTreePath(parentPath, item);
                item.IsExpanded = expandedItems.Contains(path);
                RestoreExpandedTreeItems(item.Children, path, expandedItems);
            }
        }

        private static string CreateTreePath(string parentPath, RrxProjectTreeItem item)
        {
            var segment = $"{item.SourceSide}:{item.Kind}:{item.Uid}:{item.Key}:{item.Name}";
            return string.IsNullOrEmpty(parentPath) ? segment : $"{parentPath}/{segment}";
        }

        private ObservableCollection<IItemProjectTree> GetRevitTargetCollection(RrxProjectTreeItem targetItem)
        {
            if (targetItem.Kind is RrxProjectTreeItemKind.Root or RrxProjectTreeItemKind.RevitGroup)
            {
                return targetItem.Children;
            }

            var parent = FindParent(revitRoot.Children, targetItem);
            return parent?.Children ?? revitRoot.Children;
        }

        private static RrxProjectTreeItem? FindParent(IEnumerable<IItemProjectTree> items, RrxProjectTreeItem target)
        {
            foreach (var item in items.OfType<RrxProjectTreeItem>())
            {
                if (item.Children.Contains(target))
                {
                    return item;
                }

                var nestedParent = FindParent(item.Children, target);
                if (nestedParent != null)
                {
                    return nestedParent;
                }
            }

            return null;
        }

        private void LoadRrx_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "RRX XML (*.xml;*.rrx;*.tmp)|*.xml;*.rrx;*.tmp|Alle Dateien (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                LoadRrxXml(dialog.FileName);
            }
        }

        private async void ReadRevit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (revitApi is RevitBridgeProjectTreeApi)
                {
                    await ReadRevitModelFromApiAsync();
                    return;
                }

                if (revitApi is IAsyncRevitProjectTreeApi)
                {
                    await ReadRevitModelFromApiAsync();
                    return;
                }
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Revit-Modell oder Testdaten lesen",
                Filter = "Revit Modell (*.rvt)|*.rvt|RRX/XML Testdaten (*.xml;*.rrx;*.tmp)|*.xml;*.rrx;*.tmp|Alle Dateien (*.*)|*.*"
            };

            if (!string.IsNullOrWhiteSpace(revitFilePath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(revitFilePath);
                dialog.FileName = Path.GetFileName(revitFilePath);
            }

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                await ReadRevitModelFromApiAsync(dialog.FileName);
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
            }
        }

        private async void SyncRrxToRevit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SyncRrxToRevitAsync();
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
            }
        }

        private async void UngroupAutomation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ungrouped = await UngroupAutomationGroupAsync();
                await ReadRevitModelFromApiAsync();
                RevitStatus.Text = $"{ungrouped} Gebäudeautomation-Gruppe(n) gelöst";
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
            }
        }

        private async void CreateAutomationGroup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var grouped = await CreateAutomationGroupAsync();
                await ReadRevitModelFromApiAsync();
                RevitStatus.Text = grouped == 0
                    ? "Keine RRX-Elemente für Gebäudeautomation-Gruppe gefunden"
                    : $"{grouped} RRX-Element(e) in Gebäudeautomation-Gruppe gepackt";
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
            }
        }

        private async void RemoveRevit_Click(object sender, RoutedEventArgs e)
        {
            if (Xml.SelectedItem is not RrxProjectTreeItem selectedItem || !CanDeleteRevitElements(selectedItem))
            {
                RevitStatus.Text = "Bitte Revit-Element oder Anlage zum Entfernen auswählen";
                return;
            }

            try
            {
                await DeleteRevitItemsAsync(selectedItem);
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
            }
        }

        private async void RevitTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
            {
                return;
            }

            if (Xml.SelectedItem is not RrxProjectTreeItem selectedItem || !CanDeleteRevitElements(selectedItem))
            {
                return;
            }

            e.Handled = true;
            try
            {
                await DeleteRevitItemsAsync(selectedItem);
            }
            catch (Exception exception)
            {
                RevitStatus.Text = exception.Message;
            }
        }
    }
}
