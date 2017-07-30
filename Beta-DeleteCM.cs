using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

using RvtDB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;




namespace SOFiSTiK.Analysis
{
    // The value of TransactionMode.Manual for the TransactionAttribute requests
    // that Revit not create a transaction automatically 
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    // NoLicCommand returns true for the license
    // Otherwise switch to FeaCommand
    public class DeleteCM_CR : NoLicCommand
    {
        protected override RvtUI.Result subExecute(RvtUI.ExternalCommandData cmdData, ref String message, RvtDB.ElementSet element_set)
        {
      
            return RvtUI.Result.Succeeded;
        }  
    }
}


