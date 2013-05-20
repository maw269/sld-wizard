using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SldWizard
{
    class Sld
    {
        public static String OutputPath { get; set; }

        public enum SldSymbolizerType
        {
            None = 0,
            Point = 1,
            Line = 2,
            Area = 3,
            Text = 4
        }

        public enum SldRelationalOperator
        {
            PropertyIsEqualTo = 1,
            PropertyIsNotEqualTo = 2,
            PropertyIsLessThan = 3,
            PropertyIsGreaterThan = 4,
            PropertyIsLessThanOrEqualTo = 5,
            PropertyIsGreaterThanOrEqualTo = 6
        }

        public enum SldLogicalOperator
        {
            None = 0,
            or = 1,
            AND = 2,
            and = 3,
            OR = 4
        }

        public static Boolean IsValidSldOutputPath(String selectedPath)
        {
            if (selectedPath.Equals(""))
            {
                Console.WriteLine("SLD output path is required.");
                return false;
            }

            if (!Directory.Exists(selectedPath))
            {
                Console.WriteLine(selectedPath + "does not exist.");
                return false;
            }

            Sld.OutputPath = selectedPath;

            return true;
        }

        public static void GenerateSldFiles(String pid)
        {
            // Load FullSym and AttExp data tables the first time this is run
            if (GeoSymRules.FullSym == null)
            {
                GeoSymRules.LoadGeoSymRules(pid);
            }

            OdbcConnection dbfConnection = new OdbcConnection();
            dbfConnection.ConnectionString
                = @"Driver={Microsoft dBase Driver (*.dbf)};SourceType=DBF;"
                + SpatialData.DbfPath + @"\;"
                + "Exclusive=No;Collate=Machine;NULL=NO;DELETED=NO;BACKGROUNDFETCH=NO;";
            dbfConnection.Open();
            OdbcCommand dbfCommand = dbfConnection.CreateCommand();
            DataTable dbfTable = new DataTable();

            DataTable dbfTableAllColumns = new DataTable();

            foreach (String dbfFile in SpatialData.DbfFiles)
            {
                Console.WriteLine(dbfFile);

                String fileNameWithoutExtension = Path.GetFileNameWithoutExtension(dbfFile);

                SldSymbolizerType symbolizerType = SldSymbolizerType.None;
                if (fileNameWithoutExtension.Substring(fileNameWithoutExtension.Length - 1).Equals("p"))
                {
                    symbolizerType = SldSymbolizerType.Point;
                }
                else if ((fileNameWithoutExtension.Substring(fileNameWithoutExtension.Length - 1).Equals("l"))
                    || (fileNameWithoutExtension.Substring(fileNameWithoutExtension.Length - 4).Equals("line")))
                {
                    symbolizerType = SldSymbolizerType.Line;
                }
                else if (fileNameWithoutExtension.Substring(fileNameWithoutExtension.Length - 1).Equals("a"))
                {
                    symbolizerType = SldSymbolizerType.Area;
                }

                if (symbolizerType != SldSymbolizerType.None)
                {
                    // Copy DBF file to temp directory, so that it can be queried (the issue is due to DOS 8.3 naming)
                    String dbfFileRelocated = @".\temp\spatial.dbf";
                    File.Copy(dbfFile, dbfFileRelocated, true);

                    // Load data into memory
                    dbfCommand.CommandText = "SELECT DISTINCT f_code, f_code_des FROM " + dbfFileRelocated + " ORDER BY f_code";

                    try
                    {
                        dbfTable.Load(dbfCommand.ExecuteReader());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        Console.WriteLine("Assuming query failed because dbf does not have f_code and/or f_code_des columns");
                    }

                    if (dbfTable.Rows.Count != 0)
                    {
                        // Get empty data table in order to get column names
                        dbfCommand.CommandText = "SELECT * FROM " + dbfFileRelocated + " WHERE 1 = 2";
                        try
                        {
                            dbfTableAllColumns.Load(dbfCommand.ExecuteReader());
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                        Sld.CreateSldFile(dbfTable, fileNameWithoutExtension, symbolizerType, dbfTableAllColumns);
                        dbfTable.Clear();
                        dbfTableAllColumns.Reset();
                    }
                }
            }
        }

        private static EnumerableRowCollection<DataRow> QueryFullSym(String fcode, SldSymbolizerType delin)
        {
            // Get fullsym rows where fcode equals the given fcode and delin = {1 (point); 2 (line); 3 (area)}
            // ordered by dispri, id
            EnumerableRowCollection<DataRow> fullSymRows =
                from row in GeoSymRules.FullSym.AsEnumerable()
                where (row.Field<String>("fcode") == fcode) && (row.Field<String>("delin") == ((Int32)delin).ToString())
                orderby row.Field<String>("dispri"), row.Field<String>("id")
                select row;

            return fullSymRows;
        }

        private static EnumerableRowCollection<DataRow> QueryAttExp(String fullsymId)
        {
            // Get attexp rows where fullsym.id = attexp.cond_index
            // ordered by seq
            EnumerableRowCollection<DataRow> attExpRows =
                from row in GeoSymRules.AttExp.AsEnumerable()
                where row.Field<String>("cond_index") == fullsymId
                orderby row.Field<String>("seq")
                select row;

            return attExpRows;
        }

        private static EnumerableRowCollection<DataRow> QueryGeoSymLineAreaAttr(String geosymCode)
        {
            // Get geosym-line-area-attr rows where geosym_code equals the given geosymCode
            // ordered by dispri, id
            EnumerableRowCollection<DataRow> geoSymLineAreaAttrRows =
                from row in GeoSymRules.GeoSymLineAreaAttr.AsEnumerable()
                where row.Field<String>("geosym_code") == geosymCode
                orderby row.Field<String>("geosym_code")
                select row;

            return geoSymLineAreaAttrRows;
        }

        private static void CreateSoundingSldFile(String filePrefix)
        {
            StreamReader reader = new StreamReader(GeoSymRules.SldAssembly.GetManifestResourceStream("SldWizard.Templates.template_hyd_soundp.sld"));
            string content = reader.ReadToEnd();
            reader.Close();

            content = Regex.Replace(content, "FILE_PREFIX", filePrefix);

            StreamWriter writer = new StreamWriter(Sld.OutputPath + "\\" + filePrefix + "_hyd_soundp.sld");
            writer.Write(content);
            writer.Close();
        }

        private static void CreateSldFile(DataTable dbfTable, String dbfFileNameWithoutExtension, SldSymbolizerType symbolizerType, DataTable dbfTableAllColumns)
        {
            LinkedList<String> logicalOperatorClosingTagLinkedList = new LinkedList<String>();
            LinkedList<String> filterLinkedList = new LinkedList<String>();
            LinkedList<String> featureTypeStyleLinkedList = new LinkedList<String>();
            LinkedList<String> pointHalfSizeScaleLinkedList = null;
            LinkedList<String> pointQuarterSizeScaleLinkedList = null;

            Match match = Regex.Match(dbfFileNameWithoutExtension, @"(^.*)_hyd_soundp", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                Sld.CreateSoundingSldFile(match.Groups[1].Value);
                return;
            }

            LinkedList<String> sldList = new LinkedList<String>();

            TextWriter textWriter = new StreamWriter(Sld.OutputPath + @"/" + dbfFileNameWithoutExtension + @".sld");
            Sld.ApplyTextFromEmbeddedTextResource(textWriter, "SldWizard.Templates.sld-header.txt");
            textWriter.WriteLine("".PadLeft(6) + "<sld:Name>" + dbfFileNameWithoutExtension + "</sld:Name>");

            if ((symbolizerType == SldSymbolizerType.Point) || (symbolizerType == SldSymbolizerType.Area))
            {
                // Save complete <sld:FeatureTypeStyle>...</sld:FeatureTypeStyle> for half size scale
                pointHalfSizeScaleLinkedList = new LinkedList<String>();

                // Save complete <sld:FeatureTypeStyle>...</sld:FeatureTypeStyle> for quarter size scale
                pointQuarterSizeScaleLinkedList = new LinkedList<String>();
            }

            EnumerableRowCollection<DataRow> fullSymRowsForFcode = null;

            // Combine all fullsym rows by display priority
            // DataTable attExpByDisplayPriority = GeoSymRules.AttExp.Clone();
            DataTable fullSymRowsCombined = GeoSymRules.FullSym.Clone();
            foreach (DataRow dataRow in dbfTable.Rows)
            {
                fullSymRowsForFcode = Sld.QueryFullSym(dataRow["f_code"].ToString(), symbolizerType);
                fullSymRowsForFcode.CopyToDataTable(fullSymRowsCombined, LoadOption.PreserveChanges);
            }

            // Query fullSymRowsCombined to produce fullSymRows enumerator ordered by Diplay Priority and ID
            EnumerableRowCollection<DataRow> fullSymRows =
                from row in fullSymRowsCombined.AsEnumerable()
                orderby row.Field<String>("dispri"), row.Field<String>("id")
                select row;

            featureTypeStyleLinkedList.AddLast("".PadLeft(6) + "<sld:FeatureTypeStyle>");

            String pointsym = "";
            foreach (DataRow fullSymRow in fullSymRows)
            {
                String linesym = "";
                String areasym = "";
                pointsym = "";

                Boolean columnFound = false;

                // LSH 2013-05-16
                Boolean labelAttribute = false;
                /////

                String labatt = fullSymRow["labatt"].ToString();

                // Do not process FeatureTypeStyle, if true
                if ((labatt == "ccc") || (labatt == "psc") || (labatt == "mcc") || (labatt == "sst") || (labatt == "psc,mcc"))
                {
                    continue;
                }

                // LSH 2013-05-16
                if ((labatt != "") && (labatt != "ccc") && (labatt != "psc") && (labatt != "mcc") && (labatt != "sst") && (labatt != "psc,mcc"))
                {
                    labelAttribute = true;
                }
                /////

                if (symbolizerType == SldSymbolizerType.Point)
                {
                    pointsym = fullSymRow["pointsym"].ToString();
                }
                else if (symbolizerType == SldSymbolizerType.Line)
                {
                    linesym = fullSymRow["linesym"].ToString();
                }
                else if (symbolizerType == SldSymbolizerType.Area)
                {
                    areasym = fullSymRow["areasym"].ToString();
                    linesym = fullSymRow["linesym"].ToString();
                    pointsym = fullSymRow["pointsym"].ToString();
                }

                String fcode = fullSymRow["fcode"].ToString();

                String ruleName = fcode + "-" + fullSymRow["id"].ToString();

                featureTypeStyleLinkedList.AddLast("".PadLeft(8) + "<sld:Rule>");
                featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<sld:Name>" + ruleName + "</sld:Name>");
                featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<sld:Title>" + fullSymRow["feadesc"].ToString() + "</sld:Title>");
                featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<ogc:Filter>");

                foreach (String featureTypeStyleElement in featureTypeStyleLinkedList)
                {
                    sldList.AddLast(featureTypeStyleElement);

                    // LSH 2013-05-16
                    //if (pointsym != "")
                    //{
                    //    pointHalfSizeScaleLinkedList.AddLast(featureTypeStyleElement);
                    //    pointQuarterSizeScaleLinkedList.AddLast(featureTypeStyleElement);
                    //}
                    if ((pointsym != "") || (labelAttribute))
                    {
                        if (pointHalfSizeScaleLinkedList != null)
                        {
                            pointHalfSizeScaleLinkedList.AddLast(featureTypeStyleElement);
                        }
                        if (pointQuarterSizeScaleLinkedList != null)
                        {
                            pointQuarterSizeScaleLinkedList.AddLast(featureTypeStyleElement);
                        }
                    }
                    /////
                }

                // LSH TO DO: I am clearing list hear for now, but may want to change this if processing featureTypeStyle at end
                featureTypeStyleLinkedList.Clear();
                /////

                EnumerableRowCollection<DataRow> attExpRows = Sld.QueryAttExp(fullSymRow["id"].ToString());

                String relationalOperator = "";

                // If there were no attexp rows, filter on f_code
                if (!attExpRows.Any())
                {
                    relationalOperator = SldRelationalOperator.PropertyIsEqualTo.ToString();

                    filterLinkedList.AddLast("<ogc:" + relationalOperator + ">");
                    filterLinkedList.AddLast("<ogc:PropertyName>f_code</ogc:PropertyName>");
                    filterLinkedList.AddLast("<ogc:Literal>" + fcode + "</ogc:Literal>");
                    filterLinkedList.AddLast("</ogc:" + relationalOperator + ">");

                    columnFound = true;
                }
                
                String lastProperty = "";
                String lastLogicalOperator = "None";
                String currentProperty = "";
                String currentLogicalOperator = "";
                Int32 logicalOperatorCode = -1;
                Int32 relationalOperatorCode = -1;
                String currentValue = "";
                CultureInfo cultureInfo = null;
                TextInfo textInfo = null;

                foreach (DataRow attExpRow in attExpRows)
                {
                    logicalOperatorCode = Int32.Parse(attExpRow["connector"].ToString());
                    currentLogicalOperator = ((SldLogicalOperator)logicalOperatorCode).ToString();
                    currentProperty = attExpRow["att"].ToString();
                    currentValue = attExpRow["value"].ToString();
                    relationalOperatorCode = Int32.Parse(attExpRow["oper"].ToString());
                    relationalOperator = ((SldRelationalOperator)relationalOperatorCode).ToString();


                    cultureInfo = Thread.CurrentThread.CurrentCulture;
                    textInfo = cultureInfo.TextInfo;

                    if (currentLogicalOperator.Equals("None"))
                    {
                        // Append filter condition
                        if (columnFound)
                        {
                            Sld.AppendFilterCondition(filterLinkedList, relationalOperator, currentProperty, currentValue, dbfTableAllColumns);
                        }
                        else
                        {
                            columnFound = Sld.AppendFilterCondition(filterLinkedList, relationalOperator, currentProperty, currentValue, dbfTableAllColumns);
                        }

                        // Append all remaining closing tags to the filter linked list
                        while (logicalOperatorClosingTagLinkedList.Count > 0)
                        {
                            filterLinkedList.AddLast(logicalOperatorClosingTagLinkedList.First.Value);
                            logicalOperatorClosingTagLinkedList.RemoveFirst();
                        }
                    }
                    else if (currentLogicalOperator.Equals("or") || currentLogicalOperator.Equals("and"))
                    {
                        filterLinkedList.AddLast("<ogc:" + currentLogicalOperator + ">");
                        logicalOperatorClosingTagLinkedList.AddFirst("</ogc:" + currentLogicalOperator + ">");

                        // Append filter condition
                        if (columnFound)
                        {
                            Sld.AppendFilterCondition(filterLinkedList, relationalOperator, currentProperty, currentValue, dbfTableAllColumns);
                        }
                        else
                        {
                            columnFound = Sld.AppendFilterCondition(filterLinkedList, relationalOperator, currentProperty, currentValue, dbfTableAllColumns);
                        }
                    }
                    else if (currentLogicalOperator.Equals("OR") || currentLogicalOperator.Equals("AND"))
                    {
                        // Append filter condition
                        if (columnFound)
                        {
                            Sld.AppendFilterCondition(filterLinkedList, relationalOperator, currentProperty, currentValue, dbfTableAllColumns);
                        }
                        else
                        {
                            columnFound = Sld.AppendFilterCondition(filterLinkedList, relationalOperator, currentProperty, currentValue, dbfTableAllColumns);
                        }

                        // Append current closing tags to filter list
                        while (logicalOperatorClosingTagLinkedList.Count > 0)
                        {
                            filterLinkedList.AddLast(logicalOperatorClosingTagLinkedList.First.Value);
                            logicalOperatorClosingTagLinkedList.RemoveFirst();
                        }

                        // Add parent AND or OR
                        filterLinkedList.AddFirst("<ogc:" + currentLogicalOperator + ">");
                        logicalOperatorClosingTagLinkedList.AddLast("</ogc:" + currentLogicalOperator + ">");
                    }

                    /////

                    lastProperty = currentProperty;
                    lastLogicalOperator = currentLogicalOperator;

                } // attexp

                Int32 logicalOperatorPadLeftIndent = 12;
                foreach (String filterElement in filterLinkedList)
                {
                    String filterElementToWrite = filterElement;

                    if (filterElement.Substring(0, 2).Equals("</"))
                    {
                        logicalOperatorPadLeftIndent = logicalOperatorPadLeftIndent - 2;
                    }

                    if (filterElement.ToLower().Contains(":and>"))
                    {
                        filterElementToWrite = filterElement.ToLower().Replace(":and>", ":And>");
                    }

                    if (filterElement.ToLower().Contains(":or>"))
                    {
                        filterElementToWrite = filterElement.ToLower().Replace(":or>", ":Or>");
                    }

                    sldList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + filterElementToWrite);

                    // LSH 2013-05-16
                    //if (pointsym != "")
                    //{
                    //    pointHalfSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + filterElementToWrite);
                    //    pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + filterElementToWrite);
                    //}
                    if ((pointsym != "") || (labelAttribute))
                    {
                        if (pointHalfSizeScaleLinkedList != null)
                        {
                            pointHalfSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + filterElementToWrite);
                        }
                        if (pointQuarterSizeScaleLinkedList != null)
                        {
                            pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + filterElementToWrite);
                        }
                    }
                    /////

                    if (!filterElement.Substring(0, 2).Equals("</"))
                    {
                        if (filterElement.Length > 13)
                        {
                            if (!filterElement.Substring(0, 13).Equals("<ogc:Literal>"))
                            {
                                if (filterElement.Length > 18)
                                {
                                    if (!filterElement.Substring(0, 18).Equals("<ogc:PropertyName>"))
                                    {
                                        logicalOperatorPadLeftIndent = logicalOperatorPadLeftIndent + 2;
                                    }
                                }
                                else
                                {
                                    logicalOperatorPadLeftIndent = logicalOperatorPadLeftIndent + 2;
                                }
                            }
                        }
                        else
                        {
                            logicalOperatorPadLeftIndent = logicalOperatorPadLeftIndent + 2;
                        }
                    }
                }

                filterLinkedList.Clear();

                foreach (String logicalOperatorClosingTag in logicalOperatorClosingTagLinkedList)
                {
                    String logicalOperatorClosingTagToWrite = logicalOperatorClosingTag;

                    logicalOperatorPadLeftIndent = logicalOperatorPadLeftIndent - 2;

                    if (logicalOperatorClosingTag.ToLower().Contains(":and>"))
                    {
                        logicalOperatorClosingTagToWrite = logicalOperatorClosingTag.ToLower().Replace(":and>", ":And>");
                    }

                    if (logicalOperatorClosingTag.ToLower().Contains(":or>"))
                    {
                        logicalOperatorClosingTagToWrite = logicalOperatorClosingTag.ToLower().Replace(":or>", ":Or>");
                    }

                    sldList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + logicalOperatorClosingTagToWrite);

                    // LSH 2013-05-16
                    //if (pointsym != "")
                    //{
                    //    pointHalfSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + logicalOperatorClosingTagToWrite);
                    //    pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + logicalOperatorClosingTagToWrite);
                    //}
                    if ((pointsym != "") || (labelAttribute))
                    {
                        if (pointHalfSizeScaleLinkedList != null)
                        {
                            pointHalfSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + logicalOperatorClosingTagToWrite);
                        }
                        if (pointQuarterSizeScaleLinkedList != null)
                        {
                            pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(logicalOperatorPadLeftIndent) + logicalOperatorClosingTagToWrite);
                        }
                    }
                    /////
                }

                logicalOperatorClosingTagLinkedList.Clear();

                String closingFilterTag = "".PadLeft(10) + "</ogc:Filter>";

                sldList.AddLast(closingFilterTag);

                // LSH 2013-05-16
                //if (pointsym != "")
                //{
                //    pointHalfSizeScaleLinkedList.AddLast(closingFilterTag);
                //    pointQuarterSizeScaleLinkedList.AddLast(closingFilterTag);

                //    // Full size
                //    sldList.AddLast("".PadLeft(10) + "<sld:MaxScaleDenominator>60000</sld:MaxScaleDenominator>");

                //    // Half size
                //    pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:MinScaleDenominator>60000</sld:MinScaleDenominator>");
                //    pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:MaxScaleDenominator>110000</sld:MaxScaleDenominator>");

                //    // Quarter size
                //    pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:MinScaleDenominator>110000</sld:MinScaleDenominator>");
                //}
                if ((pointsym != "") || (labelAttribute))
                {
                    // Full size
                    sldList.AddLast("".PadLeft(10) + "<sld:MaxScaleDenominator>60000</sld:MaxScaleDenominator>");

                    // Half size
                    if (pointHalfSizeScaleLinkedList != null)
                    {
                        pointHalfSizeScaleLinkedList.AddLast(closingFilterTag);
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:MinScaleDenominator>60000</sld:MinScaleDenominator>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:MaxScaleDenominator>110000</sld:MaxScaleDenominator>");
                    }

                    // Quarter size
                    if (pointQuarterSizeScaleLinkedList != null)
                    {
                        pointQuarterSizeScaleLinkedList.AddLast(closingFilterTag);
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:MinScaleDenominator>110000</sld:MinScaleDenominator>");
                    }
                }
                /////

                EnumerableRowCollection<DataRow> geosymRow = null;
                DataTable geosymDataTable = null;

                if (areasym != "")
                {
                    geosymRow = Sld.QueryGeoSymLineAreaAttr(areasym);

                    // Convert to data table, since there will only be one row
                    geosymDataTable = geosymRow.CopyToDataTable();

                    if (geosymDataTable.Rows[0]["feature_type"].Equals("AreaPlain"))
                    {
                        if (pointsym != "")
                        {
                            featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<sld:PolygonSymbolizer>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "<sld:Fill>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"fill\">" + geosymDataTable.Rows[0]["fill_color"].ToString() + "</sld:CssParameter>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "</sld:Fill>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "</sld:PolygonSymbolizer>");
                        }
                        else
                        {
                            sldList.AddLast("".PadLeft(10) + "<sld:PolygonSymbolizer>");
                            sldList.AddLast("".PadLeft(12) + "<sld:Fill>");
                            sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"fill\">" + geosymDataTable.Rows[0]["fill_color"].ToString() + "</sld:CssParameter>");
                            sldList.AddLast("".PadLeft(12) + "</sld:Fill>");
                            sldList.AddLast("".PadLeft(10) + "</sld:PolygonSymbolizer>");
                        }
                    }
                    else if (geosymDataTable.Rows[0]["feature_type"].Equals("AreaPattern"))
                    {
                        if (pointsym != "")
                        {
                            featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<sld:PolygonSymbolizer>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "<sld:Fill>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "<sld:GraphicFill>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(16) + "<sld:Graphic>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(18) + "<sld:ExternalGraphic>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(20) + "<sld:OnlineResource xlink:href=\"../www/img/geosym/" + fullSymRow["areasym"].ToString() + ".png\"/>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(20) + "<sld:Format>image/png</sld:Format>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(18) + "</sld:ExternalGraphic>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(16) + "</sld:Graphic>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "</sld:GraphicFill>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "</sld:Fill>");
                            featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "</sld:PolygonSymbolizer>");
                        }
                        else
                        {
                            sldList.AddLast("".PadLeft(10) + "<sld:PolygonSymbolizer>");
                            sldList.AddLast("".PadLeft(12) + "<sld:Fill>");
                            sldList.AddLast("".PadLeft(14) + "<sld:GraphicFill>");
                            sldList.AddLast("".PadLeft(16) + "<sld:Graphic>");
                            sldList.AddLast("".PadLeft(18) + "<sld:ExternalGraphic>");
                            sldList.AddLast("".PadLeft(20) + "<sld:OnlineResource xlink:href=\"../www/img/geosym/" + fullSymRow["areasym"].ToString() + ".png\"/>");
                            sldList.AddLast("".PadLeft(20) + "<sld:Format>image/png</sld:Format>");
                            sldList.AddLast("".PadLeft(18) + "</sld:ExternalGraphic>");
                            sldList.AddLast("".PadLeft(16) + "</sld:Graphic>");
                            sldList.AddLast("".PadLeft(14) + "</sld:GraphicFill>");
                            sldList.AddLast("".PadLeft(12) + "</sld:Fill>");
                            sldList.AddLast("".PadLeft(10) + "</sld:PolygonSymbolizer>");
                        }
                    }
                }

                if (linesym != "")
                {
                    geosymRow = Sld.QueryGeoSymLineAreaAttr(linesym);

                    // Convert to data table, since there will only be one row
                    geosymDataTable = geosymRow.CopyToDataTable();

                    // Determine line width based on proper units
                    String lineWidth = String.Format("{0:0.0}", Double.Parse(geosymDataTable.Rows[0]["line_width"].ToString()) / 0.3);

                    if (pointsym != "")
                    {
                        featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<sld:LineSymbolizer>");
                        featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "<sld:Stroke>");
                        featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"stroke\">" + geosymDataTable.Rows[0]["line_color"] + "</sld:CssParameter>");
                        featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"stroke-width\">" + lineWidth + "</sld:CssParameter>");
                        featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "</sld:Stroke>");
                        featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "</sld:LineSymbolizer>");
                    }
                    else
                    {
                        sldList.AddLast("".PadLeft(10) + "<sld:LineSymbolizer>");
                        sldList.AddLast("".PadLeft(12) + "<sld:Stroke>");
                        sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"stroke\">" + geosymDataTable.Rows[0]["line_color"] + "</sld:CssParameter>");
                        sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"stroke-width\">" + lineWidth + "</sld:CssParameter>");
                        sldList.AddLast("".PadLeft(12) + "</sld:Stroke>");
                        sldList.AddLast("".PadLeft(10) + "</sld:LineSymbolizer>");
                    }
                }

                String pngFileName = "";
                String size = "";
                if (pointsym != "")
                {
                    pngFileName = fullSymRow["pointsym"].ToString() + ".png";
                    Image geosymImage = Image.FromFile(@".\images\geosym-2ed-png\" + pngFileName);
                    size = String.Format("{0:0.0}", Convert.ToDouble(geosymImage.Height));

                    featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "<sld:PointSymbolizer>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "<sld:Graphic>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "<sld:ExternalGraphic>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(16) + "<sld:OnlineResource xlink:type=\"simple\"");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(18) + "xlink:href=\"../www/img/geosym/" + pngFileName + "\"/>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(16) + "<sld:Format>image/png</sld:Format>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "</sld:ExternalGraphic>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(14) + "<sld:Size>" + size + "</sld:Size>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(12) + "</sld:Graphic>");
                    featureTypeStyleLinkedList.AddLast("".PadLeft(10) + "</sld:PointSymbolizer>");

                    foreach (String featureTypeStyleElement in featureTypeStyleLinkedList)
                    {
                        sldList.AddLast(featureTypeStyleElement);

                        if (pointsym != "")
                        {
                            if (featureTypeStyleElement.Contains("<sld:Size>"))
                            {
                                Double halfSize = Double.Parse(size) / 2;
                                Double quarterSize = Double.Parse(size) / 4;
                                pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:Size>" + String.Format("{0:0.0}", halfSize.ToString()) + "</sld:Size>");
                                pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:Size>" + String.Format("{0:0.0}", quarterSize.ToString()) + "</sld:Size>");
                            }
                            else
                            {
                                pointHalfSizeScaleLinkedList.AddLast(featureTypeStyleElement);
                                pointQuarterSizeScaleLinkedList.AddLast(featureTypeStyleElement);
                            }
                        }
                    }

                    // LSH TO DO: I am clearing list hear for now, but may want to change this if processing featureTypeStyle at end
                    featureTypeStyleLinkedList.Clear();
                    /////
                }

                // LSH 2013-05-16
                // ccc, psc, mcc, and sst labels are skipped for now
                //String closingRuleTag = "".PadLeft(8) + "</sld:Rule>";
                //if ((labatt != "") && (labatt != "ccc") && (labatt != "psc") && (labatt != "mcc") && (labatt != "sst") && (labatt != "psc,mcc"))
                //{
                //    String[] labattArray = labatt.Split(',');

                //    // Full size
                //    sldList.AddLast("".PadLeft(10) + "<sld:TextSymbolizer>");
                //    sldList.AddLast("".PadLeft(12) + "<sld:Label>");
                //    foreach (String label in labattArray)
                //    {
                //        sldList.AddLast("".PadLeft(14) + "<ogc:PropertyName>" + label + "</ogc:PropertyName>");
                //    }
                //    sldList.AddLast("".PadLeft(12) + "</sld:Label>");
                //    sldList.AddLast("".PadLeft(12) + "<sld:Font>");
                //    sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-family\">");
                //    sldList.AddLast("".PadLeft(16) + "<ogc:Literal>Sans Serif</ogc:Literal>");
                //    sldList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //    sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-size\">");
                //    sldList.AddLast("".PadLeft(16) + "<ogc:Literal>10</ogc:Literal>");
                //    sldList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //    sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-style\">");
                //    sldList.AddLast("".PadLeft(16) + "<ogc:Literal>normal</ogc:Literal>");
                //    sldList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //    sldList.AddLast("".PadLeft(12) + "</sld:Font>");
                //    sldList.AddLast("".PadLeft(12) + "<sld:LabelPlacement>");
                //    sldList.AddLast("".PadLeft(14) + "<sld:PointPlacement>");
                //    sldList.AddLast("".PadLeft(16) + "<sld:Displacement>");
                //    sldList.AddLast("".PadLeft(18) + "<sld:DisplacementX>20</sld:DisplacementX>");
                //    sldList.AddLast("".PadLeft(18) + "<sld:DisplacementY>20</sld:DisplacementY>");
                //    sldList.AddLast("".PadLeft(16) + "</sld:Displacement>");
                //    sldList.AddLast("".PadLeft(14) + "</sld:PointPlacement>");
                //    sldList.AddLast("".PadLeft(12) + "</sld:LabelPlacement>");
                //    sldList.AddLast("".PadLeft(12) + "<sld:VendorOption name=\"spaceAround\">2</sld:VendorOption>");
                //    sldList.AddLast("".PadLeft(10) + "</sld:TextSymbolizer>");

                //    // Half size
                //    if (pointHalfSizeScaleLinkedList != null)
                //    {
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:TextSymbolizer>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Label>");
                //        foreach (String label in labattArray)
                //        {
                //            pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<ogc:PropertyName>" + label + "</ogc:PropertyName>");
                //        }
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Label>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Font>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-family\">");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>Sans Serif</ogc:Literal>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-size\">");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>10</ogc:Literal>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-style\">");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>normal</ogc:Literal>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Font>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:LabelPlacement>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:PointPlacement>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<sld:Displacement>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementX>10</sld:DisplacementX>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementY>10</sld:DisplacementY>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "</sld:Displacement>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:PointPlacement>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:LabelPlacement>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:VendorOption name=\"spaceAround\">2</sld:VendorOption>");
                //        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "</sld:TextSymbolizer>");
                //    }

                //    // Quarter size
                //    if (pointQuarterSizeScaleLinkedList != null)
                //    {
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:TextSymbolizer>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Label>");
                //        foreach (String label in labattArray)
                //        {
                //            pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<ogc:PropertyName>" + label + "</ogc:PropertyName>");
                //        }
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Label>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Font>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-family\">");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>Sans Serif</ogc:Literal>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-size\">");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>10</ogc:Literal>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-style\">");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>normal</ogc:Literal>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Font>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:LabelPlacement>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:PointPlacement>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<sld:Displacement>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementX>5</sld:DisplacementX>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementY>5</sld:DisplacementY>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "</sld:Displacement>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:PointPlacement>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:LabelPlacement>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:VendorOption name=\"spaceAround\">2</sld:VendorOption>");
                //        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(10) + "</sld:TextSymbolizer>");
                //    }
                //}
                //sldList.AddLast(closingRuleTag);
                String closingRuleTag = "".PadLeft(8) + "</sld:Rule>";
                if (labelAttribute)
                {
                    String[] labattArray = labatt.Split(',');

                    // Full size
                    sldList.AddLast("".PadLeft(10) + "<sld:TextSymbolizer>");
                    sldList.AddLast("".PadLeft(12) + "<sld:Label>");
                    foreach (String label in labattArray)
                    {
                        sldList.AddLast("".PadLeft(14) + "<ogc:PropertyName>" + label + "</ogc:PropertyName>");
                    }
                    sldList.AddLast("".PadLeft(12) + "</sld:Label>");
                    sldList.AddLast("".PadLeft(12) + "<sld:Font>");
                    sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-family\">");
                    sldList.AddLast("".PadLeft(16) + "<ogc:Literal>Sans Serif</ogc:Literal>");
                    sldList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                    sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-size\">");
                    sldList.AddLast("".PadLeft(16) + "<ogc:Literal>10</ogc:Literal>");
                    sldList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                    sldList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-style\">");
                    sldList.AddLast("".PadLeft(16) + "<ogc:Literal>normal</ogc:Literal>");
                    sldList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                    sldList.AddLast("".PadLeft(12) + "</sld:Font>");
                    sldList.AddLast("".PadLeft(12) + "<sld:LabelPlacement>");
                    sldList.AddLast("".PadLeft(14) + "<sld:PointPlacement>");
                    sldList.AddLast("".PadLeft(16) + "<sld:Displacement>");
                    sldList.AddLast("".PadLeft(18) + "<sld:DisplacementX>20</sld:DisplacementX>");
                    sldList.AddLast("".PadLeft(18) + "<sld:DisplacementY>20</sld:DisplacementY>");
                    sldList.AddLast("".PadLeft(16) + "</sld:Displacement>");
                    sldList.AddLast("".PadLeft(14) + "</sld:PointPlacement>");
                    sldList.AddLast("".PadLeft(12) + "</sld:LabelPlacement>");
                    sldList.AddLast("".PadLeft(12) + "<sld:VendorOption name=\"spaceAround\">2</sld:VendorOption>");
                    sldList.AddLast("".PadLeft(10) + "</sld:TextSymbolizer>");

                    // Half size
                    if (pointHalfSizeScaleLinkedList != null)
                    {
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:TextSymbolizer>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Label>");
                        foreach (String label in labattArray)
                        {
                            pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<ogc:PropertyName>" + label + "</ogc:PropertyName>");
                        }
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Label>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Font>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-family\">");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>Sans Serif</ogc:Literal>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-size\">");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>10</ogc:Literal>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-style\">");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>normal</ogc:Literal>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Font>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:LabelPlacement>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:PointPlacement>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "<sld:Displacement>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementX>10</sld:DisplacementX>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementY>10</sld:DisplacementY>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(16) + "</sld:Displacement>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:PointPlacement>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:LabelPlacement>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:VendorOption name=\"spaceAround\">2</sld:VendorOption>");
                        pointHalfSizeScaleLinkedList.AddLast("".PadLeft(10) + "</sld:TextSymbolizer>");
                    }

                    // Quarter size
                    if (pointQuarterSizeScaleLinkedList != null)
                    {
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(10) + "<sld:TextSymbolizer>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Label>");
                        foreach (String label in labattArray)
                        {
                            pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<ogc:PropertyName>" + label + "</ogc:PropertyName>");
                        }
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Label>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:Font>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-family\">");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>Sans Serif</ogc:Literal>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-size\">");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>10</ogc:Literal>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:CssParameter name=\"font-style\">");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<ogc:Literal>normal</ogc:Literal>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:CssParameter>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:Font>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:LabelPlacement>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "<sld:PointPlacement>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "<sld:Displacement>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementX>5</sld:DisplacementX>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(18) + "<sld:DisplacementY>5</sld:DisplacementY>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(16) + "</sld:Displacement>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(14) + "</sld:PointPlacement>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "</sld:LabelPlacement>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(12) + "<sld:VendorOption name=\"spaceAround\">2</sld:VendorOption>");
                        pointQuarterSizeScaleLinkedList.AddLast("".PadLeft(10) + "</sld:TextSymbolizer>");
                    }
                }
                sldList.AddLast(closingRuleTag);
                /////

                // LSH 2013-05-16
                //if (pointsym != "")
                //{
                //    pointHalfSizeScaleLinkedList.AddLast(closingRuleTag);
                //    pointQuarterSizeScaleLinkedList.AddLast(closingRuleTag);

                //    if (symbolizerType == SldSymbolizerType.Area)
                //    {
                //        // Append half-size scaling rules
                //        foreach (String featureTypeStyleElement in pointHalfSizeScaleLinkedList)
                //        {
                //            if ((symbolizerType == SldSymbolizerType.Area) && (featureTypeStyleElement != "".PadLeft(6) + "<sld:FeatureTypeStyle>"))
                //            {
                //                sldList.AddLast(featureTypeStyleElement);
                //            }
                //        }

                //        pointHalfSizeScaleLinkedList.Clear();

                //        // Append quarter-size scaling rules
                //        foreach (String featureTypeStyleElement in pointQuarterSizeScaleLinkedList)
                //        {
                //            if ((symbolizerType == SldSymbolizerType.Area) && (featureTypeStyleElement != "".PadLeft(6) + "<sld:FeatureTypeStyle>"))
                //            {
                //                sldList.AddLast(featureTypeStyleElement);
                //            }
                //        }

                //        pointQuarterSizeScaleLinkedList.Clear();
                //    }
                //}
                if ((pointsym != "") || (labelAttribute))
                {
                    if (pointHalfSizeScaleLinkedList != null)
                    {
                        pointHalfSizeScaleLinkedList.AddLast(closingRuleTag);
                    }
                    if (pointQuarterSizeScaleLinkedList != null)
                    {
                        pointQuarterSizeScaleLinkedList.AddLast(closingRuleTag);
                    }

                    if (symbolizerType == SldSymbolizerType.Area)
                    {
                        if (pointHalfSizeScaleLinkedList != null)
                        {
                            // Append half-size scaling rules
                            foreach (String featureTypeStyleElement in pointHalfSizeScaleLinkedList)
                            {
                                if ((symbolizerType == SldSymbolizerType.Area) && (featureTypeStyleElement != "".PadLeft(6) + "<sld:FeatureTypeStyle>"))
                                {
                                    sldList.AddLast(featureTypeStyleElement);
                                }
                            }

                            pointHalfSizeScaleLinkedList.Clear();
                        }

                        if (pointQuarterSizeScaleLinkedList != null)
                        {
                            // Append quarter-size scaling rules
                            foreach (String featureTypeStyleElement in pointQuarterSizeScaleLinkedList)
                            {
                                if ((symbolizerType == SldSymbolizerType.Area) && (featureTypeStyleElement != "".PadLeft(6) + "<sld:FeatureTypeStyle>"))
                                {
                                    sldList.AddLast(featureTypeStyleElement);
                                }
                            }

                            pointQuarterSizeScaleLinkedList.Clear();
                        }
                    }
                }
                /////

                foreach (String sldListElement in sldList)
                {
                    if (sldListElement.Contains("sld:FeatureTypeStyle"))
                    {
                        textWriter.WriteLine(sldListElement);
                    }
                    else if (columnFound)
                    {
                        textWriter.WriteLine(sldListElement);
                    }
                }
                sldList.Clear();

            } // fullsym

            String closingFeatureTypeStyleTag = "".PadLeft(6) + "</sld:FeatureTypeStyle>";

            if ((symbolizerType == SldSymbolizerType.Point) && pointHalfSizeScaleLinkedList.Any<String>())
            {
                // Append ElseFilter for regular size
                Sld.AppendElseFilter(textWriter, 64);
                textWriter.WriteLine(closingFeatureTypeStyleTag);

                if (pointHalfSizeScaleLinkedList.First.Value != "".PadLeft(6) + "<sld:FeatureTypeStyle>")
                {
                    textWriter.WriteLine("".PadLeft(6) + "<sld:FeatureTypeStyle>");
                }

                // Append half-size scaling rules
                foreach (String featureTypeStyleElement in pointHalfSizeScaleLinkedList)
                {
                    textWriter.WriteLine(featureTypeStyleElement);
                }

                // Append ElseFilter for half size
                Sld.AppendElseFilter(textWriter, 32);
                textWriter.WriteLine(closingFeatureTypeStyleTag);

                // Append quarter-size scaling rules
                foreach (String featureTypeStyleElement in pointQuarterSizeScaleLinkedList)
                {
                    textWriter.WriteLine(featureTypeStyleElement);
                }

                // Append ElseFilter for quarter size
                Sld.AppendElseFilter(textWriter, 16);
            }
            else if (symbolizerType == SldSymbolizerType.Area)
            {
                Sld.AppendElseFilter(textWriter, 64);
                Sld.AppendElseFilter(textWriter, 32);
                Sld.AppendElseFilter(textWriter, 16);
            }

            textWriter.WriteLine(closingFeatureTypeStyleTag);

            Sld.ApplyTextFromEmbeddedTextResource(textWriter, "SldWizard.Templates.sld-footer.txt");

            textWriter.Close();
        }

        private static void AppendElseFilter(TextWriter textWriter, Int32 size)
        {
            textWriter.WriteLine("".PadLeft(8) + "<sld:Rule>");
            textWriter.WriteLine("".PadLeft(10) + "<sld:ElseFilter/>");

            if (size.Equals(64))
            {
                textWriter.WriteLine("".PadLeft(10) + "<sld:MaxScaleDenominator>60000</sld:MaxScaleDenominator>");
            }
            else if (size.Equals(32))
            {
                textWriter.WriteLine("".PadLeft(10) + "<sld:MinScaleDenominator>60000</sld:MinScaleDenominator>");
                textWriter.WriteLine("".PadLeft(10) + "<sld:MaxScaleDenominator>110000</sld:MaxScaleDenominator>");
            }
            else if (size.Equals(16))
            {
                textWriter.WriteLine("".PadLeft(10) + "<sld:MinScaleDenominator>110000</sld:MinScaleDenominator>");
            }
            textWriter.WriteLine("".PadLeft(10) + "<sld:PointSymbolizer>");
            textWriter.WriteLine("".PadLeft(12) + "<sld:Graphic>");
            textWriter.WriteLine("".PadLeft(14) + "<sld:ExternalGraphic>");
            textWriter.WriteLine("".PadLeft(16) + "<sld:OnlineResource xlink:type=\"simple\"");
            textWriter.WriteLine("".PadLeft(18) + "xlink:href=\"../www/img/geosym/5000.png\"/>");
            textWriter.WriteLine("".PadLeft(16) + "<sld:Format>image/png</sld:Format>");
            textWriter.WriteLine("".PadLeft(14) + "</sld:ExternalGraphic>");
            textWriter.WriteLine("".PadLeft(14) + "<sld:Size>" + size + "</sld:Size>");
            textWriter.WriteLine("".PadLeft(12) + "</sld:Graphic>");
            textWriter.WriteLine("".PadLeft(10) + "</sld:PointSymbolizer>");
            textWriter.WriteLine("".PadLeft(8) + "</sld:Rule>");
        }

        private static Boolean AppendFilterCondition(LinkedList<String> filterLinkedList, String relationalOperator, String currentProperty, String currentValue, DataTable dbfTableAllColumns)
        {
            Boolean columnFound = false;
            foreach (DataColumn column in dbfTableAllColumns.Columns)
            {
                if (currentProperty.Equals(column.ColumnName))
                {
                    columnFound = true;
                    break;
                }
            }

            if (!columnFound && !currentProperty.Equals("isdm") && !currentProperty.Equals("idsm"))
            {
                relationalOperator = SldRelationalOperator.PropertyIsEqualTo.ToString();
            }

            // relational operator
            filterLinkedList.AddLast("<ogc:" + relationalOperator + ">");

            // Top part of condition
            if (currentProperty.Equals("isdm") || currentProperty.Equals("idsm"))
            {
                filterLinkedList.AddLast("<ogc:Literal>0</ogc:Literal>");
                columnFound = true;
            }
            else if (!columnFound)
            {
                filterLinkedList.AddLast("<ogc:Literal>0</ogc:Literal>");
            }
            else
            {
                filterLinkedList.AddLast("<ogc:PropertyName>" + currentProperty + "</ogc:PropertyName>");
            }

            // Bottom part of condition
            //
            // Hardcoded values for ssdc, msdc, and mssc; otherwise value from attexp is used
            //
            // When column is not found in database condition is set to 0 == 0
            //
            if (!columnFound && !currentProperty.Equals("isdm") && !currentProperty.Equals("idsm"))
            {
                filterLinkedList.AddLast("<ogc:Literal>1</ogc:Literal>");
            }
            else if (currentValue.Equals("ssdc"))
            {
                filterLinkedList.AddLast("<ogc:Literal>8</ogc:Literal>");
            }
            else if (currentValue.Equals("msdc"))
            {
                filterLinkedList.AddLast("<ogc:Literal>30</ogc:Literal>");
            }
            else if (currentValue.Equals("mssc"))
            {
                filterLinkedList.AddLast("<ogc:Literal>2</ogc:Literal>");
            }
            else if (currentValue.Equals("\"UNK\""))
            {
                filterLinkedList.AddLast("<ogc:Literal>UNK</ogc:Literal>");
            }
            else
            {
                filterLinkedList.AddLast("<ogc:Literal>" + currentValue + "</ogc:Literal>");
            }

            filterLinkedList.AddLast("</ogc:" + relationalOperator + ">");
            /////

            return columnFound;
        }

        private static void ApplyTextFromEmbeddedTextResource(TextWriter textWriter, String embeddedTextResource)
        {
            String lineOfText = "";

            TextReader textReader = new StreamReader(GeoSymRules.SldAssembly.GetManifestResourceStream(embeddedTextResource));

            while ((lineOfText = textReader.ReadLine()) != null)
            {
                textWriter.WriteLine(lineOfText);
            }
        }
    }
}
