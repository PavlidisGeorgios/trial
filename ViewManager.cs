using System;
using System.Collections.Generic;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;
using RvtStr = Autodesk.Revit.DB.Structure;

namespace SOFiSTiK.Analysis
{
    public static class SettingAdjuster
    {
        public static void TurnOnAllGraphicsInActiveView(RvtDB.Document doc, RvtDB.View view)
        {
            foreach (RvtDB.Category cat in doc.ActiveView.Document.Settings.Categories)
            {
                if (view.CanCategoryBeHidden(cat.Id))
                {
                    view.SetCategoryHidden(cat.Id, false);
                }
            }
        }
        public static void TurnOnSpecificGraphicsInActiveView(RvtDB.Document doc, RvtDB.View view, RvtDB.Category cat)
        {
            if (view.CanCategoryBeHidden(cat.Id))
            {
                view.SetCategoryHidden(cat.Id, false);
            }
        } 
        public static void TurnOnAllAnalyticalGraphicsInActiveView(RvtDB.Document doc, RvtDB.View view)
        {
            foreach (RvtDB.Category cat in doc.ActiveView.Document.Settings.Categories)
            {
                if (view.CanCategoryBeHidden(cat.Id) && (cat.CategoryType == RvtDB.CategoryType.AnalyticalModel))
                {
                    view.SetCategoryHidden(cat.Id, false);
                }
            }
        }
        public static void TurnOnAllSimplifiedGraphicsInActiveView(RvtDB.Document doc, RvtDB.View view)
        {
            List<RvtDB.Category> my_category_list = new List<RvtDB.Category>();
            my_category_list.Add(doc.Settings.Categories.get_Item(RvtDB.BuiltInCategory.OST_WallAnalytical));
            my_category_list.Add(doc.Settings.Categories.get_Item(RvtDB.BuiltInCategory.OST_BeamAnalytical));
            my_category_list.Add(doc.Settings.Categories.get_Item(RvtDB.BuiltInCategory.OST_ColumnAnalytical));
            my_category_list.Add(doc.Settings.Categories.get_Item(RvtDB.BuiltInCategory.OST_FloorAnalytical));
            my_category_list.Add(doc.Settings.Categories.get_Item(RvtDB.BuiltInCategory.OST_GenericModel));

            foreach (RvtDB.Category cat in my_category_list)
            {
                if (view.CanCategoryBeHidden(cat.Id))
                {
                    view.SetCategoryHidden(cat.Id, false);
                }
            }
        }

    }


    public class ViewManager
    {
        private SimplifiedModel mysimplifiedmodel = null;
        private RvtDB.Document doc = null;
        private RvtDB.View my_active_view = null;

        public void HideFromView(RvtUI.UIDocument uidoc, SimplifiedModel simplified)
        {
            doc = uidoc.Document;
            mysimplifiedmodel = simplified;

            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Create view"))
            {
                my_active_view = uidoc.ActiveView;
                // Strange that you cannot isolate or unhide the elements in the same transaction 
                trans.Start();

                // Isolate the elements and enable the analytical model
                my_active_view.IsolateElementsTemporary(mysimplifiedmodel.get_elementIDs_of_simplified());
                my_active_view.AreAnalyticalModelCategoriesHidden = false;
 
                // SettingAdjuster.TurnOnAllGraphicsInActiveView(doc,myactiveview);
                //  TODO : Remove the Template from the View -> DO WE REALLY NEED IT ?
                RvtDB.Parameter par = my_active_view.GetParameters("View Template")[0];
                par.Set(new RvtDB.ElementId(-1));

                trans.Commit();
                }

            using (RvtDB.Transaction trans = new RvtDB.Transaction(doc, "Create view"))
            {
                my_active_view = uidoc.ActiveView;
                // Strange that you cannot isolate or unhide the elements in the same transaction 
                trans.Start();
                SettingAdjuster.TurnOnAllSimplifiedGraphicsInActiveView(doc, my_active_view);
                trans.Commit();
            }
            foreach (Floor fl in simplified.floors)
            {
                fl.get_CenterMass().CreateMarker(doc);
                fl.get_CenterRigidity().CreateMarker(doc);
            }
            foreach (Floor fl in simplified.floors)
            {
                fl.get_CenterMass().DeleteMarker(doc);
                fl.get_CenterRigidity().DeleteMarker(doc);
            }

        }
    }

}
