using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SldWizard
{
    class SpatialData
    {
        public static String[] DbfFiles { get; set; }
        public static String DbfPath { get; set; }
        
        public static Boolean IsValidSpatialDataPath(String selectedPath)
        {
            if (selectedPath.Equals(""))
            {
                Console.WriteLine("Spatial data path is required.");
                return false;
            }

            if (!Directory.Exists(selectedPath))
            {
                Console.WriteLine(selectedPath + "does not exist.");
                return false;
            }

            SpatialData.DbfFiles = Directory.GetFiles(selectedPath, "*.dbf");

            if (SpatialData.DbfFiles.Length.Equals(0))
            {
                Console.WriteLine("No DBF files to process.");
                return false;
            }

            SpatialData.DbfPath = selectedPath;

            return true;
        }
    }
}
