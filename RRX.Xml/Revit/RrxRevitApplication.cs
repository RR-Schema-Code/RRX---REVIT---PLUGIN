#if REVIT_API_AVAILABLE
using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;

namespace RRX.Xml.Revit
{
    public class RrxRevitApplication : IExternalApplication
    {
        private const string RibbonTabName = "RRX";
        private const string RibbonPanelName = "Revit";
        private const string CommandButtonName = "RRX x Revit";

        public Result OnStartup(UIControlledApplication application)
        {
            RevitBridgeServer.Start();
            CreateRibbonButton(application);
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void CreateRibbonButton(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(RibbonTabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
            }

            var panel = application.GetRibbonPanels(RibbonTabName)
                .Find(item => string.Equals(item.Name, RibbonPanelName, StringComparison.OrdinalIgnoreCase))
                ?? application.CreateRibbonPanel(RibbonTabName, RibbonPanelName);

            if (panel.GetItems().Any(item => string.Equals(item.Name, CommandButtonName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var buttonData = new PushButtonData(
                CommandButtonName,
                CommandButtonName,
                typeof(RrxRevitApplication).Assembly.Location,
                typeof(RrxRevitCommand).FullName);

            if (buttonData.AssemblyName == null || string.IsNullOrWhiteSpace(buttonData.AssemblyName))
            {
                buttonData.AssemblyName = Path.Combine(AppContext.BaseDirectory, "RRX.Xml.dll");
            }

            buttonData.ToolTip = "RRX x Revit öffnen";
            panel.AddItem(buttonData);
        }
    }
}
#endif
