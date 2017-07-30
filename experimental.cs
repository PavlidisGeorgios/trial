using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using System.Text;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using SOFiSTiK.DBUtils;
using Autodesk.Revit.Creation;
using System.Drawing;
using System.Windows;



namespace SOFiSTiK.Analysis
{
    
    public class SimplifiedModel
    {
        private RvtDB.Document my_doc = null;
        private BuildingGraph my_buiilding_graph = null;
        public List<Floor> floors { get; }
        //public List<BuildingGraph.Node> strElements { get; }
        public List<RvtDB.ElementId> boundary_elements { get; }

        public SimplifiedModel(RvtDB.Document doc, BuildingGraph buildinggraph)
        {
            // Initialize the private properties 
            my_doc = doc;
            my_buiilding_graph = buildinggraph;
            floors = new List<Floor>();

            // Error
            double err = 0.001;

            // Every Simplified Model consists of : 
            // 1. Floors ( which consists of Slabs + Structural Elements)
            List<BuildingGraph.Node> slabnodes = my_buiilding_graph.FindNodes(n => { return n.MemberType == BuildingGraph.Node.MemberTypeEnum.Slab;});

            foreach (BuildingGraph.Node slab in slabnodes)
            {
                double floor_height = new double();
                if (slab.AdjacentRelations[0].Connectors[0] is BuildingGraph.ConnectorPoint)
                {
                    var p = slab.AdjacentRelations[0].Connectors[0] as BuildingGraph.ConnectorPoint;// we take only the first connector because every slab has at least one connector
                    floor_height = p.Geometry.Coord.Z;
                }
                else
                {
                    var p = slab.AdjacentRelations[0].Connectors[0] as BuildingGraph.ConnectorLinear;
                    floor_height = (p.Geometry as RvtDB.Line).Origin.Z;
                }
                // We add every slab-node with unique Z_coord to the floor list
                bool flag = floors.Any(k => (Math.Abs(k.height - floor_height)<err));
                if  (!flag)
                    {
                    List<BuildingGraph.Node> my_new_floor = new List<BuildingGraph.Node>();
                    my_new_floor.Add(slab);
                    floors.Add(new Floor(my_doc, my_new_floor));
                }
                // In case that a floor has 2 or more slabs we just add the slab-node to the floor (with the same Z coord)
                else if (flag)
                {
                    Floor myfloor = floors.Find((j => (Math.Abs(j.height - floor_height) < err)));
                    myfloor.Add_Floor(slab);
                }
            }
            
            // 2. Boundary Conditions (elements)
            boundary_elements = new RvtDB.FilteredElementCollector(my_doc).OfCategory(RvtDB.BuiltInCategory.OST_BoundaryConditions).ToElementIds().ToList();

            // 3. Center of Mass (CM) and Center of Rigidity (CR) 
             foreach(Floor floor in floors) // PS: We do it here and not in the constructor "Floors" because we want to cover the case that a floor has >2 slabs 
            {
                floor.calculate_CenterMass();
                floor.calculate_CenterRigidity();
            }
        }

        public List<RvtDB.ElementId> get_elementIDs_of_simplified()
        {
            List<RvtDB.ElementId> myelementId = new List<RvtDB.ElementId>();

            foreach ( var fl in floors)
            {
                myelementId.AddRange(fl.get_elementIDs_of_floors());
                myelementId.AddRange(boundary_elements);
                
            }

            return myelementId;
        }

        private void Visualize() { }
    }

    


    // DELETE ME
    public class SimplifiedModuleManager
    {
        private RvtUI.UIDocument myuidoc;
        private RvtDB.Document mydoc;
        public BuildingGraph mybuilding_graph; // ????
        private RvtDB.View3D myview3D;
        private RvtDB.View myactiveview;
        private RvtDB.Options option;
        private string markerName = "SOFiSTiK Reference Point";
        public List<RvtDB.XYZ> mylist_CM = null;
        public List<RvtDB.ElementId> mylist_CM_id = null;
        public List<RvtDB.XYZ> mylist_CR = null;
        public List<RvtDB.ElementId> mylist_CR_id = null;


        private RvtDB.ElementId _viewTemplId = RvtDB.ElementId.InvalidElementId;
        private string myfilename { get; set; }

        public SimplifiedModuleManager(RvtUI.UIDocument uidoc)
        {
            myuidoc = uidoc;
            mydoc = myuidoc.Document;
            option = new RvtDB.Options();
            mylist_CM = new List<RvtDB.XYZ>();
            mylist_CM_id = new List<RvtDB.ElementId>();
            mylist_CR = new List<RvtDB.XYZ>();
            mylist_CR_id = new List<RvtDB.ElementId>();

            // Initialize the building manager and building graph
            BuildingManager building_manager = BuildingManager.GetInitializedInstance(mydoc);
            mybuilding_graph = building_manager.BuildingGraph;

            //check if Main system has been generated 
            if (mybuilding_graph.CountNodes < 1 || building_manager == null)
            {
                // Warning 
            }
            else
            {
                // Create a new View
                myuidoc.ActiveView = CreateNewView();
            }
        }

        public RvtDB.View3D CreateNewView()
        {
            // 1. Collect the first Family Type that is 3D 
            RvtDB.ViewFamilyType vd = new RvtDB.FilteredElementCollector(mydoc).
                                      OfClass(typeof(RvtDB.ViewFamilyType)).
                                         Cast<RvtDB.ViewFamilyType>().
                                             FirstOrDefault(p => p.ViewFamily == RvtDB.ViewFamily.ThreeDimensional);


            // 2. Start the transaction 
            using (RvtDB.Transaction trans = new RvtDB.Transaction(mydoc, "Create view"))
            {
                trans.Start();
                // 3. Create the new view in the given doc 
                myview3D = RvtDB.View3D.CreateIsometric(mydoc, vd.Id);

                // 3*. CASE 2 
                // RvtDB.View3D myview_v2 = RvtDB.View3D.CreateIsometric(doc, vd.Id);

                // 4. Create a list of all the 3D View Templates 
                var view3DTemplates = new RvtDB.FilteredElementCollector(mydoc).OfClass(typeof(RvtDB.View3D))
                                                 .Cast<RvtDB.View3D>().Where(v3 => v3.IsTemplate).ToList();

                // 5. Check whether exist Berechnungsmodel and use that, otherwise use the last Template (count-1) -> According to SubsystemSpecifyCmd
                int subsysIdx = view3DTemplates.FindIndex(v => v.ViewName.StartsWith("05"));
                if (subsysIdx < 0)
                    subsysIdx = view3DTemplates.Count - 1;
                if (subsysIdx >= 0)
                    _viewTemplId = view3DTemplates[subsysIdx].Id;

                // 6. Assign the Template Views to our  (new) View
                myview3D.ViewTemplateId = _viewTemplId;

                // 7. Name the View
                List<string> view3D_names = new RvtDB.FilteredElementCollector(mydoc).OfClass(typeof(RvtDB.View3D))
                          .Cast<RvtDB.View3D>().Where(v3 => !(v3.IsTemplate)).Select(v3 => v3.ViewName).ToList();

                // 7.1 Change of the name in case of a same name
                string name = "Simplified model";
                int i = 2;
                
                while (view3D_names.Contains(name))
                { 
                    name = "Simplified model " + i.ToString();
                    i++;
                }

                myview3D.Name = name;
               

                // 7*. CASE 2
                /*
                myview_v2.Name = " Simplfied model version 2";
                myview_v2.AreAnalyticalModelCategoriesHidden = false;
                myview_v2.IsolateElementsTemporary(isolated);
                */


                trans.Commit();

                return myview3D;
            }
        }

        public void HideFromView(ICollection<RvtDB.ElementId> isolatedid)
        {
            using (RvtDB.Transaction trans = new RvtDB.Transaction(mydoc, "Create view"))
            {
                myactiveview = myuidoc.ActiveView;
                // Strange that you cannot isolate or unhide the elements in the same transaction 
                trans.Start();                
                // 9. Isolate the elements and enable the analytical model
                myactiveview.IsolateElementsTemporary(isolatedid);
                myactiveview.AreAnalyticalModelCategoriesHidden = false;

                // 8. Remove the Template from the View -> DO WE REALLY NEED IT ?
              //  RvtDB.Parameter par = myactiveview.GetParameters("View Template").First();
              //  par.Set(new RvtDB.ElementId(-1));


                trans.Commit();
            }
        }

        public void HideFromView(ICollection<RvtDB.Element> isolated)
        {
            List<RvtDB.ElementId> isolatedid = new List<RvtDB.ElementId>();

            foreach (RvtDB.Element el in isolated)
            {
                isolatedid.Add(el.Id);
            }
            using (RvtDB.Transaction trans = new RvtDB.Transaction(mydoc, "Create view"))
            {
                myactiveview = myuidoc.ActiveView;
                // Strange that you cannot isolate or unhide the elements in the same transaction 
                trans.Start();
                // 9. Isolate the elements and enable the analytical model
                myactiveview.IsolateElementsTemporary(isolatedid);
                myactiveview.AreAnalyticalModelCategoriesHidden = false;
                trans.Commit();
            }
        }

        public RvtDB.XYZ GetCenterofMass(BuildingGraph.Node node)
        {
            RvtDB.Element myele = myuidoc.Document.GetElement(node.Member);
            RvtDB.GeometryElement gm = myele.get_Geometry(option);
            RvtDB.Solid so = gm.First() as RvtDB.Solid;
            RvtDB.PlanarFace fc = so.Faces.get_Item(0) as RvtDB.PlanarFace;

            foreach (RvtDB.PlanarFace f in so.Faces)
            {
                if (f.FaceNormal == new RvtDB.XYZ(0, 0, -1)) fc = f;
            }
            RvtDB.XYZ max = new RvtDB.XYZ();
            RvtDB.XYZ min = new RvtDB.XYZ();

            RvtDB.Mesh mesh = fc.Triangulate();

            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                RvtDB.MeshTriangle triangle = mesh.get_Triangle(i);
                RvtDB.XYZ vertex1 = triangle.get_Vertex(0);
                RvtDB.XYZ vertex2 = triangle.get_Vertex(1);
                RvtDB.XYZ vertex3 = triangle.get_Vertex(2);
            }

            foreach (RvtDB.XYZ vx in mesh.Vertices)
            {
                //Comparing points
                if (vx.X > max.X) max = new RvtDB.XYZ(vx.X, max.Y, 0);
                if (vx.Y > max.Y) max = new RvtDB.XYZ(max.X, vx.Y, 0);
                if (vx.X < min.X) min = new RvtDB.XYZ(vx.X, min.Y, 0);
                if (vx.Y < min.Y) min = new RvtDB.XYZ(min.X, vx.Y, 0);
            }
            RvtDB.XYZ midSum = max + min;

            // Attention : Ft to meter is not required
            mylist_CM.Add(new RvtDB.XYZ(midSum.X / 2, midSum.Y / 2, 0));
            return new RvtDB.XYZ(midSum.X / 2, midSum.Y / 2, 0);
        }

        public RvtDB.XYZ GetCenterofRigidity(BuildingGraph.Node node)
        {
            // 2a Find all the walls that are connected with the each slab
            List<BuildingGraph.Relation> slab_wallcolunm_relation = new List<BuildingGraph.Relation>();
            slab_wallcolunm_relation = node.FindRelations(relation =>
            {
                return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Wall && relation.Direction == BuildingGraph.Relation.DirectionEnum.Down //||
                                                                                                                                                                 //      (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Column && relation.Direction == BuildingGraph.Relation.DirectionEnum.Down
                    ;
            });

            // Initialize the stiffness in each direction and (stiffness X distance) 
            double SIyy = new double();
            double SIxx = new double();
            double SIyy_y = new double();
            double SIxx_x = new double();

            // delete me 
            double Ixx = new double();
            double Iyy = new double();
            double SIyy2 = new double();
            double SIxx2 = new double();
            double SIyy_y2 = new double();
            double SIxx_x2 = new double();
            double e_yy2 = new double();
            double e_xx2 = new double();

            // eccentricy in x and y direction 
            double e_yy = new double();
            double e_xx = new double();

            List<double> overview = new List<double>();
            List<double> overviewwidth = new List<double>();

            foreach (BuildingGraph.Relation rel in slab_wallcolunm_relation)
            {
                foreach (BuildingGraph.Connector con in rel.Connectors)
                {
                    if (con is BuildingGraph.ConnectorLinear) // if the connector is linear that means that ...
                    {
                        var p = con as BuildingGraph.ConnectorLinear;// we take only the first connector because ...
                        var gep = p.Geometry as RvtDB.Line;
                        // TODO : GetThickness should take into consideration the direction as a vector and not as a bool
                        double mythickness = 0.3048 * GetWallThickness(rel.Target.BoundingBox.Min, rel.Target.BoundingBox.Max, Math.Abs(gep.Direction.X) == 1);// Get the thickness of each wall through the bounding box information
                        double mywidth = 0.3048 * GetWallWidth(rel.Target.BoundingBox.Min, rel.Target.BoundingBox.Max, Math.Abs(gep.Direction.X) == 1);// Get the width of each wall through the bounding box infromation

                        overview.Add(mythickness);
                        overviewwidth.Add(mywidth);

                        Ixx = Math.Pow(gep.Length * 0.3048, 3) * 0.25 * Math.Pow(gep.Direction.Y, 2) / 12;
                        Iyy = Math.Pow(gep.Length * 0.3048, 3) * 0.25 * Math.Pow(gep.Direction.X, 2) / 12;

                        SIxx2 += Ixx;
                        SIxx_x2 += Ixx *0.3048 * (gep.Origin.X  + gep.Direction.X * gep.Length  * 0.5);

                        SIyy2 += Iyy;
                        SIyy_y2 += Iyy  *  0.3048 * (gep.Origin.Y  + gep.Direction.Y * gep.Length  * 0.5);

                        // 3. Calculate the stiffness of each wall
                        double I = mythickness * Math.Pow(mywidth, 3) / 12;

                        if (Math.Abs(gep.Direction.X) == 1)
                        {
                            // 4. Calculate the distance of each wall from the center of origin (arbitrary)
                            SIxx_x += I * rel.Target.BoundingBox.Center.Y * 0.3048;
                            SIxx += I;
                        }
                        else if (Math.Abs(gep.Direction.Y) == 1)
                        {
                            // 4. Calculate the distance of each wall from the center of origin (arbitrary)
                            SIyy += I;
                            SIyy_y += I * rel.Target.BoundingBox.Center.X * 0.3048;
                        }
                        else // TODO : Check what happens when we have inclined shear wall
                        {
                            // Both directions
                        }
                    }
                }
            }
            // 5.Calculate the XYZ(point) of C.R
            e_xx = SIyy_y / SIyy / 0.3048;
            e_yy = SIxx_x / SIxx / 0.3048;

            e_xx2 = SIyy_y2 / SIyy2 / 0.3048;
            e_yy2 = SIxx_x2 / SIxx2 / 0.3048;

            // check if e_xx or e_yy is NaN : case where there is no wall in x or y direction 
            //if (double.IsNaN(e_xx)) e_xx = mid.X;
            //if (double.IsNaN(e_yy)) e_yy = mid.Y;
            mylist_CR.Add(new RvtDB.XYZ(e_xx2, e_yy2, node.BoundingBox.Center.Z));
            return new RvtDB.XYZ(e_xx, e_yy, node.BoundingBox.Center.Z);
        }

        public RvtDB.XYZ GetCenterofRigidity2(BuildingGraph.Node node)
        {
            // 2a Find all the walls that are connected with the each slab
            List<BuildingGraph.Relation> slab_wallcolunm_relation = new List<BuildingGraph.Relation>();
            slab_wallcolunm_relation = node.FindRelations(relation =>
            {
                return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Wall && relation.Direction == BuildingGraph.Relation.DirectionEnum.Down //||
                                                                                                                                                                 //      (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Column && relation.Direction == BuildingGraph.Relation.DirectionEnum.Down
                    ;
            });

            // Initialize the stiffness in each direction and (stiffness X distance) 
            double SIyy = new double();
            double SIxx = new double();
            double SIyy_y = new double();
            double SIxx_x = new double();
            //double Ixx = new double();
            //double Iyy = new double();

            // eccentricy in x and y direction 
            double e_yy = new double();
            double e_xx = new double();

            List<double> overview = new List<double>();
            List<double> overviewwidth = new List<double>();

            foreach (BuildingGraph.Relation rel in slab_wallcolunm_relation)
            {

                if (rel.Connectors[0] is BuildingGraph.ConnectorLinear) // if the connector is linear that means that ...
                {
                    var p = rel.Connectors[0] as BuildingGraph.ConnectorLinear;// we take only the first connector because ...
                    var gep = p.Geometry as RvtDB.Line;

                    
                    
                    //Ixx = 
                   // SIxx_x += 
                    // TODO : GetThickness should take into consideration the direction as a vector and not as a bool
                    double mythickness = 0.3048 * GetWallThickness(rel.Target.BoundingBox.Min, rel.Target.BoundingBox.Max, Math.Abs(gep.Direction.X) == 1);// Get the thickness of each wall through the bounding box information
                    double mywidth = 0.3048 * GetWallWidth(rel.Target.BoundingBox.Min, rel.Target.BoundingBox.Max, Math.Abs(gep.Direction.X) == 1);// Get the width of each wall through the bounding box infromation


                    // 3. Calculate the stiffness of each wall
                    double I = mythickness * Math.Pow(mywidth, 3) / 12;

                    if (Math.Abs(gep.Direction.X) == 1)
                    {
                        // 4. Calculate the distance of each wall from the center of origin (arbitrary)
                        SIxx_x += I * rel.Target.BoundingBox.Center.Y * 0.3048;
                        SIxx += I;
                    }
                    else if (Math.Abs(gep.Direction.Y) == 1)
                    {
                        // 4. Calculate the distance of each wall from the center of origin (arbitrary)
                        SIyy += I;
                        SIyy_y += I * rel.Target.BoundingBox.Center.X * 0.3048;
                    }
                    else // TODO : Check what happens when we have inclined shear wall
                    {
                        // Both directions
                    }
                }
            }
            // 5.Calculate the XYZ(point) of C.R
            e_xx = SIyy_y / SIyy / 0.3048;
            e_yy = SIxx_x / SIxx / 0.3048;

            // check if e_xx or e_yy is NaN : case where there is no wall in x or y direction 
            //if (double.IsNaN(e_xx)) e_xx = mid.X;
            //if (double.IsNaN(e_yy)) e_yy = mid.Y;
            mylist_CR.Add(new RvtDB.XYZ(e_xx, e_yy, node.BoundingBox.Center.Z));
            return new RvtDB.XYZ(e_xx, e_yy, node.BoundingBox.Center.Z);
        }

        public void CreateMarker(RvtDB.XYZ mypoint)
        { 
            // Collect all the Family Symbols of the project 
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(mydoc).OfClass(typeof(RvtDB.FamilySymbol));

            Func<RvtDB.FamilySymbol, bool> isTargetFamily = famSym => famSym.Name.Contains(markerName);

            RvtDB.FamilySymbol targetFamily
              = collector.OfType<RvtDB.FamilySymbol>()
                .Where<RvtDB.FamilySymbol>(isTargetFamily)
                .First<RvtDB.FamilySymbol>();

            RvtDB.FamilyInstance result = null;

            using (RvtDB.Transaction t = new RvtDB.Transaction(mydoc, " Insert the generic family instance "))
            {
                t.Start();
                try
                {
                    // First, we need to check if that Family exists 
                    result = mydoc.Create.NewFamilyInstance(mypoint, targetFamily, RvtDB.Structure.StructuralType.NonStructural);
                    t.Commit();
                }
                catch (Exception)
                {
                    t.RollBack();
                }
            }
        }

        public void DeleteMarker(RvtDB.ElementId elementid)
        {
            using (RvtDB.Transaction t = new RvtDB.Transaction(mydoc, " Insert the generic family instance "))
            {
                t.Start();
                try
                {
                    // First, we need to check if that Family exists 
                    mydoc.Delete(elementid);
                    t.Commit();
                }
                catch (Exception)
                {
                    t.RollBack();
                }
            }
        }


        // Maybe we have to move that functions to the utiliziers
        // Get the heigh a wall -  dir is true when the element is at X direction and false when at Y direction
        public double GetWallThickness(RvtDB.XYZ min, RvtDB.XYZ max, bool dir)
        {
            double thickness = new double();
            if (!dir)
            {
                thickness = max.X - min.X;
            }
            else
            {
                thickness = max.Y - min.Y;
            }
            return thickness;
        }

        // Get the heigh a wall  - dir is true when the element is at X direction and false when at Y direction
        public double GetWallWidth(RvtDB.XYZ min, RvtDB.XYZ max, bool dir)
        {
            double width = new double();
            if (dir)
            {
                width = max.X - min.X;
            }
            else
            {
                width = max.Y - min.Y;
            }
            return width;
        }

        public RvtDB.XYZ GetEndPoint(RvtDB.XYZ startpoint, double length, RvtDB.XYZ direction)
        {
            return new RvtDB.XYZ(startpoint.X + direction.X * length, startpoint.Y + direction.Y * length, startpoint.Z + direction.Z * length);
        }

    }

    
}

namespace SOFiSTiK.Analysis
{
    // The value of TransactionMode.Manual for the TransactionAttribute requests
    // that Revit not create a transaction automatically 
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    // NoLicCommand returns true for the license
    // Otherwise switch to FeaCommand
    public class ExperimentalCommand01 : NoLicCommand
    {
        #region functions

        // Check whether the selected walls are connected
        public bool WallsAreConnected (RvtDB.Document doc,ICollection<RvtDB.ElementId> ids)
        {
            bool flag = new bool();
            var building_manager = BuildingManager.GetInitializedInstance(doc);
            var building_graph = building_manager.BuildingGraph;

            // Access all the slabs through the building manager     
            List<BuildingGraph.Node> mywalls = building_graph.FindNodes(ids);

            // Initialize the list of the relation btw slab and walls
            List<BuildingGraph.Relation> slab_wallcolunm_relation = new List<BuildingGraph.Relation>();

            foreach (BuildingGraph.Node wall in mywalls)
            {
                slab_wallcolunm_relation = wall.FindRelations(relation =>
                {
                    return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Wall && relation.Direction == BuildingGraph.Relation.DirectionEnum.Horizontal;
                });
                flag = false;

                foreach (BuildingGraph.Relation rel in slab_wallcolunm_relation)
                {
                    if (mywalls.Contains(building_graph.FindNode(rel.Target.Member))) flag = true;
                }
                if (!flag) return flag;
            }
            return flag;
        }

        // Find relations btw walls UNDER CONSTRUCTION
        public void Wallrelation(RvtDB.Document doc)
        {
            var building_manager = BuildingManager.GetInitializedInstance(doc);
            var building_graph = building_manager.BuildingGraph;

            // Access all the slabs through the building manager     
            List<BuildingGraph.Node> myslabs = building_graph.FindNodes(n => { return n.MemberType == BuildingGraph.Node.MemberTypeEnum.Slab; });

            // Initialize the list of the relation btw slab and walls
            List<List<BuildingGraph.Relation>> slab_wallcolunm_relation = new List<List<BuildingGraph.Relation>>();

            List<List<BuildingGraph.Relation>> wall_wall_relation = new List<List<BuildingGraph.Relation>>();

            foreach (BuildingGraph.Node n in myslabs)
            {
                //  Find all the walls that are connected with the each slab
                slab_wallcolunm_relation.Add(n.FindRelations(relation =>
                {
                    return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Wall && relation.Direction == BuildingGraph.Relation.DirectionEnum.Down //||
                                                     //      (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Column && relation.Direction == BuildingGraph.Relation.DirectionEnum.Down
                        ;
                }));
                foreach (List<BuildingGraph.Relation> rel in slab_wallcolunm_relation)
                {
                    foreach (BuildingGraph.Relation r in rel)
                    {
                        wall_wall_relation.Add(r.Target.FindRelations(relation => { return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Wall && relation.Direction == BuildingGraph.Relation.DirectionEnum.Horizontal; }));
                    }
                }

            }
        }

        // Get the center of mass
        public RvtDB.XYZ GetCenterofMass(RvtUI.UIDocument uidoc, RvtDB.ElementId elid)
        {
            RvtDB.Options option = new RvtDB.Options();
            RvtDB.Element myele = uidoc.Document.GetElement(elid);
            RvtDB.GeometryElement gm = myele.get_Geometry(option);
            RvtDB.Solid so = gm.First() as RvtDB.Solid;
            RvtDB.PlanarFace fc = so.Faces.get_Item(0) as RvtDB.PlanarFace;


            foreach (RvtDB.PlanarFace f in so.Faces)
            {
                if (f.FaceNormal == new RvtDB.XYZ(0, 0, -1)) fc = f;
            }
            RvtDB.XYZ max = new RvtDB.XYZ();
            RvtDB.XYZ min = new RvtDB.XYZ();

            int no = 0;

            List<RvtDB.XYZ> coord = new List<RvtDB.XYZ>();


            RvtDB.Mesh mesh = fc.Triangulate();


            foreach (RvtDB.XYZ vx in mesh.Vertices)
            {
                //RvtUI.TaskDialog.Show("Point:" + no.ToString(), vx.ToString());
                no++;

                //Comparing points
                if (vx.X > max.X) max = new RvtDB.XYZ(vx.X, max.Y, 0);
                if (vx.Y > max.Y) max = new RvtDB.XYZ(max.X, vx.Y, 0);
                if (vx.X < min.X) min = new RvtDB.XYZ(vx.X, min.Y, 0);
                if (vx.Y < min.Y) min = new RvtDB.XYZ(min.X, vx.Y, 0);
            }
            RvtDB.XYZ midSum = max + min;

            // Attention : Ft to meter is not required
            return new RvtDB.XYZ(midSum.X / 2, midSum.Y / 2, 0);

        }

        // Create Dimensions
        public void CreateDimension(RvtDB.Document doc, RvtDB.XYZ startpoint , RvtDB.XYZ endpoint)
        {
            RvtDB.Options _opt = new RvtDB.Options();
            _opt.ComputeReferences = true;
            _opt.IncludeNonVisibleObjects = true;

            RvtDB.XYZ[] pts = new RvtDB.XYZ[2];
            RvtDB.Reference[] refs = new RvtDB.Reference[2];

            pts[0] = startpoint;
            pts[1] = endpoint;


        }

        // Test placing a family symbol in all views (ONLY DEBUGGING OPTION )
        void PlaceInstancesOnViews(RvtDB.Document doc)
        {
            RvtDB.FilteredElementCollector collector
              = new RvtDB.FilteredElementCollector(doc);

            collector.OfClass(typeof(RvtDB.FamilySymbol));

            Func<RvtDB.FamilySymbol, bool> isTargetFamily
              = famSym => famSym.Name.Contains("SOFiSTiK Reference Point");

            RvtDB.FamilySymbol targetFamily
              = collector.OfType<RvtDB.FamilySymbol>()
                .Where<RvtDB.FamilySymbol>(isTargetFamily)
                .First< RvtDB.FamilySymbol>();

            collector = new RvtDB.FilteredElementCollector(doc);
            collector.OfClass(typeof(RvtDB.View));

            Func<RvtDB.View, bool> isPossibleView
              = v => !v.IsTemplate && !(v is RvtDB.View3D);

            IEnumerable<RvtDB.View> possibleViews
              = collector.OfType<RvtDB.View>()
                .Where<RvtDB.View>(isPossibleView);

            foreach (RvtDB.View view in possibleViews)
            {
                Transaction t = new Transaction(doc);

                t.start(" now " );

                try
                {
                    RvtDB.FamilyInstance result
                      = doc.Create.NewFamilyInstance(
                        RvtDB.XYZ.Zero, targetFamily, RvtDB.Structure.StructuralType.NonStructural);

                    t.commit();
                }
                catch 
                {
                    t.rollBack();
                }
            }
        }

        // Create a label(text) at the given position UNDER CONSTRUCTION
        public void CreateLabel(RvtDB.Document doc,RvtDB.ElementId id, RvtDB.XYZ location, string str)
        {
            RvtDB.TextNoteOptions txtoption = new RvtDB.TextNoteOptions();

            RvtDB.TextNoteType textType = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.TextNoteType)).FirstElement() as RvtDB.TextNoteType;

            Transaction t = new Transaction(doc);
            t.start(" Create Text ");

            try
            {
                RvtDB.TextNote txtNote = RvtDB.TextNote.Create(doc,doc.ActiveView.Id, location, 10, str, textType.Id);

                t.commit();
            }
            catch
            {
                t.rollBack();
            }
        }

        // Get the name of the generic family
        public void CreateMarker(RvtDB.Document doc,RvtDB.XYZ mypoint,string str)
        {
            // Collect all the Family Symbols of the project 
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.FamilySymbol));

            Func<RvtDB.FamilySymbol, bool> isTargetFamily = famSym => famSym.Name.Contains(str);
            
            RvtDB.FamilySymbol targetFamily
              = collector.OfType<RvtDB.FamilySymbol>()
                .Where<RvtDB.FamilySymbol>(isTargetFamily)
                .First<RvtDB.FamilySymbol>();

            RvtDB.FamilyInstance result = null;
            result = doc.Create.NewFamilyInstance(mypoint, targetFamily, RvtDB.Structure.StructuralType.NonStructural);



        }

        // Get the heigh a wall -  dir is true when the element is at X direction and false when at Y direction
        public double GetThickness (RvtDB.XYZ min , RvtDB.XYZ max, bool dir)
        {
            double thickness = new double();
            if (!dir)
            {
                thickness = max.X - min.X;
            }
            else
            {
                thickness = max.Y - min.Y;
            }
            return thickness;
        }

        // Get the heigh a wall  - dir is true when the element is at X direction and false when at Y direction
        public double GetWidth(RvtDB.XYZ min, RvtDB.XYZ max, bool dir)
        {
            double width = new double();
            if (dir)
            {
                width = max.X - min.X;
            }
            else
            {
                width = max.Y - min.Y;
            }
            return width;
        }

        // Get X,Y,Z coordinate of an element (return -1 if it fails) - Elevate the Z coordinate of the first input with the height of the second point
        public RvtDB.XYZ elevation(RvtDB.Document doc, RvtDB.XYZ point, RvtDB.XYZ height)
        {
            RvtDB.XYZ mypoint = new RvtDB.XYZ(point.X, point.Y, height.Z);
            return mypoint;
        }

        // Split an line element into 2 pieces - input ( start , end )  output (start1 , end1, start2, end2)
        List<RvtDB.XYZ> Splitmyelement(RvtDB.Document doc, List<RvtDB.XYZ> elem)
        {
            List<RvtDB.XYZ> mynewElemenets = new List<RvtDB.XYZ>(elem.Count*2);
            mynewElemenets.Add(new RvtDB.XYZ(elem[0].X, elem[0].Y, elem[0].Z));
            mynewElemenets.Add(new RvtDB.XYZ((elem[0].X + elem[1].X) / 2, (elem[0].Y+ elem[1].Y )/2, elem[0].Z));
            mynewElemenets.Add(new RvtDB.XYZ((elem[0].X + elem[1].X) / 2, (elem[0].Y + elem[1].Y) / 2, elem[0].Z));
            mynewElemenets.Add(new RvtDB.XYZ(elem[1].X, elem[1].Y, elem[1].Z));
            return mynewElemenets;
        }

        public double Getcoordinate(RvtDB.Document doc, RvtDB.Element el, string XYZ)
        {
            RvtDB.Curve c = (el.Location as RvtDB.LocationCurve).Curve;
            RvtDB.XYZ wallOrigin = c.GetEndPoint(0);
            RvtDB.XYZ wallEndPoint = c.GetEndPoint(1);
            // check first if wallOrigin.Z == wallEndPoint.Z 
            switch (XYZ)
            {
                case "X":
                    return wallEndPoint.X;
                case "Y":
                    return wallEndPoint.Y;
                case "Z":
                    return wallEndPoint.Z;
            }
            return -1;
        }

        // Get wall opening
        public void GetWallOpening(RvtDB.Document doc,RvtDB.Element el)
        {
            RvtDB.Curve c = (el.Location as RvtDB.LocationCurve).Curve;
            RvtDB.XYZ wallOrigin = c.GetEndPoint(0);
            RvtDB.XYZ wallEndPoint = c.GetEndPoint(1);
            RvtDB.XYZ wallDirection = wallEndPoint - wallOrigin;
            double walllength = wallDirection.GetLength();
            wallDirection = wallDirection.Normalize();
            CreateLine(doc, wallOrigin, wallEndPoint);

        }
       
        // Other idea check if that works better
        // Get wall opening as a list of XYZ
        //      *C*       *D* 
        //
        //
        //      *A*       *B*
        public List<RvtDB.XYZ> GetWallCoordinates(RvtDB.Document doc, RvtDB.Element el)
        {
            List<RvtDB.XYZ> mycoordinates = new List<RvtDB.XYZ>();

            RvtDB.Curve c = (el.Location as RvtDB.LocationCurve).Curve;
            if (c.Tessellate().Count == 2)
            {
                // Line
                RvtDB.XYZ wallOrigin = c.GetEndPoint(0);
                RvtDB.XYZ wallEndPoint = c.GetEndPoint(1);

                mycoordinates.Add(wallOrigin);
                mycoordinates.Add(wallEndPoint);
            }
            else
            {
                Debug.rvt_assert(true, " Curve is not a line");
            }
            return mycoordinates;

        }

        public List<RvtDB.XYZ> GetWallCoordinates(RvtDB.Document doc, RvtDB.XYZ origin,double length, RvtDB.XYZ direction)
        {
            List<RvtDB.XYZ> mycoordiantes = new List<RvtDB.XYZ>();

            mycoordiantes.Add(origin);
            mycoordiantes.Add(new RvtDB.XYZ(origin.X + length * direction.X, origin.Y + length * direction.Y, origin.Z + length * direction.Z));

            return mycoordiantes;
        }
        
        // Get the Normal of a Line/Curve
        private RvtDB.XYZ GetCurveNormal (RvtDB.XYZ p, RvtDB.XYZ q)
        {
        //    IList<RvtDB.XYZ> pts = curve.Tessellate();
        //    int n = pts.Count;

         //   RvtDB.XYZ p = pts[0];
        //    RvtDB.XYZ q = pts[n - 1];
            RvtDB.XYZ v = q - p;
            RvtDB.XYZ w, normal = null,axis=null;
            int n = 2;
            if (2 == n)
            {
               // RvtUI.TaskDialog.Show("Revit ", " Curve is a line");
                axis = p.CrossProduct(q);
                
                axis = axis.Normalize();
                double dxy = Math.Abs(v.X) + Math.Abs(v.Y);

                w = (dxy > Utilities.TolPointOnPlane)
                  ? RvtDB.XYZ.BasisZ
                  : RvtDB.XYZ.BasisY;

                normal = v.CrossProduct(w).Normalize();
            }
            /*
            else
            {
                int i = 0;
                while (++i < n - 1)
                {
                    w = pts[i] - p;
                    normal = v.CrossProduct(w);
                    {
                        if (!normal.IsZeroLength())
                            normal = normal.Normalize();
                        break;
                    }
                }
            }
            */
            return axis;

        }

        // Create an element
        public void CreateLine (Autodesk.Revit.DB.Document  doc, RvtDB.XYZ startPoint, RvtDB.XYZ endPoint)
        {
            //RvtDB.Structure.StructuralType stBeam = RvtDB.Structure.StructuralType.Beam;
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc);
            collector.OfClass(typeof(RvtDB.FamilySymbol)).OfCategory(RvtDB.BuiltInCategory.OST_StructuralFraming);
            RvtDB.FamilySymbol symbol = collector.FirstElement() as RvtDB.FamilySymbol;

            
            Autodesk.Revit.ApplicationServices.Application application = RevitDocument.Application;
            if (!symbol.IsActive)
                { symbol.Activate(); doc.Regenerate(); }
                // Create a geometry line
            RvtDB.Line geomLine = RvtDB.Line.CreateBound(startPoint, endPoint);
            
            RvtDB.XYZ origin = startPoint;
            RvtDB.XYZ normal = GetCurveNormal(startPoint, endPoint);
            RvtDB.Plane geomPlane = RvtDB.Plane.CreateByNormalAndOrigin(normal, origin);

            RvtDB.SketchPlane sketch = RvtDB.SketchPlane.Create(doc, geomPlane);

            RvtDB.ModelLine line = doc.Create.NewModelCurve(geomLine, sketch) as RvtDB.ModelLine;

          //  RvtDB.FamilyInstance fi = doc.Create.NewFamilyInstance(geomLine, symbol, null, stBeam);
        }

        // Create a point at a given XYZ
        public void CreatePoint(Autodesk.Revit.DB.Document doc, RvtDB.XYZ location)
        {
            
        }

        // Create an element out of a list of 2 XYZ
        public void Creator(Autodesk.Revit.DB.Document doc, List<RvtDB.XYZ> Points)
        {
            
            RvtDB.Structure.StructuralType stBeam = RvtDB.Structure.StructuralType.Beam;
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc);
            collector.OfClass(typeof(RvtDB.FamilySymbol)).OfCategory(RvtDB.BuiltInCategory.OST_StructuralFraming);
            RvtDB.FamilySymbol symbol = collector.FirstElement() as RvtDB.FamilySymbol;
            
            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "ViewDuplicate"))
            {
                RvtDB.XYZ startPoint = Points[0];
                RvtDB.XYZ endPoint = Points[1];
                trans.Start();
                Autodesk.Revit.ApplicationServices.Application application = RevitDocument.Application;


                if (!symbol.IsActive)
                { symbol.Activate(); doc.Regenerate(); }


                // Create a geometry line
                RvtDB.Line geomLine = RvtDB.Line.CreateBound(startPoint, endPoint);

                RvtDB.XYZ origin = startPoint;
                RvtDB.XYZ normal = GetCurveNormal(startPoint, endPoint);
                RvtDB.Plane geomPlane = RvtDB.Plane.CreateByNormalAndOrigin(normal, origin);

                RvtDB.SketchPlane sketch = RvtDB.SketchPlane.Create(doc, geomPlane);
                collector.OfClass(typeof(RvtDB.SketchPlane)).GetElementCount();


                RvtDB.ModelLine line = doc.Create.NewModelCurve(geomLine, sketch) as RvtDB.ModelLine;
      
                RvtDB.FamilyInstance fi = doc.Create.NewFamilyInstance(geomLine, symbol, null, stBeam);
                trans.Commit();
            }
        }
        
        // Get the centroid of a list of points (polygon)
        public RvtDB.XYZ GetCentroid (List<RvtDB.XYZ> coordinates)
        {
            double tmp;
            int k;

            double area = 0;
            double Cx = 0;
            double Cy = 0;

            for (int i = 0; i < coordinates.Count()-1; i++)
            {
                k = (i + 1) % (coordinates.Count() + 1);
                tmp = coordinates[i].X * coordinates[k].Y - coordinates[k].X * coordinates[i].Y;
                area += tmp;

                Cx += (coordinates[i].X + coordinates[k].X) * tmp;
                Cy += (coordinates[i].Y + coordinates[k].Y) * tmp;

            }
            area = area / 2;

            Cx = Cx / 6 / area;
            Cy = Cy / 6 / area;

            // Convert from ft to meter

            RvtDB.XYZ Centroid = new RvtDB.XYZ(Cx*0.3048,Cy*0.3048,0);
            return Centroid;
        }

        // MISSING
        public RvtDB.PlanarFace GetTopFace(RvtDB.Solid solid)
        {
            RvtDB.PlanarFace topFace = null;
            RvtDB.FaceArray faces = solid.Faces;
            foreach (RvtDB.Face f in faces)
            {
                RvtDB.PlanarFace pf = f as RvtDB.PlanarFace;
                if (null != pf
                 /* && Utilities.IsHorizontal(pf)*/)
                {
                    if ((null == topFace)
                      || (topFace.Origin.Z < pf.Origin.Z))
                    {
                        topFace = pf;
                    }
                }
            }
            return topFace;
        }

        // Create new View
        public void View (RvtDB.Document doc)
        {
            // Collect all the View of the project
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.View));

            // Get the specific view 
            IEnumerable<RvtDB.View> views = from RvtDB.View f in collector where (f.ViewType == RvtDB.ViewType.ThreeD) select f;

            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "ViewDuplicate"))
            {
                trans.Start();
                foreach (RvtDB.View vw in views)
                {
                    // Duplicate the existing ViewPlan view
                    RvtDB.View newView = doc.GetElement(vw.Duplicate(RvtDB.ViewDuplicateOption.Duplicate)) as RvtDB.View;

                    // Assign the desired name
                  //  if (null != newView)
                        newView.ViewName = "MyViewName";
                }
                trans.Commit();
            }

        }

        // Create a new 3D view of the project 
        public RvtDB.View3D ViewFloor (RvtDB.Document doc)
        { 
            // 1. Collect the first Family Type that is 3D 
            RvtDB.ViewFamilyType vd = new RvtDB.FilteredElementCollector(doc).
                                      OfClass(typeof(RvtDB.ViewFamilyType)).
                                         Cast<RvtDB.ViewFamilyType>().
                                             FirstOrDefault(p => p.ViewFamily == RvtDB.ViewFamily.ThreeDimensional);
         
            RvtDB.ElementId _viewTemplId = RvtDB.ElementId.InvalidElementId;

            // 2. Start the transaction 
            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Create view"))
            {
                trans.Start();
                // 3. Create the new view in the given doc 
                RvtDB.View3D myview = RvtDB.View3D.CreateIsometric(doc, vd.Id);

                // 3*. CASE 2 
                // RvtDB.View3D myview_v2 = RvtDB.View3D.CreateIsometric(doc, vd.Id);
                
                // 4. Create a list of all the 3D View Templates 
                var view3DTemplates = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.View3D))
                                                 .Cast<RvtDB.View3D>().Where(v3 => v3.IsTemplate).ToList();
                
                // 5. Check whether exist Berechnungsmodel and use that, otherwise use the last Template (count-1) -> According to SubsystemSpecifyCmd
                int subsysIdx = view3DTemplates.FindIndex(v => v.ViewName.StartsWith("05"));
                if (subsysIdx < 0)
                    subsysIdx = view3DTemplates.Count - 1;
                if (subsysIdx >= 0)
                    _viewTemplId = view3DTemplates[subsysIdx].Id;
                
                // 6. Assign the Template Views to our  (new) View
              //  myview.ViewTemplateId = _viewTemplId;

                // 7. Name the View


                List<string> view3D_names = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.View3D))
                          .Cast<RvtDB.View3D>().Where(v3 => !(v3.IsTemplate)).Select(v3 => v3.ViewName).ToList();

                string name = "Simplified model";
                int i = 2;
                if (view3D_names.Contains(name)) 
                {
                    myview.Name = name + " " + i.ToString();
                    i++;
                    name = myview.Name;
                }
                else
                {
                    myview.Name = "Simplified model";
                }
                

                // 7*. CASE 2
                /*
                myview_v2.Name = " Simplfied model version 2";
                myview_v2.AreAnalyticalModelCategoriesHidden = false;
                myview_v2.IsolateElementsTemporary(isolated);
                */


                trans.Commit();

                return myview;
            }
        }

        // Hide some elements from a given view
        public void HideFromView(RvtDB.Document doc, RvtDB.View myview, ICollection<RvtDB.ElementId> isolated)
        {
            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Create view"))
            {
                // Strange that you cannot isolate or unhide the elements in the same transaction 
                trans.Start();

                // 9. Isolate the elements and enable the analytical model
                myview.IsolateElementsTemporary(isolated);
                myview.AreAnalyticalModelCategoriesHidden = false;

                // 8. Remove the Template from the View -> DO WE REALLY NEED IT ?
                RvtDB.Parameter par = myview.GetParameters("View Template").First();
                par.Set(new RvtDB.ElementId(-1));


                trans.Commit();
            }
        }

        public RvtDB.View3D ViewFloor(RvtDB.Document doc, List<List<RvtDB.ElementId>> isolated)
        {
            RvtDB.ViewFamilyType viewFamilyType = (from v in new RvtDB.FilteredElementCollector(doc).
                                     OfClass(typeof(RvtDB.ViewFamilyType)).
                                     Cast<RvtDB.ViewFamilyType>()
                                                   where v.ViewFamily == RvtDB.ViewFamily.ThreeDimensional
                                                   select v).First();

            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Create view"))
            {
                trans.Start();
                RvtDB.View3D view = RvtDB.View3D.CreateIsometric(doc, viewFamilyType.Id);
                view.IsolateElementsTemporary(isolated[0]);
                view.IsolateElementsTemporary(isolated[1]);
                trans.Commit();
                return view;
            }
            
        }
        
        // MISSING description
        public void ViewFloorTrial(RvtDB.Document doc)
        {
            RvtUI.UIDocument uidoc = new RvtUI.UIDocument(doc);
            IList<RvtDB.Level> levels = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.Level)).Cast<RvtDB.Level>().OrderBy(l => l.Elevation).ToList();



            RvtDB.ViewFamilyType viewFamilyType = (from v in new RvtDB.FilteredElementCollector(doc).
                                     OfClass(typeof(RvtDB.ViewFamilyType)).
                                     Cast<RvtDB.ViewFamilyType>()
                                                   where v.ViewFamily == RvtDB.ViewFamily.ThreeDimensional
                                                   select v).First();

            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Create view"))
            {
                int ctr = 0;
                foreach (RvtDB.Level level in levels)
                {


                trans.Start();
                RvtDB.View3D view = RvtDB.View3D.CreateIsometric(doc, viewFamilyType.Id);

                    view.Name = level.Name + " Section Box";

                    trans.SetName("Create view " + view.Name);

                    // Create a new BoundingBox to define a 3D space 

                    RvtDB.BoundingBoxXYZ boundingBoxXYZ = new RvtDB.BoundingBoxXYZ();

                    boundingBoxXYZ.Min = new RvtDB.XYZ(-50, -100, level.Elevation);

                    double zOffset = 0;

                    if (levels.Count > ctr + 1)
                    {
                        zOffset = levels.ElementAt(ctr + 1).Elevation;

                    }
                    else
                    {
                        zOffset = level.Elevation + 10;                        
                    }
                    boundingBoxXYZ.Max = new RvtDB.XYZ(200, 125, zOffset);



                    trans.Commit();
                    uidoc.ActiveView = view;
                        ctr ++;
                }

            }
        }

        // Get a List (plot) with all the walls of the model
        public void GetListWalls(RvtDB.Document doc)
        {
            RvtDB.FilteredElementCollector walls = new RvtDB.FilteredElementCollector(doc);

            walls.OfClass(typeof(RvtDB.Wall));

            foreach (RvtDB.Wall wall in walls)
            {
                RvtDB.Parameter param = wall.get_Parameter(RvtDB.BuiltInParameter.HOST_AREA_COMPUTED);

                double a = ((null != param) && (RvtDB.StorageType.Double == param.StorageType)) ? param.AsDouble() : 0.0;

                string s = (null != param) ? param.AsValueString() : "null";

                RvtDB.LocationCurve lc = wall.Location as RvtDB.LocationCurve;

                RvtDB.XYZ p = lc.Curve.GetEndPoint(0);
                RvtDB.XYZ q = lc.Curve.GetEndPoint(1);

                double l = q.DistanceTo(p);

              

                RvtUI.TaskDialog.Show("Revit ", " Wall " + wall.Id.IntegerValue.ToString() + " " + wall.Name + " length " + Utilities.RealString(l) + " area " + Utilities.RealString(a) + " " + s);
       
            }
        }

        // MISSING
        public void GetWallDimension(RvtDB.Document doc)
        {

        }

        // MISSING
        public class ElementCollector
        {
            private ICollection<RvtDB.Element> modelElements;
  //          private RvtDB.Document doc;

            public ElementCollector (ICollection<RvtDB.Element> gatheredelement)
            {
                modelElements = gatheredelement;
               
            }
            public ElementCollector(ICollection<RvtDB.ElementId> gatheredelementId)
            {
                
                foreach (var id in gatheredelementId)
                {
                  //  modelElements.Add(doc.GetElement(id));
                    

                }

            }
            // Get elements Filtered elements based on their Category Name 
            public ICollection<RvtDB.Element> ElementFiltering(RvtDB.Document doc, RvtDB.BuiltInCategory category,bool isAnalytical= true)
            {
                // Find all <cat> instances in the document by using category filter
                RvtDB.ElementCategoryFilter filter = new RvtDB.ElementCategoryFilter(category);

                //Apply the filter to the elements in the active document
                RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc);

                ICollection<RvtDB.Element> filteredelements = collector.WherePasses(filter).WhereElementIsNotElementType().ToElements();
              
                foreach (RvtDB.Element ele in filteredelements)
                    modelElements.Add(ele);

                RvtUI.TaskDialog.Show(" Revit ", modelElements.Count + " have/has passed the element filtering");
                return modelElements;
            }
        }

         // Delete a collection of elements
         public void delete_elements (RvtDB.Document doc,ICollection<RvtDB.Element> set_ele)
        {
            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc))
            {
                if (trans.Start(" Delete ") == RvtDB.TransactionStatus.Started)
                {
                    foreach (RvtDB.Element el in set_ele)
                    {
                        Autodesk.Revit.DB.ElementId elementId = el.Id;
                        ICollection<Autodesk.Revit.DB.ElementId> deletedIdSet = doc.Delete(elementId);
                        if (deletedIdSet.Count == 0)
                        {
                            throw new Exception(" delete the element failed ");
                        }                                   
                    }
                    String prompt = "The selected elements have been removed ";
                    RvtUI.TaskDialog.Show("Revit", prompt);
                    trans.Commit();
                }
            }
           
        }

        // Hide a collection of elements - Under Construction
        public void hide(RvtDB.Document doc,ICollection<RvtDB.Element> set_ele)
        {

        }

        // Get a list of elements of a specific category of a specific level
        public ICollection<RvtDB.Element> GetListofElements_OfSpecificLevel(RvtDB.Document doc, string element_name)
        {
            RvtDB.FilteredElementCollector level_collector = new RvtDB.FilteredElementCollector(doc);
            ICollection<RvtDB.Element> levels = level_collector.OfClass(typeof(RvtDB.Level)).ToElements();
            var query = from element in level_collector where element.Name == element_name select element; // LINQ query

            List<RvtDB.Element> level = query.ToList<RvtDB.Element>();
            RvtDB.ElementId levelId = level[0].Id;
     

            //Find all walls on level one

            RvtDB.ElementLevelFilter level1Filter = new RvtDB.ElementLevelFilter(levelId);
            level_collector = new RvtDB.FilteredElementCollector(doc);
            ICollection<RvtDB.Element> allelementsonLevel = level_collector.OfClass(typeof(RvtDB.Wall)).WherePasses(level1Filter).ToElements();
            
            // Just plot the total number of elements returned back
            RvtUI.TaskDialog.Show(" Revit ", " Number of wall elemnts of the first floor " + allelementsonLevel.Count);

            return allelementsonLevel;
        }

        // Get the center of an element - UNDER CONSTRUCTION
        public RvtDB.XYZ GetElementCenter(RvtDB.Element elem)
        {
            RvtDB.BoundingBoxXYZ bounding = elem.get_BoundingBox(null);
            RvtDB.XYZ center = (bounding.Max + bounding.Min) * 0.5;
            return center;
        }

        // Filter a category based on the ID
        public void Filtering(RvtDB.BuiltInCategory cat, ICollection<RvtDB.ElementId> elemID)
        {
            // TODO check whether it is required to pass also the document

            // Find all <cat> instances in the document by using category filter
            RvtDB.ElementCategoryFilter filter = new RvtDB.ElementCategoryFilter(cat);

            //Apply the filter to the elements in the active document

            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(RevitDocument, elemID);
            IList<RvtDB.Element> filteredelements = collector.WherePasses(filter).WhereElementIsNotElementType().ToElements();
            ICollection<RvtDB.ElementId> filtereelementsIDs = collector.WherePasses(filter).WhereElementIsNotElementType().ToElementIds();
            RvtDB.FilteredElementIterator elemItr = collector.WherePasses(filter).WhereElementIsNotElementType().GetElementIterator();
            
            string filtername = cat.ToString().Remove(0, 4);
            String prompt = " The " + filtername + " in the current document are :\n ";
            foreach (RvtDB.Element ele in filteredelements)
            {
                prompt += ele.Category.Name + "\n";
                
            }
            foreach (RvtDB.ElementId eleid in filtereelementsIDs)
            {
                // not required
             //   prompt += eleid.IntegerValue + "\n";
            }
            //prompt += filtereelementsIDs.Count + "\n";
            RvtUI.TaskDialog.Show("Revit", prompt);

        }

        // To be deleted
        public void BoundingBoxIntersect (RvtDB.Outline myOutline)
        {
            // NOT YET IMPLEMENTED 
            RvtDB.BoundingBoxIntersectsFilter filter = new RvtDB.BoundingBoxIntersectsFilter(myOutline);
            
        }

        // Plot properties of an element
        public void PlotSpecificElementParameterInformation (RvtDB.Document doc, RvtDB.Element ele, string text)
        { }

        // Plot all the properties of an element
        public void PlotAllElementParameterInformation (RvtDB.Document doc, RvtDB.Element ele)
        {
            //String prompt = " Show parameters in selected Element :";

            StringBuilder st = new StringBuilder();
            // Iterate element's parameters
            foreach (RvtDB.Parameter para in ele.Parameters)
            {
                st.AppendLine(PlotAllParameterInformation(para, doc));
                /*if (para.Definition.Name == "Length" )
                {
                    RvtUI.TaskDialog.Show(" Revit ", " The length of the wall is " + para.AsValueString());
                }*/
            }

            RvtUI.TaskDialog.Show("Revit", st.ToString());
        }

        // MISSING
        String PlotAllParameterInformation(RvtDB.Parameter para, RvtDB.Document doc)
        {
            string defName = para.Definition.Name + "\t";

            // Use different method to get parameter data according to the storage type
            switch (para.StorageType)
            {
            
                case RvtDB.StorageType.Double:
                    //convert number to metric
                    defName += " : " + para.AsValueString();
                    break;
                case RvtDB.StorageType.ElementId:
                    //find out the name of the element
                    RvtDB.ElementId id = para.AsElementId();
                    if (id.IntegerValue >= 0)
                    {
                        defName += " : " + doc.GetElement(id).Name;
                    }
                    else
                    {
                        defName += " : " + id.IntegerValue.ToString();
                    }
                    break;
                case RvtDB.StorageType.Integer:
                    if (RvtDB.ParameterType.YesNo == para.Definition.ParameterType)
                    {
                        if (para.AsInteger() == 0)
                        {
                            defName += " : " + " False ";
                        }
                        else
                        {
                            defName += " : " + " True ";

                        }
                    }
                    else
                    {
                        defName += " : " + para.AsInteger().ToString();
                    }
                    break;
                case RvtDB.StorageType.String:
                    defName += " : " + para.AsString();
                    break;
                default:
                    defName = "Unexposed parameter.";
                    break;
            }
            return defName;
            


        }

        // On progress 
        public bool SetNewParameterToTypeWall(RvtUI.UIApplication app, RvtDB.DefinitionFile file)
        {
            // Create a new group in the shared parameters file
            RvtDB.DefinitionGroups myGroups = file.Groups;
            RvtDB.DefinitionGroup myGroup = myGroups.Create("Experimental Parameters ");

            // Create a type definition 
            RvtDB.ExternalDefinitionCreationOptions option = new RvtDB.ExternalDefinitionCreationOptions(" Info ", RvtDB.ParameterType.Text);
            RvtDB.Definition mydefinition_group = myGroup.Definitions.Create(option);

            // Create a category set and insert category of wall to it
            RvtDB.CategorySet myCategories = app.Application.Create.NewCategorySet();
            // Use BuiltinCategory to get category of wall
            RvtDB.Category myCategory = app.ActiveUIDocument.Document.Settings.Categories.get_Item(RvtDB.BuiltInCategory.OST_Walls);
            myCategories.Insert(myCategory);

            // Create an object of TypeBinding according to the Categories
            RvtDB.TypeBinding typeBinding = app.Application.Create.NewTypeBinding(myCategories);

            // Get the BindingMap of the current document

            RvtDB.BindingMap bindingMap = app.ActiveUIDocument.Document.ParameterBindings;

            // Bind the definitions to the document
            bool typeBindOK = bindingMap.Insert(mydefinition_group, typeBinding, RvtDB.BuiltInParameterGroup.PG_TEXT);
            return typeBindOK;
        

        }

        // On progress
        public static void CreateViewFilter (RvtDB.Document doc, ICollection<RvtDB.ElementId> eleid, RvtDB.View view)
        {
            List<RvtDB.FilterRule> filterRules = new List<RvtDB.FilterRule>();

            using (RvtDB.Transaction t = new RvtDB.Transaction(doc, " Add view filter ")) 
            {
                t.Start();

                // Create filter Element associated to the input categories
                RvtDB.ParameterFilterElement parameterFilterElement = RvtDB.ParameterFilterElement.Create(doc, " Exmpale view filter ", eleid);

            }

        }

        // hide elements  - UNDER CONSTRUCTION 
        public void hide_elemenets(RvtDB.Document document, RvtDB.Element ele)
        {
        }

        // Get level of an element  - UNDER CONSTRUCTION 
        public void Getinfo_Level(RvtDB.Document doc)
        {
            StringBuilder levelInformation = new StringBuilder();
            int levelNumber = 0;
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc);
            ICollection<RvtDB.Element> collection = collector.OfClass(typeof(RvtDB.Level)).ToElements();
            foreach (RvtDB.Element ele in collection)
            {
                RvtDB.Level level = ele as RvtDB.Level;

                if (null!= level)
                {
                    //keep track of the number of levels
                    levelNumber++;

                   // We can add more info 
                   // like the name and the elevation of the level etc
                   // (for that reason we used a stringBuilder
                }
            }
            // number of total levels in the current document
            levelInformation.Append(" There are " + levelNumber + " levels in the document ");
            // Show the level information to the user
            RvtUI.TaskDialog.Show(" Revit ", levelInformation.ToString());
        }

        // Retrive all planar faces belonging to te specified opening in the given wall -  UNDER CONSTRUCTION 
        static List<RvtDB.PlanarFace> GetWallOpeningPlanarfaces(RvtDB.Wall wall , RvtDB.ElementId openingId)
        {
            List<RvtDB.PlanarFace> facelist = new List<RvtDB.PlanarFace>();

            List<RvtDB.Solid> solidList = new List<RvtDB.Solid>();

            RvtDB.Options geoOptions = wall.Document.Application.Create.NewGeometryOptions();

            if (geoOptions != null)
            {
                RvtDB.GeometryElement geoElem = wall.get_Geometry(geoOptions);
                if (geoElem != null)
                {
                    foreach ( RvtDB.GeometryObject geomObj in geoElem)
                    {
                        if (geomObj is RvtDB.Solid)
                        {
                            solidList.Add(geomObj as RvtDB.Solid);

                        }
                    }
                }
            }

            foreach ( RvtDB.Solid solid in solidList)
            {
                foreach ( RvtDB.Face face in solid.Faces)
                {
                    if ( face is RvtDB.PlanarFace)
                    {
                        if ( wall.GetGeneratingElementIds(face).Any(x => x == openingId))
                        {
                            facelist.Add(face as RvtDB.PlanarFace);
                        }
                    }
                }
            }
            return facelist;
        }

        #endregion

        protected override RvtUI.Result subExecute(RvtUI.ExternalCommandData cmdData, ref String message, RvtDB.ElementSet element_set)
      {
            
            #region Tutorial Autodesk

            // 
            //      Tutorial Autodesk 
            //
            /*
            RvtDB.Document doc = RevitUIApplication.ActiveUIDocument.Document;
            Selection sel= RevitUIApplication.ActiveUIDocument.Selection;
            RvtDB.Reference pickedRef = null;
                 
            pickedRef = sel.PickObject(ObjectType.Element,"Please select a group");
            RvtDB.Element elem =RevitDocument.GetElement(pickedRef);
            RvtDB.Group group = elem as RvtDB.Group;

            RvtDB.XYZ point = sel.PickPoint(" Please pick a point");

            RvtDB.Transaction trans = new RvtDB.Transaction(doc);

            trans.Start("Lab");
            doc.Create.PlaceGroup(point, group.GroupType);
            trans.Commit();
            */
            #endregion
            #region Video Tutorial

            /*
            //
            //  Video Tutorial 
            //

            RvtDB.Document doc = RevitUIApplication.ActiveUIDocument.Document;

            // access the selection before the command begings
            Selection sel = RevitUIApplication.ActiveUIDocument.Selection;

            
            foreach (RvtDB.ElementId eleId in sel.GetElementIds())
            {
                RvtDB.Element ele = doc.GetElement(eleId);
                RvtUI.TaskDialog.Show(ele.Category.Name, ele.Name);
            }

            // iterate through the collection

            RvtDB.FilteredElementCollector boundary = new RvtDB.FilteredElementCollector(doc);
            boundary.OfCategory(RvtDB.BuiltInCategory.OST_StructuralFraming);
            boundary.OfClass(typeof(RvtDB.FamilyInstance));
            RvtUI.TaskDialog.Show("Number of boundary", boundary.GetElementCount().ToString());


            foreach ( RvtDB.Element ele in boundary)
            {
                //will contain the inforamtion from the paramters 
                System.Text.StringBuilder paramText = new System.Text.StringBuilder();
                
                foreach(RvtDB.Parameter param in ele.Parameters)
                {
                    //first lets get the parameter name
                    paramText.AppendFormat("{0}: ", param.Definition.Name);
                    //get the information
                    switch (param.StorageType)
                    {
                        case RvtDB.StorageType.String:
                            paramText.Append(param.AsString());
                            break;
                        case RvtDB.StorageType.Double:
                            paramText.AppendFormat("{0:0.00}", param.AsDouble());
                            break;

                    }
                    //add a breakline
                    paramText.AppendLine();
                }
                RvtUI.TaskDialog.Show(ele.Name, paramText.ToString());
            }
            */
            #endregion
            #region Knowlege Network Autodesk
            /*
            // 
            //      Autodesk Knowledge Network 
            // 
            try
            {
                // TODO I feel that i didnt get the diff between doc and uidoc
            RvtDB.Document doc = RevitUIApplication.ActiveUIDocument.Document;
                // Get the handle of current document
            RvtUI.UIDocument uidoc = RevitUIApplication.ActiveUIDocument;

            // Get the element selection of the current document
            Selection selection = uidoc.Selection;

            ICollection<RvtDB.ElementId> selectedIds = uidoc.Selection.GetElementIds();
            RvtUI.TaskDialog.Show("Revit ", "number of selected elements " + selectedIds.Count.ToString());
            ICollection<RvtDB.ElementId> selectedWallIds = new List<RvtDB.ElementId>();

            if (selectedIds.Count == 0)
                {
                    //If no elements selected
                    RvtUI.TaskDialog.Show(" Revit", " No selection ");
                }
            else
                {
                    String info = " Ids of selected elements in the document are : ";
                    foreach (RvtDB.ElementId id in selectedIds )
                    {
                        info += "\n\t" + id.IntegerValue;
                        RvtDB.Element elements = uidoc.Document.GetElement(id);
                        if (elements is RvtDB.Wall)
                        {
                            selectedWallIds.Add(id);
                        }
                    }

                    RvtUI.TaskDialog.Show("Revit ", info);
                    // Set the created element set as current select element set.
                    uidoc.Selection.SetElementIds(selectedWallIds);
                }

                if (0 != selectedWallIds.Count)
                {
                    RvtUI.TaskDialog.Show("Revit", selectedWallIds.Count.ToString() + " Walls are selected!");
                }
                else
                {
                    RvtUI.TaskDialog.Show("Revit", "No Walls have been selected!");
                }
            
            }
            catch (Exception e)
            {
                message = e.Message;
                return Autodesk.Revit.UI.Result.Failed;
            }

            */
            //
            //      Start - Filtering Testing
            //
            // Filtering(RvtDB.BuiltInCategory.OST_Walls);
            //Filtering(RvtDB.BuiltInCategory.OST_WallAnalytical);

            // 
            //      End - Filtering Testing
            // 
            #endregion
            #region Playground 
            

                                //                      //       
                                //      Playground      //
                                //                      //

           

            // TODO check if there is no model

            // Get active view 
            var view = RevitDocument.ActiveView;
    
            // 
            //      Example of Selection an element 
            //

            // TODO I feel that i didnt get the diff between doc and uidoc
            RvtDB.Document doc = RevitUIApplication.ActiveUIDocument.Document;
            // Get the handle of current document
            RvtUI.UIDocument uidoc = RevitUIApplication.ActiveUIDocument;

            // In case of selection before clicking on the button
            Selection selection = uidoc.Selection;
            IList<RvtDB.ElementId> selectedIds = uidoc.Selection.GetElementIds().ToList();
            ICollection<RvtDB.Element> selectedelements = null;

            if (selectedIds.Count == 0)
            {
                try
                {   // Start - Dr.Ing Niggl
                    IList<RvtDB.Reference> selection_IDs= uidoc.Selection.PickObjects(RvtUI.Selection.ObjectType.Element, "Select elements");
                    if (selection_IDs.Count > 0)
                    {
                        // IList<RvtDB.ElementId> idsToSelect = new List<RvtDB.ElementId>(selectedelements.Count);
                        selectedelements = new List<RvtDB.Element>(selection_IDs.Count);
                        foreach (RvtDB.Reference eID in selection_IDs)
                        {
                            selectedIds.Add(eID.ElementId);
                            selectedelements.Add(RevitDocument.GetElement(eID));
                        }
                    }
                    
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    selectedIds.Clear();
                    
                }
            }
            
            // In case of no selection 
            if (selectedIds.Count == 0) return RvtUI.Result.Cancelled;

            // Check if the walls are connected 
            if (WallsAreConnected(doc, selectedIds)) RvtUI.TaskDialog.Show("Revít", " Shear walls are connected");
            else
            {
                RvtUI.TaskDialog.Show("Revit ", "Shear walls are NOT connected ");
            }

            // Track the selected Walls
            var selectedWalls = new List<RvtDB.ElementId>();
            var selectedBeams = new List<RvtDB.ElementId>();

            foreach (RvtDB.ElementId id in selectedIds)
            {
                RvtDB.Element elements = uidoc.Document.GetElement(id);
                if (elements is RvtDB.Wall)
                {
                    selectedWalls.Add(id);
                }
            }

            if (0 != selectedWalls.Count)
            {
                RvtUI.TaskDialog.Show("Revit ", selectedWalls.Count.ToString() + " Walls are selected ");
                // Get the Element Parameter of the fist wall (only for testing) * we have access to all elements
                PlotAllElementParameterInformation(doc, RevitDocument.GetElement(selectedWalls.OfType<RvtDB.ElementId>().First()));
            }
            else
            {
                RvtUI.TaskDialog.Show("Revit ", "No walls have been selected ");
            }

            PlotSpecificElementParameterInformation(doc, RevitDocument.GetElement(selectedIds.OfType<RvtDB.ElementId>().First()),"Length");
            
            // Some samples plots
            RvtUI.TaskDialog.Show("Plot of experimental", " You have selected in total " + selectedIds.Count.ToString()+ " elements ");
            Filtering(RvtDB.BuiltInCategory.OST_BoundaryConditions, selectedIds);

            // Get the total levels of the document
            // DOES NOT WORK -> TODO 

            //Getinfo_Level(doc);

            // DOES NOT WORK -> TODO 10/05

            // Get the element center of the first wall
            RvtDB.BoundingBoxXYZ bounding = RevitDocument.GetElement(selectedIds.OfType<RvtDB.ElementId>().First()).get_BoundingBox(null);
            RvtDB.Element ele = RevitDocument.GetElement(selectedIds.OfType<RvtDB.ElementId>().First());
            //   RvtDB.XYZ origin = GetElementCenter(RevitDocument.GetElement(selectedIds.OfType<RvtDB.ElementId>().First()));
            RvtDB.Structure.AnalyticalModel columnModel = ele.GetAnalyticalModel();
            /*  if (columnModel!= null)
              {
                  RvtDB.XYZ startPoint = columnModel.GetCurve().GetEndPoint(0);
                  RvtDB.XYZ endPoint = columnModel.GetCurve().GetEndPoint(1);
                  RvtUI.TaskDialog.Show("Revit ", " start " + startPoint + " and end " + endPoint);
                  RvtUI.TaskDialog.Show("Revit ", " the length is "+ columnModel.GetCurve().Length);
              }

      */

            //    RvtUI.TaskDialog.Show("Revit ", " Max "+  bounding.Max + " and Min " + bounding.Min);
            #endregion

            #region Planar faces belonging to a specific opening
            //                                                                                      //
            //    Retrive all planar faces belonging to te specified opening in the given wall      //
            //                                                                                      //

            RvtDB.Categories cats = doc.Settings.Categories;
            RvtDB.ElementId catDoorsId = cats.get_Item(RvtDB.BuiltInCategory.OST_Doors).Id;

            List<RvtDB.ElementId> newIds = new List<RvtDB.ElementId>();

            foreach ( RvtDB.ElementId selectedId in selectedIds)
                {
                    RvtDB.Wall wall = doc.GetElement(selectedId) as RvtDB.Wall;

                    if (wall != null)
                    {
                        List<RvtDB.PlanarFace> faceList = new List<RvtDB.PlanarFace>();

                        List<RvtDB.ElementId> insertIds = wall.FindInserts(true, false, false, false).ToList();

                        foreach( RvtDB.ElementId insertId in insertIds)
                        {
                            RvtDB.Element elem = doc.GetElement(insertId);

                            if ( elem is RvtDB.FamilyInstance)
                            {
                                RvtDB.FamilyInstance inst = elem as RvtDB.FamilyInstance;

                                RvtDB.CategoryType catType = inst.Category.CategoryType;

                                RvtDB.Category cat = inst.Category;

                                if (catType == RvtDB.CategoryType.Model && (cat.Id == catDoorsId))
                                {
                                    faceList.AddRange(GetWallOpeningPlanarfaces(wall, insertId));
                                }


                            }
                            else if (elem is RvtDB.Opening)
                            {
                                faceList.AddRange(GetWallOpeningPlanarfaces(wall, insertId));
                            }
                        }


                    }
                }
            #endregion

            #region trash
            //                                  //
            //   Get all elements of level 1    //
            //                                  //

            //  ICollection<RvtDB.Element> elementtry = GetListofElements_OfSpecificLevel(doc, "Ebene 2");

            //                                      //
            //  Delete all the elements of level 1  // 
            //                                      //

            //    delete_elements(doc, elementtry);

            //                              //
            //   Trial of get All elements  // 
            //                              //
            /*
                        var all_elementIDs = DBUtils.Filtering.getAllElementsIncludingLinkedProjects(doc, view);
                        HashSet<string> analyticalmodelnames = new HashSet<string>();

                        ICollection<RvtDB.Element> mywalls = new List<RvtDB.Element>();
                        ICollection<RvtDB.Element> myanalyticalwalls = new List<RvtDB.Element>();
                        RvtUI.TaskDialog.Show("Revit", " All elements " + all_elementIDs.Count);
                        RvtDB.ElementSet el = new RvtDB.ElementSet();
                        foreach (var id in all_elementIDs)
                        {
                            RvtDB.Element myel = DBUtils.DocumentHelper.getElement(doc, id);

                            if (myel is RvtDB.Structure.AnalyticalModel)
                            {
                                analyticalmodelnames.Add(myel.Category.Name.ToString());
                                /*
                                if (myel.Category.Name == "Analytical Walls")
                                {
                                    myanalyticalwalls.Add(myel);
                                }
                                else if(myel.Category.Name == "Analytical")
                                {
                                    myanalyticalwalls.Add(myel);
                                }

                            }
                            //    el.Insert(DBUtils.DocumentHelper.getElement(doc, id));


                        }
                        RvtUI.TaskDialog.Show("Revit", " All wall elements " + string.Join(",",analyticalmodelnames));
                        RvtUI.TaskDialog.Show("Revit", " All wall elements " + mywalls.Count);
                        RvtUI.TaskDialog.Show("Revit", " All analytical wall elements " + myanalyticalwalls.Count);

                */
            #endregion
          

            #region            // Code for obtainining walls out of selection 
            /*
            foreach (RvtDB.ElementId id in selectedIds)
            {
                RvtDB.Element elementFromId = doc.GetElement(id);
                if (elementFromId == null)
                {
                    RvtUI.TaskDialog.Show("Revit ", " Hey we have problem -> No element has been selected");
                }

                // Obtained by ExportableFactory
                bool analyticalModelEnabled = true;
                if (DBUtils.Parameters.readParameter(elementFromId, RvtDB.BuiltInParameter.STRUCTURAL_ANALYTICAL_MODEL, out analyticalModelEnabled))
                {
                    if (!analyticalModelEnabled)
                    {
                        RvtUI.TaskDialog.Show("Revit ", " Analytical model of element with ID " + elementFromId + " is disabled");
                        continue;
                    }
                }
                RvtUI.TaskDialog.Show("Revit ", " Analytical model of element with ID " + id + " is SELECTED");
                if (elementFromId.GetType() == typeof(RvtDB.Wall))
                {
                    shearwallsId.Add(id);
                }
            }
            */
            #endregion

            /*
            SimplifiedModuleManager mysimplifiedmodulemanager = new SimplifiedModuleManager(uidoc);

            // Elegant way to filter all the required elements for a simplified model
            var my_simplified_nodes = mysimplifiedmodulemanager.mybuilding_graph.FindNodes(n => { return n.MemberType != BuildingGraph.Node.MemberTypeEnum.Undefined; });

            // add analytical model of the required elements
            List<RvtDB.ElementId> my_simplified_nodes_id = new List<RvtDB.ElementId>();
            foreach (var nd in my_simplified_nodes)
            {
                my_simplified_nodes_id.Add(doc.GetElement(nd.Member).GetAnalyticalModelId());
            }

            // add the boundary condition (as it is not included in the building graph)
            var Boundary_Id = new RvtDB.FilteredElementCollector(doc).OfCategory(RvtDB.BuiltInCategory.OST_BoundaryConditions).ToElementIds().ToList();
            my_simplified_nodes_id.AddRange(Boundary_Id);

            // Isolate only the required elements for a simplified model
            mysimplifiedmodulemanager.HideFromView(my_simplified_nodes_id);

            */

            #region replace shear wall

            var building_manager = BuildingManager.GetInitializedInstance(doc);
            var building_graph = building_manager.BuildingGraph;

            List<BuildingGraph.Node> mynodes = building_graph.FindNodes(n => { return n.MemberType == BuildingGraph.Node.MemberTypeEnum.Wall; });

            // 3. Get properties of that element
            #region acccess nodes via relations 
            // It is not exactly what we want as it ignores the inner relation 
            // just keep it for potential future use
            /*
            int counter = 0;

            foreach ( BuildingGraph.Relation r in relations)
            {
                RvtDB.Element startelement = doc.GetElement(r.Source.Member);
                RvtDB.Element endelement = doc.GetElement(r.Target.Member);
                if (startelement.Id.IntegerValue == 1775100 || endelement.Id.IntegerValue == 1775100)
                {
                    if (endelement.ToString() != "Autodesk.Revit.DB.Floor")
                    {
                        GetWallOpening(doc, endelement);
                        counter++;
                    }
                    if (startelement.ToString() != "Autodesk.Revit.DB.Floor")
                    {
                        //  GetWallOpening(doc, startelement);
                        //   counter++;
                    }
                }
            }
            */
            #endregion

            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "ViewDuplicate"))
            {
            trans.Start();
            List<List<BuildingGraph.Relation>> wallslab_relation = new List<List<BuildingGraph.Relation>>();
            int i = 0;

            // Important for version 1
            List<RvtDB.XYZ> mywallCoordinates_v2 = new List<RvtDB.XYZ>();
            List<RvtDB.XYZ> mywall = new List<RvtDB.XYZ>();
            List<RvtDB.XYZ> mywallCoordinates = new List<RvtDB.XYZ>();
                

            // TEMP for Dr. Niggl proposal
            List<BuildingGraph.Connector> wallslab_connector = null;
                
            foreach (BuildingGraph.Node n in mynodes)
            {
            // My final goal is to find the connection to the slab in order to obtain the height of the wall 
            // I keep the relation with SLAB when the direction is UP

            wallslab_relation.Add(n.FindRelations(relation => { return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Slab && relation.Direction == BuildingGraph.Relation.DirectionEnum.Up; }));

            //////                                                      ///////////
            ////// I WILL HELP YOU FINDING THE RIGHT HEIGHT OF THE WALL ///////////
            ////// version 1                                            ///////////
            #region version 1 

                /*
                    RvtDB.Element strElem = null;
                                    // A. Get the element from the <Member> property of each node (=element)

                                    strElem = doc.GetElement(n.Member);

                                    // B. Get a list of the coordinates of the previous elements

                                    mywallCoordinates = GetWallCoordinates(doc, strElem); // I do have access through the bounding box CHECH if it is better

                                    // C. Split the element into the middle and return a list of coordinates

                                    mywallCoordinates = Splitmyelement(doc, mywallCoordinates);

                                    // Elevate the element to the right height

                                    if (wallslab_relation[i].Count > 1)  // IMPORTANT *** that means that the wall is not 
                                    {
                                        Creator(doc, elevation(doc, mywallCoordinates[1], wallslab_relation[i][0].Target.BoundingBox.Max), elevation(doc, mywallCoordinates[0], wallslab_relation[i][0].Target.BoundingBox.Max));
                                        Creator(doc, elevation(doc, mywallCoordinates[3], wallslab_relation[i][0].Target.BoundingBox.Max), elevation(doc, mywallCoordinates[2], wallslab_relation[i][0].Target.BoundingBox.Max));

                                    }
                                    else
                                    {
                                        RvtUI.TaskDialog.Show("Problem ", "There is a problem with the wall-slab connection for the wallid " + wallslab_relation[i][0].Source.Member.ToString());
                                    }

                */
                //////                   END OF MY HELP                     ///////////
                #endregion

            // Start of Proposal by Dr. Niggl - Access through Connector 
            wallslab_connector = wallslab_relation[i][0].Connectors;

            foreach (BuildingGraph.Connector con in wallslab_connector) // This is in case that I have an opening at the wall
            { 
                if (con is BuildingGraph.ConnectorPoint) // either point connection
                {
                    RvtUI.TaskDialog.Show("Problem ", "Point connection " );

                }
                else if (con is BuildingGraph.ConnectorLinear) // or linear connection 
                { 
                    var p = con as BuildingGraph.ConnectorLinear;
                    var gep = p.Geometry as RvtDB.Line;
                    mywallCoordinates_v2 = GetWallCoordinates(doc, gep.Origin, gep.Length, gep.Direction); // I get the start and the end XYZ of my wall
                    mywallCoordinates_v2 = Splitmyelement(doc, mywallCoordinates_v2); // I split my wall into 2 elements and I get the start_1 end_1 start_2 end_2 of my wall 
                    CreateLine(doc, mywallCoordinates_v2[1], mywallCoordinates_v2[0]); // I create the first element (beam)
                    CreateLine(doc, mywallCoordinates_v2[3], mywallCoordinates_v2[2]); // I create the second element (beam)
                }
            }

            // End of Proposal

            // Increment
            i++; // CHECK : Is there an alternative
            }
            trans.Commit();
        }
            //                              //
            // 4. Hide elements of wall     //
            //   NOT SURE IF NEEDED         //

            /*
            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Hide elements"))
            {
                trans.Start();
                uidoc.ActiveView.HideElements(elemId);
                trans.Commit();
            }
            */

            //    uidoc.ActiveView  = HideElements(doc, view, elemId);



            // 5. Create a new element
            //   foreach (var n in relation.


            #endregion


            #region STEPS for finding CM and CR
            // Trial of find the Center of rigidity and create a node there

            //                              STEPS                                   //
            //                                                                      //
            // 1. Find all Slabs                                                    //
            // 1a. Calculate the center of Mass                                     //
            // 1b. Create a marker in the center of Mass                            //
            // (2. Find all elements(columns and walls) that belong to each Slab)   //
            // (3. Calculate the I for everyfloor)                                  //
            // (4. Calculate the distance of each wall from the center of origin)   //
            // 5. Calculate the XYZ (point) of C.R                                  //
            // 6. Create a node at the position of step.5                           //
            #endregion

            /*

            // 1. Find all Slabs - Access all the slabs through the building manager     
            List<BuildingGraph.Node> myslabs = mysimplifiedmodulemanager.mybuilding_graph.FindNodes(n => { return n.MemberType == BuildingGraph.Node.MemberTypeEnum.Slab; });

            // iterate through all the slabs -> through all the 
            foreach ( BuildingGraph.Node n in myslabs)
            {
  
                // 1a. Find the center of Mass
                RvtDB.XYZ mid = mysimplifiedmodulemanager.GetCenterofMass(n);

                // 1b.Create a marker in the center of Mass
                mysimplifiedmodulemanager.CreateMarker(new RvtDB.XYZ(mid.X, mid.Y, n.BoundingBox.Center.Z));

                // 5. Find the center of Rigidity
                RvtDB.XYZ CR = mysimplifiedmodulemanager.GetCenterofRigidity(n);

                // 6. Create a node at the position of step.5 
                mysimplifiedmodulemanager.CreateMarker(CR);
                    
            }
            */

            // TEST

            SimplifiedManager mymanager = new SimplifiedManager(uidoc);

            SimplifiedModel mymodel = mymanager.SimplifiedModel;

            ViewManager myviewmanager = new ViewManager();

            myviewmanager.HideFromView(uidoc, mymodel);




            return RvtUI.Result.Succeeded;
        }
       
    }
}


