using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using SOFiSTiK.Analysis;

namespace SOFiSTiK.Experimental
{
    class CreatorJT
    {
        Document _doc;

        Autodesk.Revit.Creation.Application _creapp;
        Autodesk.Revit.Creation.Document _credoc;

        public CreatorJT(Document doc)
        {
            _doc = doc;
            _credoc = doc.Create;
            _creapp = doc.Application.Create;
        }

        XYZ GetCurveNormal (Curve curve)
        {
            IList<XYZ> pts = curve.Tessellate();
            int n = pts.Count;

         

            XYZ p = pts[0];
            XYZ q = pts[n - 1];
            XYZ v = q - p;
            XYZ w, normal = null;

            if (2 == n)
            {
               

                // For non-vertical lines, use Z axis to
                // span the plane, otherwise Y axis:

                double dxy = Math.Abs(v.X) + Math.Abs(v.Y);

                w = XYZ.BasisZ;
                normal = v.CrossProduct(w).Normalize();
            }
            else
            {
                int i = 0;
                while (++i < n - 1)
                {
                    w = pts[i] - p;
                    normal = v.CrossProduct(w);
                    if (!normal.IsZeroLength())
                    {
                        normal = normal.Normalize();
                        break;
                    }
                }

#if DEBUG
        {
          XYZ normal2;
          while( ++i < n - 1 )
          {
            w = pts[i] - p;
            normal2 = v.CrossProduct( w );
            Debug.Assert( normal2.IsZeroLength()
              || Util.IsZero( normal2.AngleTo( normal ) ),
              "expected all points of curve to "
              + "lie in same plane" );
          }
        }
#endif // DEBUG

            }
            return normal;
        }

        /// <summary>
        /// Create a model line between the two given points.
        /// Internally, it creates an arbitrary sketch
        /// plane given the model line end points.
        /// </summary>
        public static ModelLine CreateModelLine(
          Document doc,
          XYZ p,
          XYZ q)
        {
            if (p.DistanceTo(q) < Utilities.MinimumLineLength) return null;

            // Create sketch plane; for non-vertical lines,
            // use Z-axis to span the plane, otherwise Y-axis:

            XYZ v = q - p;

            double dxy = Math.Abs(v.X) + Math.Abs(v.Y);

            XYZ w = XYZ.BasisZ;

            XYZ norm = v.CrossProduct(w).Normalize();

            //Autodesk.Revit.Creation.Application creApp
            //  = doc.Application.Create;

            //Plane plane = creApp.NewPlane( norm, p ); // 2014
            //Plane plane = new Plane( norm, p ); // 2015, 2016
            Plane plane = Plane.CreateByNormalAndOrigin(norm, p); // 2017

            //SketchPlane sketchPlane = creDoc.NewSketchPlane( plane ); // 2013
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane); // 2014

            //Line line = creApp.NewLine( p, q, true ); // 2013
            Line line = Line.CreateBound(p, q); // 2014

            // The following line is only valid in a project 
            // document. In a family, it will throw an exception 
            // saying "Document.Create can only be used with 
            // project documents. Use Document.FamilyCreate 
            // in the Family Editor."

            //Autodesk.Revit.Creation.Document creDoc
            //  = doc.Create;

            //return creDoc.NewModelCurve(
            //  //creApp.NewLine( p, q, true ), // 2013
            //  Line.CreateBound( p, q ), // 2014
            //  sketchPlane ) as ModelLine;

            ModelCurve curve = doc.IsFamilyDocument
              ? doc.FamilyCreate.NewModelCurve(line, sketchPlane)
              : doc.Create.NewModelCurve(line, sketchPlane);

            return curve as ModelLine;
        }

        SketchPlane NewSketchPlanePassLine(
          Line line)
        {
            XYZ p = line.GetEndPoint(0);
            XYZ q = line.GetEndPoint(1);
            XYZ norm;
            if (p.X == q.X)
            {
                norm = XYZ.BasisX;
            }
            else if (p.Y == q.Y)
            {
                norm = XYZ.BasisY;
            }
            else
            {
                norm = XYZ.BasisZ;
            }
            //Plane plane = _creapp.NewPlane( norm, p ); // 2016
            Plane plane = Plane.CreateByNormalAndOrigin(norm, p); // 2017

            //return _credoc.NewSketchPlane( plane ); // 2013

            return SketchPlane.Create(_doc, plane); // 2014
        }

        //public void CreateModelLine( XYZ p, XYZ q )
        //{
        //  if( p.IsAlmostEqualTo( q ) )
        //  {
        //    throw new ArgumentException(
        //      "Expected two different points." );
        //  }
        //  Line line = Line.CreateBound( p, q );
        //  if( null == line )
        //  {
        //    throw new Exception(
        //      "Geometry line creation failed." );
        //  }
        //  _credoc.NewModelCurve( line,
        //    NewSketchPlanePassLine( line ) );
        //}

        /// <summary>
        /// Return a new sketch plane containing the given curve.
        /// Update, later: please note that the Revit API provides
        /// an overload of the NewPlane method taking a CurveArray
        /// argument, which could presumably be used instead.
        /// </summary>
        SketchPlane NewSketchPlaneContainCurve(
          Curve curve)
        {
            XYZ p = curve.GetEndPoint(0);
            XYZ normal = GetCurveNormal(curve);

            //Plane plane = _creapp.NewPlane( normal, p ); // 2016
            Plane plane = Plane.CreateByNormalAndOrigin(normal, p); // 2017

#if DEBUG
      if( !( curve is Line ) )
      {
        //CurveArray a = _creapp.NewCurveArray();
        //a.Append( curve );
        //Plane plane2 = _creapp.NewPlane( a ); // 2016

        List<Curve> a = new List<Curve>( 1 );
        a.Add( curve );
        CurveLoop b = CurveLoop.Create( a );
        Plane plane2 = b.GetPlane(); // 2017


        Debug.Assert( Util.IsParallel( plane2.Normal,
          plane.Normal ), "expected equal planes" );

        Debug.Assert( Util.IsZero( plane2.SignedDistanceTo(
          plane.Origin ) ), "expected equal planes" );
      }
#endif // DEBUG

            //return _credoc.NewSketchPlane( plane ); // 2013

            return SketchPlane.Create(_doc, plane); // 2014
        }

        public ModelCurve CreateModelCurve(Curve curve)
        {
            return _credoc.NewModelCurve(curve,
              NewSketchPlaneContainCurve(curve));
        }

        ModelCurve CreateModelCurve(
          Curve curve,
          XYZ origin,
          XYZ normal)
        {
            //Plane plane = _creapp.NewPlane( normal, origin ); // 2016
            Plane plane = Plane.CreateByNormalAndOrigin(
              normal, origin); // 2017

            SketchPlane sketchPlane = SketchPlane.Create(
              _doc, plane);

            return _credoc.NewModelCurve(
              curve, sketchPlane);
        }

        public ModelCurveArray CreateModelCurves(
          Curve curve)
        {
            var array = new ModelCurveArray();

            var line = curve as Line;
            if (line != null)
            {
                array.Append(CreateModelLine(_doc,
                  curve.GetEndPoint(0),
                  curve.GetEndPoint(1)));

                return array;
            }

            var arc = curve as Arc;
            if (arc != null)
            {
                var origin = arc.Center;
                var normal = arc.Normal;

                array.Append(CreateModelCurve(
                  arc, origin, normal));

                return array;
            }

            var ellipse = curve as Ellipse;
            if (ellipse != null)
            {
                var origin = ellipse.Center;
                var normal = ellipse.Normal;

                array.Append(CreateModelCurve(
                  ellipse, origin, normal));

                return array;
            }

            var points = curve.Tessellate();
            var p = points.First();

            foreach (var q in points.Skip(1))
            {
                array.Append(CreateModelLine(_doc, p, q));
                p = q;
            }

            return array;
        }

        public void DrawPolygon(
          List<XYZ> loop)
        {
            XYZ p1 = XYZ.Zero;
            XYZ q = XYZ.Zero;
            bool first = true;
            foreach (XYZ p in loop)
            {
                if (first)
                {
                    p1 = p;
                    first = false;
                }
                else
                {
                    CreateModelLine(_doc, p, q);
                }
                q = p;
            }
            CreateModelLine(_doc, q, p1);
        }

        public void DrawPolygons(
          List<List<XYZ>> loops)
        {
            foreach (List<XYZ> loop in loops)
            {
                DrawPolygon(loop);
            }
        }

        public void DrawFaceTriangleNormals(Face f)
        {
            Mesh mesh = f.Triangulate();
            int n = mesh.NumTriangles;

         

           

            for (int i = 0; i < n; ++i)
            {
                MeshTriangle t = mesh.get_Triangle(i);

                XYZ p = (t.get_Vertex(0)
                  + t.get_Vertex(1)
                  + t.get_Vertex(2)) / 3;

                XYZ v = t.get_Vertex(1)
                  - t.get_Vertex(0);

                XYZ w = t.get_Vertex(2)
                  - t.get_Vertex(0);

                XYZ normal = v.CrossProduct(w).Normalize();

              

                CreateModelLine(_doc, p, p + normal);
            }
        }
    }
}
