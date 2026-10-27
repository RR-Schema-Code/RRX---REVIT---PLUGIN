using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public class ItemProjectTreeDefault : INotifyPropertyChanged, IItemProjectTree
    {
        private string name = string.Empty;
        private string content = string.Empty;
        private ObservableCollection<IItemProjectTree> children = new();

        public string Name
        {
            get => name;
            set
            {
                name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Content
        {
            get => content;
            set
            {
                content = value;
                OnPropertyChanged(nameof(Content));
            }
        }

        public ObservableCollection<IItemProjectTree> Children
        {
            get => children;
            set
            {
                children = value;
                OnPropertyChanged(nameof(Children));
            }
        }

        public IItemProjectTree Copy()
        {
            var copy = new ItemProjectTreeDefault
            {
                Name = Name,
                Content = Content
            };

            foreach (var child in Children)
            {
                copy.Children.Add(child.Copy());
            }

            return copy;
        }

        public XElement Xml()
        {
            return new XElement("default");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
