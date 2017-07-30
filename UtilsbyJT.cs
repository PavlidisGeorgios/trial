using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using System.Text;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SOFiSTiK.Analysis
{
    // Here all the additional methods will be implemented

    // May 2016

    class Utilities
    {
        

        const double err = 1.0e-9;

        public static double Err
        {
            get { return err; }
        }


        public static double TolPointOnPlane
        {
            get
            {
                return err;
            }
        }
        public static double MinimumLineLength
        {
            get { return err; }
        }

        public static bool IsZero(double a,double tolerance)
        {
            return tolerance > Math.Abs(a);
        }

        public static bool IsZero(double a)
        {
            return IsZero(a, err);
        }

        public static bool IsEqual (double a,double b)
        {
            return IsZero(b - a);
        }

        public static int Compare(double a, double b)
        {
            return IsEqual(a, b) ? 0 : (a < b ? -1 : 1);
        }

        public static int Compare(RvtDB.XYZ p, RvtDB.XYZ q)
        {
            int d = Compare(p.X, q.X);
            if ( 0 == d)
            {
                d = Compare(p.Y, q.Y);
                if (0  == d)
                {
                    d = Compare(p.Z, q.Z);
                }
            }
            return d;
        }

        // Compare planes

        public static bool IsEqual(RvtDB.XYZ p,RvtDB.XYZ q)
        {
            return 0 == Compare(p, q);
        }

        bool IsPerpendicular (RvtDB.XYZ v, RvtDB.XYZ w)
        {
            double a = v.GetLength();
            double b = w.GetLength();
            double c = Math.Abs(v.DotProduct(w));
            return err < a && err < b && err > c;


        }

        static bool XYZ_Parallel(RvtDB.XYZ a, RvtDB.XYZ b)
        {
            double angle = a.AngleTo(b);
            return err > angle || IsEqual(angle, Math.PI);
        }

        public static string RealString(double a)
        {
            return a.ToString("0.##");
        }
        /// <summary>
        /// Return a string for a UV point
        /// or vector with its coordinates
        /// formatted to two decimal places.
        /// </summary>
        public static string PointString(RvtDB.UV p)
        {
            return string.Format("({0},{1})",
              RealString(p.U),
              RealString(p.V));
        }

        public static bool IsHorizontal(RvtDB.XYZ v)
        {
            return IsZero(v.Z);
        }

        public static bool IsHorizontal(RvtDB.PlanarFace f)
        {
            return IsVertical(f.FaceNormal);
        }

        public static bool IsVertical(RvtDB.XYZ v)
        {
            return IsZero(v.X) && IsZero(v.Y);
        }

        public static bool GetSelectedElementsOrAll(
       List<RvtDB.Element> a,
       RvtUI.UIDocument uidoc,
       Type t)
        {
            RvtDB.Document doc = uidoc.Document;

            ICollection<RvtDB.ElementId> ids
              = uidoc.Selection.GetElementIds();

            if (0 < ids.Count)
            {
                a.AddRange(ids
                  .Select<RvtDB.ElementId, RvtDB.Element>(
                    id => doc.GetElement(id))
                  .Where<RvtDB.Element>(
                    e => t.IsInstanceOfType(e)));
            }
            else
            {
                a.AddRange(new RvtDB.FilteredElementCollector(doc)
                  .OfClass(t));
            }
            return 0 < a.Count;
        }

    }

}