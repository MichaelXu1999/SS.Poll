﻿using System.Data;
using SiteServer.Plugin;

namespace SS.Poll.Provider
{
    public class Dao
    {
        private readonly string _connectionString;
        private readonly IDataApi _helper;

        public Dao(string connectionString, IDataApi dataApi)
        {
            _connectionString = connectionString;
            _helper = dataApi;
        }

        public int GetIntResult(string sqlString)
        {
            var count = 0;

            using (var conn = _helper.GetConnection(_connectionString))
            {
                conn.Open();
                using (var rdr = _helper.ExecuteReader(conn, sqlString))
                {
                    if (rdr.Read() && !rdr.IsDBNull(0))
                    {
                        count = rdr.GetInt32(0);
                    }
                    rdr.Close();
                }
            }
            return count;
        }

        public int GetIntResult(string sqlString, IDataParameter[] parameters)
        {
            var count = 0;

            using (var conn = _helper.GetConnection(_connectionString))
            {
                conn.Open();
                using (var rdr = _helper.ExecuteReader(conn, sqlString, parameters))
                {
                    if (rdr.Read() && !rdr.IsDBNull(0))
                    {
                        count = rdr.GetInt32(0);
                    }
                    rdr.Close();
                }
            }
            return count;
        }
    }
}