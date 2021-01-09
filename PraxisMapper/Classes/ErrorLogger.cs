using CoreComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes
{
    public static class ErrorLogger
    {
        public static void LogError(Exception ex)
        {
            var el = new DbTables.ErrorLog();
            el.Message = ex.Message;
            el.StackTrace = ex.StackTrace;
            el.LoggedAt = DateTime.Now;
            var db = new PraxisContext();
            db.ErrorLogs.Add(el);
            db.SaveChanges();
        }
    }
}
