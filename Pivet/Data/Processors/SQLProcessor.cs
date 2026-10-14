using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using System.IO;
using BasicSQLFormatter;

namespace Pivet.Data.Processors
{
    internal class SQLProcessor : IDataProcessor
    {
        public string ItemName => "SQL";
        public string ProcessorID => "SQLProcessor";
        public event ProgressHandler ProgressChanged;
        OracleConnection _conn;
        List<SQLItem> _items = new List<SQLItem>();

        public int LoadItems(OracleConnection conn, FilterConfig filters)
        {
            _conn = conn;
            using (var itemLoad = new OracleCommand())
            {
                itemLoad.Connection = conn;
                StringBuilder sb = new StringBuilder();
                if (filters.Projects != null && filters.Projects.Count > 0)
                {
                    sb.Append("select A.SQLID, A.SQLTYPE, A.LASTUPDOPRID, A.LASTUPDDTTM from PSSQLDEFN A, PSPROJECTITEM B WHERE B.OBJECTTYPE = 30 and B.OBJECTVALUE1 = A.SQLID and B.OBJECTVALUE2 = A.SQLTYPE AND B.PROJECTNAME in (");
                    for (var x = 0; x < filters.Projects.Count; x++)
                    {
                        sb.Append(":" + (x + 1) + ",");
                    }
                    sb.Length--;
                    sb.Append(")");


                    foreach (var proj in filters.Projects)
                    {
                        itemLoad.Parameters.Add(new OracleParameter() { OracleDbType = OracleDbType.Varchar2, Value = proj });
                    }
                }
                else
                {
                    //sb.Append("select A.FIELDNAME, A.FIELDVALUE, A.EFFDT, A.EFF_STATUS, A.XLATLONGNAME, A.XLATSHORTNAME, A.LASTUPDDTTM, A.LASTUPDOPRID from PSXLATITEM A");
                    sb.Append("select A.SQLID, A.SQLTYPE, A.LASTUPDOPRID, A.LASTUPDDTTM FROM PSSQLDEFN A");
                }
                itemLoad.CommandText = sb.ToString();

                using (var reader = itemLoad.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(3))
                        {
                            _items.Add(new SQLItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), DateTime.MinValue));
                        }
                        else
                        {
                            _items.Add(new SQLItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));
                        }
                    }
                }

                ApplyFilters(filters);
            }

            return _items.Count;
        }

        private void ApplyFilters(FilterConfig filters)
        {
            /* project filter is handled by load on HTML Objects */

            for (var x = _items.Count - 1; x >= 0; x--)
            {
                var shouldDiscard = true;

                if (filters.Prefixes == null || filters.Prefixes.Count == 0)
                {
                    shouldDiscard = false;
                }
                else
                {
                    foreach (var prfx in filters.Prefixes)
                    {
                        if (_items[x].SQLID.StartsWith(prfx))
                        {
                            shouldDiscard = false;
                            break;
                        }
                    }
                }

                if (!shouldDiscard && filters.IncludeOprids != null && filters.IncludeOprids.Count > 0)
                {
                    if (filters.IncludeOprids.Contains(_items[x].Oprid))
                    {
                        shouldDiscard = false;
                    }
                    else
                    {
                        shouldDiscard = true;
                    }
                }

                if (!shouldDiscard && filters.ExcludeOprids != null && filters.ExcludeOprids.Count > 0)
                {
                    if (filters.ExcludeOprids.Contains(_items[x].Oprid))
                    {
                        shouldDiscard = true;
                    }
                    else
                    {
                        shouldDiscard = false;
                    }
                }

                if (shouldDiscard)
                {
                    _items.RemoveAt(x);
                }
            }
        }

        private string GetFilePathForSQL(string rootFolder, string sqlid, string sqltype)
        {
            var sqlRoot = Path.Combine(rootFolder, sqltype.Equals("6") ? "App Engine XSLT" :"SQL Objects");
            var path = "";
            switch (sqltype)
            {
                case "0":
                    path = Path.Combine(sqlRoot, "Standalone", sqlid + ".sql");
                    break;
                case "1":
                    path = Path.Combine(sqlRoot, "App Engine", sqlid + ".sql");
                    break;
                case "2":
                    path = Path.Combine(sqlRoot, "Views", sqlid + ".sql");
                    break;
                case "5":
                    path = Path.Combine(sqlRoot, "Audits", sqlid + ".sql");
                    break;
                case "6":
                    path = Path.Combine(sqlRoot, sqlid + ".xslt");
                    break;
                default:
                    path = Path.Combine(sqlRoot, "Type " + sqltype, sqlid + ".sql");
                    break;

            }
            return path;
        }

        public void ProcessDeletes(string rootFolder)
        {
            var sqlRoot = Path.Combine(rootFolder, "SQL Objects");
            if (Directory.Exists(sqlRoot))
            {
                Directory.Delete(sqlRoot, true);
            }
        }

        private void ReportProgress(double progress)
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(new ProgressEvent() { Progress = progress });
            }
        }

        public List<ChangedItem> SaveToDisk(string rootFolder)
        {
            ReportProgress(0);
            var sqlRoot = Path.Combine(rootFolder, "SQL Objects");
            Directory.CreateDirectory(sqlRoot);
            List<ChangedItem> changedItems = new List<ChangedItem>();
            double total = _items.Count;
            double current = 0;
            if (total == 0)
            {
                ReportProgress(100);
            }
            foreach (var item in _items)
            {
                var baseFileName = GetFilePathForSQL(rootFolder, item.SQLID.CleanFileName(), item.SqlType);
                Directory.CreateDirectory(new FileInfo(baseFileName).Directory.FullName);

                /* One file per text: the default text keeps the plain name, and each platform, market or
                 * effective-dated version gets its own file next to it, e.g. NAME.sql and NAME.Microsoft.sql. */
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var texts = item.GetContents(_conn);
                if (texts.Count == 0)
                {
                    /* no text rows: keep writing an empty file, as before */
                    texts.Add(new SQLText());
                }
                foreach (var text in texts)
                {
                    var fileName = FileNameForText(baseFileName, text, usedNames);
                    try
                    {
                        var formattedText = new SQLFormatter(text.Text).Format();

                        File.WriteAllText(fileName, formattedText);
                    }catch(Exception ex)
                    {
                        File.WriteAllText(fileName, text.Text);
                    }

                    changedItems.Add(new ChangedItem(fileName, item.Oprid));
                }

                current++;
                ReportProgress(((int)(((current / total) * 10000)) / (double)100));

            }
            return changedItems;
        }

        /* PeopleTools database types (translate values of the DBTYPE field). A blank DBTYPE is the default
         * text, used on every platform that has no text of its own. */
        private static readonly Dictionary<string, string> PlatformNames = new Dictionary<string, string>
        {
            { "0", "SQLBase" }, { "1", "DB2" }, { "2", "Oracle" }, { "3", "Informix" }, { "4", "DB2_UNIX" },
            { "5", "AllBase" }, { "6", "Sybase" }, { "7", "Microsoft" }, { "8", "DB2_400" }
        };

        private static string FileNameForText(string baseFileName, SQLText text, HashSet<string> usedNames)
        {
            var parts = new List<string>();
            if (text.Market.Length > 0 && text.Market != "GBL")
            {
                parts.Add(text.Market);
            }
            if (text.EffDt.Length > 0 && text.EffDt != "1900-01-01")
            {
                parts.Add(text.EffDt);
            }
            if (text.DbType.Length > 0)
            {
                parts.Add(PlatformNames.TryGetValue(text.DbType, out var platform) ? platform : "DBTYPE" + text.DbType);
            }

            var extension = Path.GetExtension(baseFileName);
            var stem = baseFileName.Substring(0, baseFileName.Length - extension.Length);
            var fileName = parts.Count == 0 ? baseFileName : stem + "." + string.Join(".", parts) + extension;
            for (var n = 2; usedNames.Contains(fileName); n++)
            {
                fileName = stem + "." + string.Join(".", parts.Append(n.ToString())) + extension;
            }
            usedNames.Add(fileName);
            return fileName;
        }
    }

    class SQLItem
    {
        internal string SQLID;
        internal string SqlType;
        internal string Oprid;
        internal DateTime LastUpdate;

        public SQLItem(string id, string type, string opr, DateTime dttm)
        {
            SQLID = id;
            SqlType = type;
            Oprid = opr;
            LastUpdate = dttm;
        }

        /* Each text of this SQL object: one per market, database type and effective date. A long text is
         * stored in several rows (SEQNUM), which are joined in order. Reading by SQLID alone used to run
         * every platform's text together in one string, with no separator and in no fixed order. */
        internal List<SQLText> GetContents(OracleConnection conn)
        {
            var texts = new List<SQLText>();
            using (var cmd = new OracleCommand("SELECT MARKET, DBTYPE, TO_CHAR(EFFDT, 'YYYY-MM-DD'), SQLTEXT FROM PSSQLTEXTDEFN WHERE SQLID = :1 AND SQLTYPE = :2 ORDER BY MARKET, DBTYPE, EFFDT, SEQNUM", conn))
            {
                cmd.Parameters.Add(new OracleParameter() { OracleDbType = OracleDbType.Varchar2, Value = SQLID });
                cmd.Parameters.Add(new OracleParameter() { OracleDbType = OracleDbType.Varchar2, Value = SqlType });
                using (var reader = cmd.ExecuteReader())
                {
                    SQLText current = null;
                    MemoryStream data = null;
                    while (reader.Read())
                    {
                        var market = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                        var dbType = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                        var effDt = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        if (current == null || current.Market != market || current.DbType != dbType || current.EffDt != effDt)
                        {
                            if (current != null)
                            {
                                current.Text = Encoding.Unicode.GetString(data.ToArray()).Split('\0')[0];
                                texts.Add(current);
                            }
                            current = new SQLText { Market = market, DbType = dbType, EffDt = effDt };
                            data = new MemoryStream();
                        }
                        var blob = reader.GetOracleClob(3);
                        blob.CopyTo(data);
                        blob.Close();
                    }
                    if (current != null)
                    {
                        current.Text = Encoding.Unicode.GetString(data.ToArray()).Split('\0')[0];
                        texts.Add(current);
                    }
                }
            }

            return texts;
        }
    }

    class SQLText
    {
        internal string Market = "";
        internal string DbType = "";
        internal string EffDt = "";
        internal string Text = "";
    }
}
