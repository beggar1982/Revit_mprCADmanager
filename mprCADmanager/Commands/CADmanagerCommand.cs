﻿namespace mprCADmanager.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows;
    using Autodesk.Revit.Attributes;
    using Autodesk.Revit.DB;
    using Autodesk.Revit.DB.Events;
    using Autodesk.Revit.UI;
    using Autodesk.Revit.UI.Events;
    using ModPlusAPI;
    using ModPlusAPI.Windows;
    using Revit;
    using View;
    using ViewModel;
    using MessageBox = ModPlusAPI.Windows.MessageBox;

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    //// ReSharper disable once InconsistentNaming
    public class DWGImportManagerCommand : IExternalCommand
    {
        private const string LangItem = "mprCADmanager";
        public static DWGImportManagerWindow MainWindow;
        private DeleteElementEvent _deleteElementEvent;
        private RemoveEvents _removeEvents;
        private ChangeViewEvent _changeViewEvent;
        private DeleteManyElementsEvent _deleteManyElementsEvent;
        private Document _currentDocument;
        private UIApplication _uiApplication;
        private DWGImportManagerVM _dwgImportManagerVm;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
#if !DEBUG
                Statistic.SendCommandStarting(new ModPlusConnector());
#endif

                _uiApplication = commandData.Application;
                _currentDocument = _uiApplication.ActiveUIDocument.Document;

                _deleteElementEvent = new DeleteElementEvent();
                _removeEvents = new RemoveEvents();
                _changeViewEvent = new ChangeViewEvent();
                _deleteManyElementsEvent = new DeleteManyElementsEvent();

                SearchImportsAndBind(false);

                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return Result.Failed;
            }
        }

        private void Application_DocumentCreated(object sender, DocumentCreatedEventArgs e)
        {
            _currentDocument = e.Document;
            SearchImportsAndBind(true);
        }

        private void Application_DocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (MainWindow != null)
            {
                _currentDocument = e.GetDocument();
                var hasImports = false;
                var added = e.GetAddedElementIds();
                var removed = e.GetDeletedElementIds();
                if (added != null && added.Any())
                {
                    foreach (var elementId in added)
                    {
                        if (_currentDocument.GetElement(elementId) is CADLinkType)
                        {
                            hasImports = true;
                            break;
                        }
                    }
                }

                if (removed != null && removed.Any() && _dwgImportManagerVm.DwgImportsItems.Any())
                {
                    foreach (var elementId in removed)
                    {
                        foreach (var dwgImportsItem in _dwgImportManagerVm.DwgImportsItems)
                        {
                            if (dwgImportsItem.Id.Equals(elementId))
                            {
                                hasImports = true;
                                break;
                            }
                        }
                    }
                }

                if (hasImports)
                    SearchImportsAndBind(true);
            }
        }

        private void UiApplication_ViewActivated(object sender, ViewActivatedEventArgs e)
        {
            if (!Equals(e.Document, _currentDocument) && MainWindow != null)
            {
                _currentDocument = e.Document;
                SearchImportsAndBind(true);
            }
        }

        private void Application_DocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            if (_uiApplication.Application.Documents.IsEmpty)
                MainWindow?.Close();
        }

        private void SearchImportsAndBind(bool newActiveViewModel)
        {
            var col = GetElements(_currentDocument);

            if (col.Any())
            {
                if (MainWindow == null)
                {
                    MainWindow = new DWGImportManagerWindow();
                    _deleteManyElementsEvent.MainWindow = MainWindow;
                    MainWindow.Loaded += MainWindowOnLoaded;
                    MainWindow.Closed += MainWindow_Closed;
                    _dwgImportManagerVm = new DWGImportManagerVM(_uiApplication, col, _deleteElementEvent, _changeViewEvent, _deleteManyElementsEvent);
                    MainWindow.DataContext = _dwgImportManagerVm;
                    MainWindow.Show();
                }
                else
                {
                    MainWindow.Activate();
                    if (newActiveViewModel)
                    {
                        _dwgImportManagerVm = new DWGImportManagerVM(_uiApplication, col, _deleteElementEvent, _changeViewEvent, _deleteManyElementsEvent);
                        MainWindow.DataContext = _dwgImportManagerVm;
                    }
                }
            }
            else
            {
                MessageBox.Show(Language.GetItem(LangItem, "msg2"));
                MainWindow?.Close();
            }
        }

        private void MainWindowOnLoaded(object sender, RoutedEventArgs e)
        {
            _uiApplication.Application.DocumentClosed += Application_DocumentClosed;
            _uiApplication.ViewActivated += UiApplication_ViewActivated;
            _uiApplication.Application.DocumentChanged += Application_DocumentChanged;
            _uiApplication.Application.DocumentCreated += Application_DocumentCreated;
        }

        public static List<Element> GetElements(Document document)
        {
            var elements = new List<Element>();

            var collector = new FilteredElementCollector(document)
                .OfClass(typeof(ImportInstance));

            var typesOfImportInstances = new List<int>();
            foreach (var element in collector)
            {
                if (element is ImportInstance importInstance)
                {
                    elements.Add(element);
                    typesOfImportInstances.Add(importInstance.GetTypeId().IntegerValue);
                }
            }

            collector = new FilteredElementCollector(document).OfClass(typeof(CADLinkType));
            foreach (var element in collector)
            {
                if (element is CADLinkType cadLinkType &&
                    !typesOfImportInstances.Contains(cadLinkType.Id.IntegerValue))
                    elements.Add(element);
            }

            return elements;
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                MainWindow = null;
                _removeEvents.SetAction(Application_DocumentClosed, UiApplication_ViewActivated, Application_DocumentChanged, Application_DocumentCreated);
            }
            catch (Exception exception)
            {
                ExceptionBox.Show(exception);
            }
        }
    }
}
