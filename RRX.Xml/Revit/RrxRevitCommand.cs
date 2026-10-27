#if REVIT_API_AVAILABLE
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RRX.Xml.ProjectTree;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace RRX.Xml.Revit
{
    [Transaction(TransactionMode.Manual)]
    public class RrxRevitCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var revitApi = new RevitExternalEventProjectTreeApi(commandData.Application);
                var page = new PageProjectTree(revitApi);
                var window = new Window
                {
                    Title = "RRX - Revit Abgleich",
                    Width = 1200,
                    Height = 760,
                    MinWidth = 900,
                    MinHeight = 560,
                    Content = new System.Windows.Controls.Frame
                    {
                        Content = page,
                        NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
                    }
                };
                window.Closed += (_, _) => revitApi.Dispose();

                new WindowInteropHelper(window)
                {
                    Owner = commandData.Application.MainWindowHandle
                };

                window.Show();
                return Result.Succeeded;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return Result.Failed;
            }
        }
    }
}
#endif
