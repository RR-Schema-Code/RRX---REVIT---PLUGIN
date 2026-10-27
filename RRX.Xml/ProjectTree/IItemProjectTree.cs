using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Security.Policy;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace RRX.Xml.ProjectTree
{
    public interface IItemProjectTree
    {
        // ---------- Properties ----------
        string Name { get; set; }


        // ---------- Basic ----------
        ObservableCollection<IItemProjectTree> Children { get; set; }


        // ---------- Functions ----------
        IItemProjectTree Copy();

        XElement Xml();
    }
}
