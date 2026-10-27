# RRX – Revit Plugin

WPF-Anwendung und Revit-Add-in für den Abgleich eines Revit-Projektbaums. Dieses Repository enthält den Quellcode der beiden Projekte aus der Demo-Anwendung.

## Projektstruktur

- `RRX.Xml/`: WPF-Anwendung, Projektbaum und Revit-Anbindung
- `RRX.DragDrop/`: Drag-and-drop-Komponente für die Oberfläche
- `RRX.Xml/RRX.Xml.slnx`: Projektmappe für beide Projekte

Lokale Revit-Modelle, IFC-Dateien, Beispieldaten, Build-Ausgaben und benutzerspezifische Einstellungen gehören nicht zum Repository.

## Bauen

Voraussetzung ist das .NET 10 SDK unter Windows. Für die Revit-Anbindung wird Revit 2027 mit `RevitAPI.dll` und `RevitAPIUI.dll` benötigt. Ohne diese Dateien wird die bedingte Revit-API-Integration nicht aktiviert.

```powershell
dotnet build .\RRX.Xml\RRX.Xml.csproj -c Release -f net10.0-windows
```

Der Standardpfad für Revit ist `C:\Program Files\Autodesk\Revit 2027\`. Bei einer anderen Installation kann `RevitInstallDir` beim Build gesetzt werden.

## Revit-Add-in einrichten

Die Datei `RRX.Xml/Revit/RRX.Xml.addin` ist eine Vorlage. Nach dem Build beide `<Assembly>`-Einträge auf den **absoluten Pfad** der erzeugten `RRX.Xml.dll` ändern und die angepasste `.addin`-Datei nach `%APPDATA%\Autodesk\Revit\Addins\2027\` kopieren. Die persönliche Pfadangabe nicht zurück in das Repository übernehmen.
