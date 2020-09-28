using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static DatabaseAccess.DbTables;
using NetTopologySuite.Geometries;
using NetTopologySuite;

namespace DatabaseAccess
{
    public class GeometryConverter : JsonConverter<Geometry>
    {
        public override Geometry ReadJson(JsonReader reader, Type objectType, Geometry existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            NetTopologySuite.IO.WKTReader wkt = new NetTopologySuite.IO.WKTReader();
            return wkt.Read(reader.ReadAsString());
        }

        public override void WriteJson(JsonWriter writer, Geometry value, Newtonsoft.Json.JsonSerializer serializer)
        {
            writer.WriteValue(value.AsText());
        }
    }
}
