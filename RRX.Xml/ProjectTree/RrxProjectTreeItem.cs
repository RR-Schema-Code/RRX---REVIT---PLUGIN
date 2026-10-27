using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public enum RrxProjectTreeItemKind
    {
        Root,
        Asp,
        SystemFolder,
        Component,
        DataPoint,
        Wire,
        Article,
        Additional,
        RevitGroup,
        RevitElement
    }

    public class RrxProjectTreeItem : INotifyPropertyChanged, IItemProjectTree
    {
        private string name = string.Empty;
        private string key = string.Empty;
        private string label = string.Empty;
        private string uid = string.Empty;
        private bool isMatched;
        private bool isExpanded;
        private RrxProjectTreeItemKind kind;

        public RrxProjectTreeItemKind Kind
        {
            get => kind;
            set
            {
                kind = value;
                OnPropertyChanged(nameof(Kind));
                OnPropertyChanged(nameof(ShowMatchStatus));
                OnPropertyChanged(nameof(IconBrush));
                OnPropertyChanged(nameof(IconData));
            }
        }

        public XElement? SourceElement { get; set; }
        public string SourceSide { get; set; } = "RRX";

        public string Name
        {
            get => name;
            set
            {
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Key
        {
            get => key;
            set
            {
                key = value;
                OnPropertyChanged(nameof(Key));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string Label
        {
            get => label;
            set
            {
                label = value;
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string Uid
        {
            get => uid;
            set
            {
                uid = value;
                SourceElement?.SetAttributeValue("uid", uid);
                OnPropertyChanged(nameof(Uid));
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public bool IsMatched
        {
            get => isMatched;
            set
            {
                isMatched = value;
                OnPropertyChanged(nameof(IsMatched));
                OnPropertyChanged(nameof(MatchBrush));
                OnPropertyChanged(nameof(MatchText));
            }
        }

        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
            }
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Key) && !string.IsNullOrWhiteSpace(Label))
                {
                    return $"{Key} - {Label}";
                }

                if (!string.IsNullOrWhiteSpace(Key))
                {
                    return Key;
                }

                return Name;
            }
        }

        public string MatchText => IsMatched ? "verbunden" : "offen";
        public Brush MatchBrush => IsMatched ? Brushes.ForestGreen : Brushes.Firebrick;
        public bool ShowMatchStatus => Kind is RrxProjectTreeItemKind.Root
            or RrxProjectTreeItemKind.Asp
            or RrxProjectTreeItemKind.SystemFolder
            or RrxProjectTreeItemKind.Component
            or RrxProjectTreeItemKind.RevitGroup
            or RrxProjectTreeItemKind.RevitElement;

        public Brush IconBrush => Kind switch
        {
            RrxProjectTreeItemKind.Component or RrxProjectTreeItemKind.RevitElement => Brushes.DimGray,
            RrxProjectTreeItemKind.DataPoint => Brushes.SteelBlue,
            RrxProjectTreeItemKind.Wire => Brushes.DarkGoldenrod,
            RrxProjectTreeItemKind.Article => Brushes.SaddleBrown,
            RrxProjectTreeItemKind.Additional => Brushes.Sienna,
            _ => Brushes.Gray
        };

        public string IconData => Kind switch
        {
            RrxProjectTreeItemKind.Wire => "M 30.513672 3.9902344 A 1.50015 1.50015 0 0 0 29.771484 6.8105469 L 33.712891 9 L 27.5 9 A 1.50015 1.50015 0 0 0 26 10.5 L 26 12 L 12 12 C 7.599415 12 4 15.599415 4 20 C 4 24.400585 7.599415 28 12 28 L 35 28 C 37.779415 28 40 30.220585 40 33 C 40 35.779415 37.779415 38 35 38 L 6.5 38 A 1.50015 1.50015 0 1 0 6.5 41 L 35 41 C 39.400585 41 43 37.400585 43 33 C 43 28.599415 39.400585 25 35 25 L 12 25 C 9.220585 25 7 22.779415 7 20 C 7 17.220585 9.220585 15 12 15 L 26 15 L 26 16.5 A 1.50015 1.50015 0 0 0 27.5 18 L 41.5 18 A 1.50015 1.50015 0 0 0 43 16.5 L 43 10.5 A 1.50015 1.50015 0 0 0 41.5 9 L 39.886719 9 L 31.228516 4.1894531 A 1.50015 1.50015 0 0 0 30.513672 3.9902344 z",
            RrxProjectTreeItemKind.Article or RrxProjectTreeItemKind.Additional => "M 24 4.0097656 C 22.681544 4.0097656 21.362483 4.5086104 20.365234 5.5058594 L 7.8710938 18 L 4.4804688 18 C 3.7304688 18 3.0277813 18.332109 2.5507812 18.912109 C 2.0737813 19.491109 1.8823437 20.245469 2.0273438 20.980469 L 5.5058594 38.748047 C 6.1008594 41.791047 8.7827656 44 11.884766 44 L 36.097656 44 C 39.199656 44 41.881563 41.791047 42.476562 38.748047 L 45.953125 20.980469 C 45.953125 20.980469 45.955078 20.978516 45.955078 20.978516 C 46.097078 20.242516 45.905688 19.488156 45.429688 18.910156 C 44.952687 18.332156 44.249 18 43.5 18 L 40.128906 18 L 27.632812 5.5058594 C 26.635564 4.5086104 25.318456 4.0097656 24 4.0097656 z M 24 6.9921875 C 24.544472 6.9921875 25.087968 7.2032021 25.511719 7.6269531 L 35.886719 18 L 12.113281 18 L 22.486328 7.6269531 C 22.910079 7.2032021 23.455528 6.9921875 24 6.9921875 z M 20.5 25 C 21.328 25 22 25.671 22 26.5 L 22 35.5 C 22 36.329 21.328 37 20.5 37 C 19.672 37 19 36.329 19 35.5 L 19 26.5 C 19 25.671 19.672 25 20.5 25 z M 27.5 25 C 28.328 25 29 25.671 29 26.5 L 29 35.5 C 29 36.329 28.328 37 27.5 37 C 26.672 37 26 36.329 26 35.5 L 26 26.5 C 26 25.671 26.672 25 27.5 25 z M 12.636719 25.005859 C 13.330689 25.067561 13.908859 25.614938 13.990234 26.335938 L 14.990234 35.335938 C 15.081234 36.158938 14.489016 36.898234 13.666016 36.990234 C 13.609016 36.996234 13.554047 37 13.498047 37 C 12.744047 37 12.095766 36.433016 12.009766 35.666016 L 11.009766 26.666016 C 10.918766 25.843016 11.510984 25.100766 12.333984 25.009766 C 12.436734 24.997891 12.53758 24.997045 12.636719 25.005859 z M 35.363281 25.005859 C 35.462352 24.996977 35.563391 24.998016 35.666016 25.009766 C 36.489016 25.100766 37.081234 25.842016 36.990234 26.666016 L 35.990234 35.666016 C 35.904234 36.433016 35.255953 37 34.501953 37 C 34.445953 37 34.390984 36.996234 34.333984 36.990234 C 33.510984 36.899234 32.918766 36.158938 33.009766 35.335938 L 34.009766 26.335938 C 34.091141 25.614937 34.669789 25.068039 35.363281 25.005859 z",
            RrxProjectTreeItemKind.DataPoint => "M 36.5 5 A 6.5 6.5 0 0 0 31.349609 15.457031 L 25.773438 22.5 L 17.824219 22.5 A 6.5 6.5 0 0 0 11.5 17.5 A 6.5 6.5 0 0 0 11.5 30.5 A 6.5 6.5 0 0 0 17.816406 25.5 L 25.773438 25.5 L 31.349609 32.542969 A 6.5 6.5 0 0 0 36.5 43 A 6.5 6.5 0 0 0 36.5 30 A 6.5 6.5 0 0 0 33.677734 30.652344 L 28.414062 24 L 33.675781 17.351562 A 6.5 6.5 0 0 0 36.5 18 A 6.5 6.5 0 0 0 36.5 5 z",
            RrxProjectTreeItemKind.Component or RrxProjectTreeItemKind.RevitElement => "M 6 3 A 1.0001 1.0001 0 1 0 6 5 L 6 8 L 10 8 L 10 5 A 1.0001 1.0001 0 1 0 10 3 L 6 3 z M 16.828125 7 C 16.298125 7 15.789062 7.2109375 15.414062 7.5859375 L 13 10 L 13 22 L 15.414062 24.414062 C 15.789062 24.789062 16.298125 25 16.828125 25 L 28 25 C 29.105 25 30 24.105 30 23 L 30 9 C 30 7.895 29.105 7 28 7 L 16.828125 7 z M 19 9 L 27 9 C 27.553 9 28 9.447 28 10 L 28 22 C 28 22.553 27.553 23 27 23 L 19 23 C 18.447 23 18 22.553 18 22 C 18 21.447 18.447 21 19 21 L 26 21 L 26 19 L 19 19 C 18.447 19 18 18.553 18 18 C 18 17.447 18.447 17 19 17 L 26 17 L 26 15 L 19 15 C 18.447 15 18 14.553 18 14 C 18 13.447 18.447 13 19 13 L 26 13 L 26 11 L 19 11 C 18.447 11 18 10.553 18 10 C 18 9.447 18.447 9 19 9 z M 6 10 C 4.895 10 4 10.895 4 12 L 4 13 L 2 13 A 1.0001 1.0001 0 0 0 0.984375 11.986328 A 1.0001 1.0001 0 0 0 0 13 L 0 19 A 1.0001 1.0001 0 1 0 2 19 L 4 19 L 4 20 C 4 21.105 4.895 22 6 22 L 11 22 L 11 10 L 6 10 z",
            _ => "M 8.5 8 C 6.019 8 4 10.019 4 12.5 L 4 18 L 16.052734 18 C 16.636734 18 17.202344 17.793922 17.652344 17.419922 L 23.5 12.546875 L 19.572266 9.2734375 C 18.586266 8.4524375 17.336734 8 16.052734 8 L 8.5 8 z M 27.644531 13 L 19.572266 19.724609 C 18.585266 20.546609 17.336734 21 16.052734 21 L 4 21 L 4 35.5 C 4 37.981 6.019 40 8.5 40 L 39.5 40 C 41.981 40 44 37.981 44 35.5 L 44 17.5 C 44 15.019 41.981 13 39.5 13 L 27.644531 13 z"
        };

        public ObservableCollection<IItemProjectTree> Children { get; set; } = new ObservableCollection<IItemProjectTree>();

        public IItemProjectTree Copy()
        {
            var copy = new RrxProjectTreeItem
            {
                Name = Name,
                Key = Key,
                Label = Label,
                Uid = Uid,
                Kind = Kind,
                SourceElement = SourceElement == null ? null : new XElement(SourceElement),
                SourceSide = SourceSide,
                IsMatched = IsMatched,
                IsExpanded = IsExpanded
            };

            foreach (var child in Children)
            {
                copy.Children.Add(child.Copy());
            }

            return copy;
        }

        public XElement Xml()
        {
            if (SourceElement != null)
            {
                return new XElement(SourceElement);
            }

            var element = new XElement(Kind.ToString());
            element.SetAttributeValue("name", Name);
            element.SetAttributeValue("key", Key);
            element.SetAttributeValue("label", Label);
            element.SetAttributeValue("uid", Uid);
            return element;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
