using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;

using System.Threading.Tasks;
using Autodesk.Revit.DB;
using SOFiSTiK.Analysis;

namespace SOFiSTiK.Analysis
{
    public class Floor
    {
    #region private properties
        // private BuildingGraph.Node myslabs;
        private RvtDB.Document my_doc = null;
        public List<BuildingGraph.Node> my_slab_nodes { get; }
        private List<BuildingGraph.Node> strElements = null;
        public double height { get; }
        private RvtDB.Options my_option = null;
        private Marker CenterMass = null;
        private Marker CenterRigidity = null;
#endregion

    #region internal methods
            private void Triangulate()
            {
                RvtDB.XYZ max = new RvtDB.XYZ();
                RvtDB.XYZ min = new RvtDB.XYZ();

                double Area = 0;
                double CMx = 0;
                double CMy = 0;

                double Sum_Area = 0;
                double Sum_CMx = 0;
                double Sum_CMy = 0;


                List<RvtDB.MeshTriangle> mesh_triangle = new List<RvtDB.MeshTriangle>();
                // Find CM for every Node in the Slab
                foreach (BuildingGraph.Node my_floor_node in my_slab_nodes)
                {
                    RvtDB.GeometryElement gm = my_doc.GetElement(my_floor_node.Member).get_Geometry(my_option);
                    RvtDB.Solid so = gm.First() as RvtDB.Solid;
                    RvtDB.PlanarFace fc = so.Faces.get_Item(0) as RvtDB.PlanarFace;

                    // Keep only the face pointint to z direction 
                    foreach (RvtDB.PlanarFace f in so.Faces) { if (f.FaceNormal == new RvtDB.XYZ(0, 0, -1)) fc = f; }

                    RvtDB.Mesh mesh = fc.Triangulate();
                    for (int i = 0; i < mesh.NumTriangles; i++) { mesh_triangle.Add(mesh.get_Triangle(i)); }

                }

                //A = x1y2 + x2y3 + x3y1 – x1y3 – x2y1 – x3y2
                foreach (RvtDB.MeshTriangle triangle in mesh_triangle)
                {
                    RvtDB.XYZ vertex1 = triangle.get_Vertex(0);
                    RvtDB.XYZ vertex2 = triangle.get_Vertex(1);
                    RvtDB.XYZ vertex3 = triangle.get_Vertex(2);

                    CMx = (vertex1.X + vertex2.X + vertex3.X) / 3;
                    CMy = (vertex1.Y + vertex2.Y + vertex3.Y) / 3;

                    Area = vertex1.X * vertex2.Y + vertex2.X * vertex3.Y + vertex3.X * vertex1.Y - vertex1.X * vertex3.Y - vertex2.X * vertex1.Y - vertex3.X * vertex2.Y;
                    Sum_Area += Area;
                    Sum_CMx += CMx * Area;
                    Sum_CMy += CMy * Area;
                }
                Sum_CMx = Sum_CMx / Sum_Area;
                Sum_CMy = Sum_CMy / Sum_Area;
                // Attention : Ft to meter is not required
                CenterMass = new Marker(new RvtDB.XYZ(Sum_CMx, Sum_CMy, height));
            }
            #endregion

    #region external methods
        public Floor(RvtDB.Document doc, List<BuildingGraph.Node> floorNodes)
        {
            my_doc = doc;
            my_slab_nodes = floorNodes;
            my_option = new RvtDB.Options();
            // Take into account that we can have a point-connection or a line-connection 
            if (floorNodes[0].AdjacentRelations[0].Connectors[0] is BuildingGraph.ConnectorPoint)
            {
                var p = floorNodes[0].AdjacentRelations[0].Connectors[0] as BuildingGraph.ConnectorPoint;// we take only the first connector because every slab has at least one connector
                height = p.Geometry.Coord.Z;
            }
            else
            {
                var p = floorNodes[0].AdjacentRelations[0].Connectors[0] as BuildingGraph.ConnectorLinear;
                height = (p.Geometry as RvtDB.Line).Origin.Z;
            }

            strElements = new List<BuildingGraph.Node>();
            foreach (BuildingGraph.Node floor_node in my_slab_nodes)
            {
                List<BuildingGraph.Relation> up_relations = floor_node.FindRelations(relation => {
                    return (relation.Direction == BuildingGraph.Relation.DirectionEnum.Down
                         || relation.Direction == BuildingGraph.Relation.DirectionEnum.Horizontal)
                           && relation.Target.MemberType != BuildingGraph.Node.MemberTypeEnum.Slab;
                });

                foreach (BuildingGraph.Relation rel in up_relations)
                {
                    strElements.Add(rel.Target);
                }

            }

        }

        public List<RvtDB.ElementId> get_elementIDs_of_floors()
        {
            List<RvtDB.ElementId> myelementId = new List<RvtDB.ElementId>();

            // add the ID of the structural elements (except the slab)
            foreach (BuildingGraph.Node n in strElements)
            {
                myelementId.Add(my_doc.GetElement(n.Member).GetAnalyticalModelId());
            }

            // add the ID of the slabs
            foreach (BuildingGraph.Node floor_nodes in my_slab_nodes)
            {
                myelementId.Add(my_doc.GetElement(floor_nodes.Member).GetAnalyticalModelId());
            }


            return myelementId;
        }

        public Marker get_CenterMass() { return CenterMass; }
        public Marker get_CenterRigidity() {; return CenterRigidity; }

        public void calculate_CenterRigidity()
        {
            List<BuildingGraph.Relation> slab_wallcolunm_relation = new List<BuildingGraph.Relation>();

            IEnumerable<BuildingGraph.Node> walls = strElements.Where(x => x.MemberType == BuildingGraph.Node.MemberTypeEnum.Wall);

            foreach (BuildingGraph.Node wall in walls)
            {
                slab_wallcolunm_relation.AddRange(wall.FindRelations(relation =>
                {
                    return (relation.Target.MemberType) == BuildingGraph.Node.MemberTypeEnum.Slab && relation.Direction == BuildingGraph.Relation.DirectionEnum.Up;
                                                      
                }));
            }
            // Initialize the stiffness in each direction and (stiffness X distance) 
            double SIyy = new double();
            double SIxx = new double();
            double SIyy_y = new double();
            double SIxx_x = new double();


            // Eccentricy in x and y direction 
            double e_yy = new double();
            double e_xx = new double();

            foreach (BuildingGraph.Relation rel in slab_wallcolunm_relation)
            {
                double wall_length = 0;
                double thickness = 0;
                if (rel.Source.StructuralMember is BuildingGraph.StructuralAreaMember)
                {
                    var a = rel.Source.StructuralMember as BuildingGraph.StructuralAreaMember;
                    thickness = a.ThicknessConstant;
                }
                else
                {
                    RvtUI.TaskDialog.Show("Revit Simplified Model", "Could not obtain the wall thickness");
                }

                foreach (BuildingGraph.Connector lin in rel.Connectors)
                {
                    
                    if (lin is BuildingGraph.ConnectorLinear) // if the connector is linear that means that ...
                    { 
                        var p = lin as BuildingGraph.ConnectorLinear;// we take only the first connector because ...
                        var gep = p.Geometry as RvtDB.Line;

                        wall_length += gep.ApproximateLength;

                        RvtDB.XYZ EndPoint = GetEndPoint(gep.Origin, gep.Length, gep.Direction);
                        //  double mythickness = 0.3048*  //* GetWallThickness(rel.Target.BoundingBox.Min, rel.Target.BoundingBox.Max, Math.Abs(gep.Direction.X) == 1);// Get the thickness of each wall through the bounding box information
                        //  double mywidth = 0.3048;//* GetWallWidth(rel.Target.BoundingBox.Min, rel.Target.BoundingBox.Max, Math.Abs(gep.Direction.X) == 1);// Get the width of each wall through the bounding box infromation


                        // 3. Calculate the stiffness of each wall

                        var midpoint = new RvtDB.XYZ((gep.Origin.X + EndPoint.X) / 2, (gep.Origin.Y + EndPoint.Y) / 2, (gep.Origin.Z + EndPoint.Z) / 2);
                        double Ixx = thickness * Math.Pow(wall_length, 3) * Math.Abs(gep.Direction.Y) / 12;
                        double Iyy = thickness * Math.Pow(wall_length, 3) * Math.Abs(gep.Direction.X) / 12;
                        // 4. Calculate the distance of each wall from the center of origin (arbitrary)
                        SIxx += Ixx;
                        SIxx_x += Ixx * midpoint.X;



                        // 4. Calculate the distance of each wall from the center of origin (arbitrary)
                        SIyy += Iyy;
                        SIyy_y += Iyy * midpoint.Y;
                    }
                }
               
            }
            e_yy = SIyy_y / SIyy ;
            e_xx = SIxx_x / SIxx ;
            CenterRigidity = new Marker(new RvtDB.XYZ(e_xx, e_yy, height));
        }
        public void calculate_CenterMass() { Triangulate(); }// TODO : shall I rename the " Triangulate" it? Triangulate is not clear from outside to be called 

        public RvtDB.XYZ GetEndPoint(RvtDB.XYZ startpoint, double length, RvtDB.XYZ direction)
        {
            return new RvtDB.XYZ(startpoint.X + direction.X * length, startpoint.Y + direction.Y * length, startpoint.Z + direction.Z * length);
        }

        public void Add_Floor(BuildingGraph.Node new_slab)
        {
            // Add the slab to the floor TODO : It is not correct as we assume that we have filtered the floors with a descending/ascending row
            my_slab_nodes.Add(new_slab);

            // Add also the corresponding structural elements
            List<BuildingGraph.Relation> up_relations = new_slab.FindRelations(relation => { return (relation.Direction == BuildingGraph.Relation.DirectionEnum.Down
                                                                                                || relation.Direction == BuildingGraph.Relation.DirectionEnum.Horizontal)
                                                                                                && relation.Target.MemberType != BuildingGraph.Node.MemberTypeEnum.Slab ; });
            foreach (BuildingGraph.Node nd in my_slab_nodes)
            {
                foreach (BuildingGraph.Relation rel in up_relations)
                {
                    bool myflag = nd.Relations.Any(k => k.Target.Member == rel.Target.Member);
                    if (!myflag) { strElements.Add(rel.Target); }
                }
            }
        }
#endregion

    }
}
