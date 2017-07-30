using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;

namespace SOFiSTiK.Analysis
{
    class SimplifiedManager
    {
            private RvtUI.UIDocument my_uidoc = null;
            private RvtDB.Document my_doc = null;
            private BuildingGraph my_building_graph = null; // ???
            private SimplifiedModel my_simplifiedmodel = null;

            // For the view -> Shall we move it to another class?
            private RvtDB.View3D my_view3D = null;
            private RvtDB.ElementId _viewTemplId = RvtDB.ElementId.InvalidElementId;

            // Give access to the building graph
            public BuildingGraph BuildingGraph { get { return my_building_graph; } }
            // Give access to the Simplified model
            public SimplifiedModel SimplifiedModel { get { return my_simplifiedmodel; } }

            public SimplifiedManager(RvtUI.UIDocument uidoc)
            {
                my_uidoc = uidoc;
                my_doc = my_uidoc.Document;


                // Initialize the building manager and building graph
                BuildingManager building_manager = BuildingManager.GetInitializedInstance(my_doc);
                my_building_graph = building_manager.BuildingGraph;

                my_simplifiedmodel = new SimplifiedModel(my_doc, my_building_graph);

                //check if Main system has been generated 
                if (my_building_graph.CountNodes < 1 || building_manager == null)
                {
                    // Warning 
                }
                else
                {
                    // Create a new View -> TODO: Please remove it from here 
                    my_uidoc.ActiveView = CreateNewView();
                }
            }

            // TODO : Please remove it from here 
            public RvtDB.View3D CreateNewView()
            {
                // 1. Collect the first Family Type that is 3D 
                RvtDB.ViewFamilyType vd = new RvtDB.FilteredElementCollector(my_doc).
                                          OfClass(typeof(RvtDB.ViewFamilyType)).
                                             Cast<RvtDB.ViewFamilyType>().
                                                 FirstOrDefault(p => p.ViewFamily == RvtDB.ViewFamily.ThreeDimensional);


                // 2. Start the transaction 
                using (RvtDB.Transaction trans = new RvtDB.Transaction(my_doc, "Create view"))
                {
                    trans.Start();
                    // 3. Create the new view in the given doc 
                    my_view3D = RvtDB.View3D.CreateIsometric(my_doc, vd.Id);

                    // 3*. CASE 2 
                    // RvtDB.View3D myview_v2 = RvtDB.View3D.CreateIsometric(doc, vd.Id);

                    // 4. Create a list of all the 3D View Templates 
                    var view3DTemplates = new RvtDB.FilteredElementCollector(my_doc).OfClass(typeof(RvtDB.View3D))
                                                     .Cast<RvtDB.View3D>().Where(v3 => v3.IsTemplate).ToList();


                    // 5. Check whether exist Berechnungsmodel and use that, otherwise use the last Template (count-1) -> According to SubsystemSpecifyCmd
                    int subsysIdx = view3DTemplates.FindIndex(v => v.ViewName.StartsWith("05"));
                    if (subsysIdx < 0)
                        subsysIdx = view3DTemplates.Count - 1;
                    if (subsysIdx >= 0)
                        _viewTemplId = view3DTemplates[subsysIdx].Id;


                    // 6. Assign the Template Views to our  (new) View
                    my_view3D.ViewTemplateId = _viewTemplId;

                    // 7. Name the View
                    List<string> view3D_names = new RvtDB.FilteredElementCollector(my_doc).OfClass(typeof(RvtDB.View3D))
                              .Cast<RvtDB.View3D>().Where(v3 => !(v3.IsTemplate)).Select(v3 => v3.ViewName).ToList();

                    // 7.1 Change of the name in case of a same name
                    string name = "Simplified model";
                    int i = 2;

                    while (view3D_names.Contains(name))
                    {
                        name = "Simplified model " + i.ToString();
                        i++;
                    }

                    my_view3D.Name = name;

                    trans.Commit();

                    return my_view3D;
                }
            }
      
    }
}
