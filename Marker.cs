using System;
using System.Linq;
using System.Collections.Generic;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;
using SOFiSTiK.DBUtils;

using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;

namespace SOFiSTiK.Analysis
{
    public class Marker  // : RvtDB.Point
    {
        public RvtDB.ElementId pointId { get; set; }
        public RvtDB.XYZ my_coord { get; set; }
        private static string markerName = "SOFiSTiK Reference Point";

        public Marker(RvtDB.XYZ coord)
        {
            my_coord = coord;
        }
        public void CreateMarker(RvtDB.Document doc)
        {
            // Collect all the Family Symbols of the project 
            RvtDB.FilteredElementCollector collector = new RvtDB.FilteredElementCollector(doc).OfClass(typeof(RvtDB.FamilySymbol));

            Func<RvtDB.FamilySymbol, bool> isTargetFamily = famSym => famSym.Name.Contains(markerName);

            RvtDB.FamilySymbol targetFamily
              = collector.OfType<RvtDB.FamilySymbol>()
                .Where<RvtDB.FamilySymbol>(isTargetFamily)
                .First<RvtDB.FamilySymbol>();

            RvtDB.FamilyInstance result = null;

            using (RvtDB.Transaction t = new RvtDB.Transaction(doc, " Insert the generic family instance "))
            {
                t.Start();
               
                if (!targetFamily.IsActive)
                {
                    targetFamily.Activate();
                }
                try
                {
                    // First, we need to check if that Family exists 
                    result = doc.Create.NewFamilyInstance(my_coord, targetFamily, RvtDB.Structure.StructuralType.NonStructural);
                    t.Commit();
                    pointId = result.Id;
                }
                catch (Exception)
                {
                    t.RollBack();
                }
            }
        }
        public void DeleteMarker(RvtDB.Document doc)
        {
            using (RvtDB.Transaction t = new RvtDB.Transaction(doc, " Insert the generic family instance "))
            {
                t.Start();
                try
                {
                    // First, we need to check if that Family exists 
                    doc.Delete(pointId);
                    t.Commit();
                }
                catch (Exception)
                {
                    t.RollBack();
                }
            }
        }

    }

}