using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PraxisCore.Support
{
    public class PlaceJsonConverter : JsonConverter<DbTables.Place>
    {
        public override DbTables.Place Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            DbTables.Place place = new DbTables.Place();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) 
                {
                    Place.PreTag(place); //Applies any styles on this server to the loaded data, so if we have changes they still get applied automatically.
                    return place;
                }

                // Get the key.
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException();
                }

                string? propertyName = reader.GetString();
                reader.Read();
                switch(propertyName)
                {
                    case "DrawSizeHint":
                        place.DrawSizeHint = reader.GetDouble();
                        break;
                    case "ElementGeometry":
                        place.ElementGeometry = GeometrySupport.GeometryFromWKB(reader.GetBytesFromBase64());
                        break;
                    //case "PlaceData":
                        //place.PlaceData = JsonSerializer.Deserialize<ICollection<DbTables.PlaceData>>(reader.GetString());
                        //break;
                    case "SourceItemID":
                        place.SourceItemID = reader.GetInt64();
                        break;
                    case "SourceItemType":
                        place.SourceItemType = reader.GetInt32();
                        break;
                    case "Tags":
                        place.Tags = JsonSerializer.Deserialize<ICollection<DbTables.PlaceTags>>(reader.GetString());
                        break;
                }
            }
            
            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, DbTables.Place value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("DrawSizeHint", value.DrawSizeHint);
            writer.WriteBase64String("ElementGeometry", value.ElementGeometry.AsBinary());
            //writer.WriteString("PlaceData", JsonSerializer.Serialize(value.PlaceData, options)); //Removed, this will get filled on load by the server.
            writer.WriteNumber("SourceItemID", value.SourceItemID);
            writer.WriteNumber("SourceItemType", value.SourceItemType);
            writer.WriteString("Tags", JsonSerializer.Serialize(value.Tags, options));
            writer.WriteEndObject();
        }
    }
}
