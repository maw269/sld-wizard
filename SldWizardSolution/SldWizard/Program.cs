using CommandLineParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SldWizard
{
    class Program
    {
        static void Main(string[] args)
        {
            String spatialDataPath = "";
            String sldOutputPath = "";
            String pid = "";

            args.Process(
                () => Console.WriteLine("Usage is d=<Spatial Data Path> o=<SLD Output Path> p=<pid>"),
                new CommandLine.Switch("d", val => spatialDataPath = String.Join(" ", val)),
                new CommandLine.Switch("o", val => sldOutputPath = String.Join(" ", val)),
                new CommandLine.Switch("p", val => pid = String.Join(" ", val))
            );

            // Validate paths
            if (!(SpatialData.IsValidSpatialDataPath(spatialDataPath) && Sld.IsValidSldOutputPath(sldOutputPath)))
            {
                return;
            }

            // Generate SLD files
            Sld.GenerateSldFiles(pid);
        }
    }
}
