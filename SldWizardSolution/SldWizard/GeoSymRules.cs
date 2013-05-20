using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace SldWizard
{
    class GeoSymRules
    {
        #region Properties

        public static Assembly SldAssembly { get; set; }
        public static StreamReader SldTextStreamReader { get; set; }
        public static DataTable FullSym { get; set; }
        public static DataTable AttExp { get; set; }
        public static DataTable GeoSymLineAreaAttr { get; set; }

        #endregion Properties

        #region GeoSym Rules Methods

        public static void LoadGeoSymRules(String pid)
        {

            GeoSymRules.SldAssembly = Assembly.GetExecutingAssembly();
            GeoSymRules.LoadFullSym(pid);
            GeoSymRules.LoadAttExp();
            GeoSymRules.LoadGeoSymLineAreaAttr();
        }

        #endregion GeoSym Rules Methods

        #region FullSym Methods

        // Currently using the following fullsym columns:
        // - id (column A)
        // - pid (column B)
        // - fcode (column C)
        // - delin (column D)
        // - pointsym (column F)
        // - linesym (column G)
        // - areasym (column H)
        // - dispri (column I)
        // - labatt (column K)
        // - feadesc (colum Q)
        private static void LoadFullSym(String pid)
        {
            String lineOfText = "";
            Int32 loopCount = 0;

            GeoSymRules.InitializeFullSymDataTable();

            TextReader textReader = GeoSymRules.LoadRulesFile("SldWizard.Rules.fullsym.txt");
            while ((lineOfText = textReader.ReadLine()) != null)
            {
                loopCount++;
                Console.WriteLine(lineOfText);

                String[] lineArray = lineOfText.Split('|');

                if (lineArray[1] == pid) // see pid.txt in solution root
                {
                    DataRow row = GeoSymRules.FullSym.NewRow();
                    row["id"] = lineArray[0];
                    row["fcode"] = lineArray[2];
                    row["delin"] = lineArray[3];
                    row["pointsym"] = lineArray[5];
                    row["linesym"] = lineArray[6];
                    row["areasym"] = lineArray[7];
                    row["dispri"] = lineArray[8];
                    row["labatt"] = lineArray[10];
                    row["feadesc"] = lineArray[16];
                    GeoSymRules.FullSym.Rows.Add(row);
                }
            }

            Console.WriteLine("Done");
        }

        private static void InitializeFullSymDataTable()
        {
            GeoSymRules.FullSym = new DataTable();
            GeoSymRules.FullSym.Columns.Add("id");
            GeoSymRules.FullSym.Columns.Add("fcode");
            GeoSymRules.FullSym.Columns.Add("delin");
            GeoSymRules.FullSym.Columns.Add("pointsym");
            GeoSymRules.FullSym.Columns.Add("linesym");
            GeoSymRules.FullSym.Columns.Add("areasym");
            GeoSymRules.FullSym.Columns.Add("dispri");
            GeoSymRules.FullSym.Columns.Add("labatt");
            GeoSymRules.FullSym.Columns.Add("feadesc");
        }

        #endregion FullSym Methods

        #region AttExp Methods

        // Currently using all attexp columns
        private static void LoadAttExp()
        {
            String lineOfText = "";

            GeoSymRules.InitializeAttExpDataTable();

            TextReader textReader = GeoSymRules.LoadRulesFile("SldWizard.Rules.attexp.txt");

            while ((lineOfText = textReader.ReadLine()) != null)
            {
                Console.WriteLine(lineOfText);

                String[] lineArray = lineOfText.Split('|');

                DataRow row = GeoSymRules.AttExp.NewRow();
                row["cond_index"] = lineArray[0];

                // Pad left with zero for querying
                row["seq"] = lineArray[1].PadLeft(2, '0');

                row["att"] = lineArray[2];
                row["oper"] = lineArray[3];
                row["value"] = lineArray[4];
                row["connector"] = lineArray[5];
                GeoSymRules.AttExp.Rows.Add(row);
            }

            Console.WriteLine("Done");
        }

        private static void InitializeAttExpDataTable()
        {
            GeoSymRules.AttExp = new DataTable();
            GeoSymRules.AttExp.Columns.Add("cond_index");
            GeoSymRules.AttExp.Columns.Add("seq");
            GeoSymRules.AttExp.Columns.Add("att");
            GeoSymRules.AttExp.Columns.Add("oper");
            GeoSymRules.AttExp.Columns.Add("value");
            GeoSymRules.AttExp.Columns.Add("connector");
        }

        #endregion AttExp Methods

        #region GeoSymLineAreaAttr Methods

        // Currently using the following geosym-line-area-attr columns:
        // - geosym_code
        // - feature_type
        // - line_width
        // - line_color
        // - fill_color
        private static void LoadGeoSymLineAreaAttr()
        {
            String lineOfText = "";

            GeoSymRules.InitializeGeoSymLineAreaAttrDataTable();

            TextReader textReader = GeoSymRules.LoadRulesFile("SldWizard.Rules.geosym-line-area-attr.txt");

            while ((lineOfText = textReader.ReadLine()) != null)
            {
                Console.WriteLine(lineOfText);

                String[] lineArray = lineOfText.Split('|');

                DataRow row = GeoSymRules.GeoSymLineAreaAttr.NewRow();
                row["geosym_code"] = lineArray[0];
                row["feature_type"] = lineArray[1];
                row["line_width"] = lineArray[2];
                row["line_color"] = lineArray[3];
                row["fill_color"] = lineArray[6];
                GeoSymRules.GeoSymLineAreaAttr.Rows.Add(row);
            }

            Console.WriteLine("Done");
        }

        private static void InitializeGeoSymLineAreaAttrDataTable()
        {
            GeoSymRules.GeoSymLineAreaAttr = new DataTable();
            GeoSymRules.GeoSymLineAreaAttr.Columns.Add("geosym_code");
            GeoSymRules.GeoSymLineAreaAttr.Columns.Add("feature_type");
            GeoSymRules.GeoSymLineAreaAttr.Columns.Add("line_width");
            GeoSymRules.GeoSymLineAreaAttr.Columns.Add("line_color");
            GeoSymRules.GeoSymLineAreaAttr.Columns.Add("fill_color");
        }

        #endregion GeoSymLineAreaAttr Methods

        #region TextReader Methods

        private static void SkipToFirstLineOfData(TextReader textReader)
        {
            String lineOfText = "";

            while ((lineOfText = textReader.ReadLine()).Trim().Substring(0) != ";")
	        {
                Console.WriteLine(lineOfText);
	        }
        }

        private static TextReader LoadRulesFile(String embeddedTextFile)
        {
            GeoSymRules.SldTextStreamReader = new StreamReader(SldAssembly.GetManifestResourceStream(embeddedTextFile));
            GeoSymRules.SkipToFirstLineOfData(GeoSymRules.SldTextStreamReader);
            TextReader textReader = GeoSymRules.SldTextStreamReader;
            return textReader;
        }

        #endregion TextReader Methods
    }
}
